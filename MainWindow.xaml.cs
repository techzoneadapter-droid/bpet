using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MenuItem = System.Windows.Controls.MenuItem;

namespace BPet;

public partial class MainWindow : Window
{
    private enum Act { Idle, Walk, Sit, Sleep, Happy, Climb, Fall }

    private readonly AppServices _services;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _moveTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly Dictionary<string, BitmapImage[]> _clips = new();
    private readonly Random _random = new();
    private readonly string[] _lines =
    {
        "Mình ở đây với bạn nè.",
        "Đi dạo một vòng không?",
        "Vỗ đầu mình một cái đi.",
        "Hôm nay cũng được đó chứ.",
        "Đói thì cho mình ăn nha.",
        "Bạn làm việc đi, mình ngồi cạnh."
    };
    private bool _dragging;
    private bool _pressed;
    private bool _dragged;
    private bool _exitRequested;
    private System.Windows.Point _grab;
    private Act _act = Act.Idle;
    private DateTime _actUntil = DateTime.Now;
    private int _dir = -1;
    private int _frame;
    private int _bob;
    private string _clip = "idle";
    private double _climbTop;
    private const int GwlExStyle = -20, WsExTransparent = 0x20;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        Loaded += (_, _) =>
        {
            LoadClips();
            RestoreVisiblePosition();
            ApplyOptions();
            ApplyCharacter();
            RefreshStats();
            Show();
            Activate();
            _animTimer.Start();
            _moveTimer.Start();
        };
        LocationChanged += (_, _) => { _services.Settings.General.Left = Left; _services.Settings.General.Top = Top; };
        _speechTimer.Tick += (_, _) => { SpeechBubble.Visibility = Visibility.Collapsed; _speechTimer.Stop(); };
        _animTimer.Tick += (_, _) => Animate();
        _moveTimer.Tick += (_, _) => Move();
    }

    private void LoadClips()
    {
        _clips["idle"] = new[] { LoadFrame("idle") };
        _clips["walk"] = new[] { LoadFrame("walk1"), LoadFrame("walk2") };
        _clips["sit"] = new[] { LoadFrame("sit") };
        _clips["sleep"] = new[] { LoadFrame("sleep") };
        _clips["happy"] = new[] { LoadFrame("happy") };
        _clips["raised"] = new[] { LoadFrame("raised") };
        _clips["climb"] = new[] { LoadFrame("climb") };
        SetClip("idle");
    }

    private static BitmapImage LoadFrame(string name)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/{name}.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void ApplyOptions()
    {
        Topmost = _services.Settings.General.AlwaysOnTop;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, _services.Settings.General.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent);
    }

    private void RestoreVisiblePosition()
    {
        var area = SystemParameters.WorkArea;
        var width = double.IsFinite(Width) && Width > 1 ? Width : 250;
        var height = double.IsFinite(Height) && Height > 1 ? Height : 390;
        var desiredLeft = _services.Settings.General.Left;
        var left = double.IsFinite(desiredLeft) ? desiredLeft : area.Right - width - 28;
        var rightLimit = Math.Max(area.Left, area.Right - width);
        Left = Math.Clamp(left, area.Left, rightLimit);
        Top = area.Bottom - height;
        _services.Settings.General.Left = Left;
        _services.Settings.General.Top = Top;
    }

    public void ApplyCharacter()
    {
        var selected = _services.Characters.Find(_services.Settings.General.CharacterId) ?? _services.Characters.All.FirstOrDefault();
        if (selected is null) return;
        _services.Settings.General.CharacterId = selected.Id;
    }

    public void ShowSpeech(string text, PetState state = PetState.Talking)
    {
        SpeechText.Text = text;
        SpeechBubble.Visibility = Visibility.Visible;
        _speechTimer.Stop();
        _speechTimer.Start();
        if (state == PetState.Sleep) Begin(Act.Sleep, 8);
        else if (state == PetState.Happy) Begin(Act.Happy, 2.4);
    }

    public void RequestExit() => _exitRequested = true;

    private void Animate()
    {
        if (!_clips.TryGetValue(_clip, out var frames) || frames.Length == 0) return;
        _frame = (_frame + 1) % frames.Length;
        PetImage.Source = frames[_frame];
        _bob++;
        PetBob.Y = _clip is "idle" or "sit" ? Math.Sin(_bob * 0.55) * 2.5 : 0;
    }

    private void Move()
    {
        if (_dragging)
        {
            SetClip("raised");
            return;
        }
        var area = WorkAreaDip();
        var floor = area.Bottom - Height;
        switch (_act)
        {
            case Act.Walk:
                Left += _dir * 1.45;
                Top = floor;
                PetFlip.ScaleX = _dir > 0 ? -1 : 1;
                if (Left <= area.Left + 1) StartClimb(1, area);
                else if (Left >= area.Right - Width - 1) StartClimb(-1, area);
                else if (DateTime.Now >= _actUntil) Decide();
                break;
            case Act.Climb:
                Top -= 1.15;
                if (Top <= _climbTop || DateTime.Now >= _actUntil)
                {
                    _dir = Left <= area.Left + Width ? 1 : -1;
                    Left = Math.Clamp(Left + _dir * 36, area.Left + 8, Math.Max(area.Left + 8, area.Right - Width - 8));
                    Begin(Act.Walk, 5);
                }
                break;
            case Act.Fall:
                Top = Math.Min(Top + 26, floor);
                if (Top >= floor - 1) Begin(Act.Idle, 2.2);
                break;
            default:
                Top = floor;
                if (DateTime.Now >= _actUntil) Decide();
                break;
        }
    }

    private void Decide()
    {
        var stats = _services.Settings.PetBehavior;
        if (!stats.AutoMovement)
        {
            Begin(Act.Idle, 4);
            return;
        }
        stats.Hunger = Math.Min(100, stats.Hunger + 1);
        stats.Energy = Math.Max(0, stats.Energy - 1);
        if (stats.Energy < 22)
        {
            Begin(Act.Sleep, 12);
            ShowSpeech("Mình mệt, ngủ một chút nha.", PetState.Sleep);
            RefreshStats();
            return;
        }
        if (stats.Hunger > 78)
        {
            Begin(Act.Sit, 6);
            ShowSpeech("Đói bụng rồi… cho mình ăn đi.");
            RefreshStats();
            return;
        }
        var roll = _random.Next(100);
        if (roll < 48)
        {
            _dir = _random.Next(2) == 0 ? -1 : 1;
            Begin(Act.Walk, _random.Next(4, 9));
        }
        else if (roll < 72) Begin(Act.Sit, _random.Next(4, 8));
        else if (roll < 84) Begin(Act.Sleep, _random.Next(6, 11));
        else Begin(Act.Idle, _random.Next(3, 6));
        RefreshStats();
    }

    private void StartClimb(int leaveDir, Rect area)
    {
        _dir = leaveDir;
        PetFlip.ScaleX = leaveDir > 0 ? -1 : 1;
        _climbTop = Math.Max(area.Top + 24, Top - _random.Next(110, 200));
        Begin(Act.Climb, 7);
    }

    private void Begin(Act act, double seconds)
    {
        _act = act;
        _actUntil = DateTime.Now.AddSeconds(seconds);
        SetClip(act switch
        {
            Act.Walk => "walk",
            Act.Sit => "sit",
            Act.Sleep => "sleep",
            Act.Happy => "happy",
            Act.Climb => "climb",
            Act.Fall => "raised",
            _ => "idle"
        });
    }

    private void SetClip(string clip)
    {
        if (_clip == clip && PetImage.Source is not null) return;
        _clip = clip;
        _frame = 0;
        if (_clips.TryGetValue(clip, out var frames) && frames.Length > 0) PetImage.Source = frames[0];
    }

    private Rect WorkAreaDip()
    {
        var probe = new System.Drawing.Point((int)Math.Max(0, Left + 20), (int)Math.Max(0, Top + 20));
        var screen = System.Windows.Forms.Screen.FromPoint(probe);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return SystemParameters.WorkArea;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void Pat()
    {
        var stats = _services.Settings.PetBehavior;
        stats.Happiness = Math.Min(100, stats.Happiness + 6);
        stats.Affinity = Math.Min(100, stats.Affinity + 2);
        Begin(Act.Happy, 2.5);
        ShowSpeech(_random.Next(2) == 0 ? "Nựng đầu dễ chịu quá." : "Hehe, thêm cái nữa đi.");
        RefreshStats();
    }

    private void Nudge()
    {
        ShowSpeech(_lines[_random.Next(_lines.Length)]);
        if (_act is Act.Sleep or Act.Climb) Begin(Act.Idle, 2);
    }

    private void RefreshStats()
    {
        var stats = _services.Settings.PetBehavior;
        StatText.Text = $"Đói {stats.Hunger}   ·   Vui {stats.Happiness}   ·   Sức {stats.Energy}";
    }

    private void Feed_Click(object sender, RoutedEventArgs e)
    {
        var stats = _services.Settings.PetBehavior;
        stats.Hunger = Math.Max(0, stats.Hunger - 32);
        stats.Happiness = Math.Min(100, stats.Happiness + 10);
        stats.Energy = Math.Min(100, stats.Energy + 6);
        Begin(Act.Happy, 2.6);
        ShowSpeech("Ngon quá. Cảm ơn bạn.");
        RefreshStats();
        _services.Save();
    }

    private void Pat_Click(object sender, RoutedEventArgs e) => Pat();
    private void Sleep_Click(object sender, RoutedEventArgs e)
    {
        var stats = _services.Settings.PetBehavior;
        stats.Energy = Math.Min(100, stats.Energy + 18);
        Begin(Act.Sleep, 12);
        ShowSpeech("Mình ngủ một lát nha.", PetState.Sleep);
        RefreshStats();
    }

    private void Work_Click(object sender, RoutedEventArgs e)
    {
        var stats = _services.Settings.PetBehavior;
        stats.Energy = Math.Max(0, stats.Energy - 8);
        stats.Happiness = Math.Min(100, stats.Happiness + 4);
        Begin(Act.Sit, 7);
        ShowSpeech("Mình ngồi làm cùng bạn đây.");
        RefreshStats();
        _services.Save();
    }

    private void Chat_Click(object sender, RoutedEventArgs e) => new ChatWindow(_services).Show();

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _grab = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        if (!_dragged)
        {
            var here = e.GetPosition(this);
            if (Math.Abs(here.X - _grab.X) + Math.Abs(here.Y - _grab.Y) < 10) return;
            _dragged = true;
            _dragging = true;
            SetClip("raised");
        }
        var cursor = CursorDip();
        Left = cursor.X - _grab.X;
        Top = cursor.Y - _grab.Y;
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragged)
        {
            _dragging = false;
            _dragged = false;
            Begin(Act.Fall, 2);
            return;
        }
        if (e.ClickCount >= 2)
        {
            new ChatWindow(_services).Show();
            return;
        }
        var onPet = e.GetPosition(PetImage).Y;
        if (onPet < PetImage.ActualHeight * 0.42) Pat();
        else Nudge();
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ActionBar.Visibility = ActionBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RefreshStats();
        e.Handled = true;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Cài đặt", () => new SettingsWindow(_services).Show()));
        menu.Items.Add(Item(_services.Settings.General.LaunchWithWindows ? "Tắt khởi động cùng Windows" : "Bật khởi động cùng Windows", () =>
        {
            _services.Settings.General.LaunchWithWindows = !_services.Settings.General.LaunchWithWindows;
            WindowsStartup.Apply(_services.Settings.General.LaunchWithWindows);
            _services.Save();
            _services.Tray.SyncStartupItem();
            ShowSpeech(_services.Settings.General.LaunchWithWindows ? "Mình sẽ mở cùng Windows." : "Mình không tự mở cùng Windows nữa.");
        }));
        menu.Items.Add(Item("Kiểm tra cập nhật", () => new UpdateWindow().Show()));
        var profiles = new MenuItem { Header = "Tính cách" };
        foreach (var profile in _services.Settings.Profiles) profiles.Items.Add(Item(profile.Name, () => { _services.ActivatePersonalityProfile(profile); ShowSpeech($"Đã chuyển sang {profile.Name}.", PetState.Happy); }));
        profiles.Items.Add(Item("Chỉnh sửa profile…", () => new SettingsWindow(_services).Show()));
        menu.Items.Add(profiles);
        menu.Items.Add(Item(_services.Settings.General.AlwaysOnTop ? "Bỏ luôn trên cùng" : "Luôn trên cùng", () => { _services.Settings.General.AlwaysOnTop = !_services.Settings.General.AlwaysOnTop; ApplyOptions(); }));
        menu.PlacementTarget = (System.Windows.Controls.Button)sender;
        menu.IsOpen = true;
    }

    private System.Windows.Point CursorDip()
    {
        var point = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return new System.Windows.Point(point.X, point.Y);
        return source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(point.X, point.Y));
    }

    private static MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested) { e.Cancel = true; Hide(); }
        _animTimer.Stop();
        _moveTimer.Stop();
        _services.Save();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
