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
    private readonly DispatcherTimer _moveTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
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
    private string _clip = "idle";
    private double _vx;
    private double _vy;
    private double _phase;
    private double _clock;
    private DateTime _lastTick;
    private const double DesignWidth = 250;
    private const double DesignHeight = 390;
    private const double DesignImage = 384;
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
            _moveTimer.Start();
        };
        LocationChanged += (_, _) => { _services.Settings.General.Left = Left; _services.Settings.General.Top = Top; };
        _speechTimer.Tick += (_, _) => { SpeechBubble.Visibility = Visibility.Collapsed; _speechTimer.Stop(); };
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
        ApplySize();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, _services.Settings.General.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent);
    }

    public void SetSize(int percent)
    {
        percent = Math.Clamp(percent, 35, 140);
        _services.Settings.General.SizePercent = percent;
        ApplySize();
        _services.Save();
        ShowSpeech($"Cỡ {percent}%.");
    }

    private void ApplySize()
    {
        var percent = _services.Settings.General.SizePercent;
        if (percent is < 35 or > 140) percent = 48;
        var scale = percent / 100.0;
        Width = DesignWidth * scale;
        Height = DesignHeight * scale;
        PetImage.Width = DesignWidth * scale;
        PetImage.Height = DesignImage * scale;
        GroundShadow.Width = 92 * scale;
        GroundShadow.Height = Math.Max(6, 14 * scale);
        SpeechBubble.MaxWidth = Math.Max(120, 230 * scale);
        if (!IsLoaded || _dragging) return;
        var area = WorkAreaDip();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = area.Bottom - Height;
    }

    private void RestoreVisiblePosition()
    {
        var area = SystemParameters.WorkArea;
        var width = double.IsFinite(Width) && Width > 1 ? Width : DesignWidth * 0.48;
        var height = double.IsFinite(Height) && Height > 1 ? Height : DesignHeight * 0.48;
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

    private void Move()
    {
        var now = DateTime.UtcNow;
        if (_lastTick == default) _lastTick = now;
        var dt = Math.Clamp((now - _lastTick).TotalSeconds, 0.001, 0.05);
        _lastTick = now;
        _clock += dt;
        var scale = Math.Max(0.35, Height / DesignHeight);
        if (_dragging)
        {
            PoseDrag(scale);
            return;
        }
        var area = WorkAreaDip();
        var floor = area.Bottom - Height;
        switch (_act)
        {
            case Act.Walk:
                _vx = Approach(_vx, _dir * 150 * scale, dt, 260 * scale);
                Left += _vx * dt;
                _phase += dt * (1.15 + Math.Abs(_vx) / (220 * scale));
                Top = floor;
                Face(_vx);
                PoseWalk(scale);
                if (Left <= area.Left + 1) StartClimb(1, area);
                else if (Left >= area.Right - Width - 1) StartClimb(-1, area);
                else if (DateTime.Now >= _actUntil) Begin(Act.Idle, 2.4);
                break;
            case Act.Climb:
                Top -= 72 * scale * dt;
                _phase += dt * 2.1;
                PoseClimb(scale);
                if (Top <= _climbTop || DateTime.Now >= _actUntil)
                {
                    _dir = Left <= area.Left + Width ? 1 : -1;
                    Left = Math.Clamp(Left + _dir * 28 * scale, area.Left + 8, Math.Max(area.Left + 8, area.Right - Width - 8));
                    _vx = _dir * 40 * scale;
                    Begin(Act.Walk, 5);
                }
                break;
            case Act.Fall:
                _vy = Math.Min(_vy + 1700 * scale * dt, 900 * scale);
                Top = Math.Min(Top + _vy * dt, floor);
                PetTilt.Angle = Math.Clamp(_vy / (40 * scale), -14, 14) * -_dir;
                PetSquash.ScaleX = 1;
                PetSquash.ScaleY = 1 + Math.Min(0.08, _vy / 4000);
                if (Top >= floor - 0.5)
                {
                    _vy = 0;
                    PetSquash.ScaleY = 0.9;
                    PetSquash.ScaleX = 1.08;
                    Begin(Act.Idle, 2.2);
                }
                break;
            default:
                _vx = Approach(_vx, 0, dt, 340 * scale);
                if (Math.Abs(_vx) > 1) Left += _vx * dt;
                Top = floor;
                if (Math.Abs(_vx) > 22) PoseWalk(scale);
                else PoseRest(scale);
                if (DateTime.Now >= _actUntil && Math.Abs(_vx) < 10) Decide();
                break;
        }
        GroundShadow.Width = (92 * scale) * PetSquash.ScaleX;
    }

    private static double Approach(double value, double target, double dt, double accel)
    {
        var delta = target - value;
        var step = accel * dt;
        if (Math.Abs(delta) <= step) return target;
        return value + Math.Sign(delta) * step;
    }

    private void Face(double velocity)
    {
        if (Math.Abs(velocity) < 8) return;
        PetFlip.ScaleX = velocity > 0 ? -1 : 1;
    }

    private void PoseWalk(double scale)
    {
        if (_clips.TryGetValue("walk", out var frames) && frames.Length > 0)
        {
            var index = (int)(_phase * frames.Length) % frames.Length;
            if (index < 0) index += frames.Length;
            if (PetImage.Source != frames[index]) PetImage.Source = frames[index];
        }
        var step = _phase * Math.PI * 2;
        var lift = Math.Sin(step);
        PetBob.Y = lift * -4.2 * scale;
        PetBob.X = Math.Sin(step) * 1.1 * scale;
        PetTilt.Angle = Math.Sin(step) * 3.2;
        var plant = Math.Sin(step * 2);
        PetSquash.ScaleY = 1 + plant * 0.045;
        PetSquash.ScaleX = 1 - plant * 0.03;
    }

    private void PoseRest(double scale)
    {
        var slow = _act == Act.Sleep ? 0.85 : _act == Act.Sit ? 1.15 : 1.7;
        var breath = Math.Sin(_clock * slow);
        PetBob.X = Math.Sin(_clock * 0.6) * 0.6 * scale;
        PetBob.Y = breath * (_act == Act.Happy ? -2.2 : -1.8) * scale;
        PetTilt.Angle = _act == Act.Sleep ? 7 * Math.Sin(_clock * 0.35) : Math.Sin(_clock * 0.55) * 1.1;
        PetSquash.ScaleY = 1 + breath * (_act == Act.Sleep ? 0.012 : 0.02);
        PetSquash.ScaleX = 1 - breath * 0.012;
        if (_act == Act.Happy)
        {
            var hop = Math.Abs(Math.Sin(_clock * 7.5));
            PetBob.Y = -hop * 9 * scale;
            PetSquash.ScaleY = 1 + hop * 0.08;
            PetSquash.ScaleX = 1 - hop * 0.05;
            PetTilt.Angle = Math.Sin(_clock * 7.5) * 4;
        }
    }

    private void PoseClimb(double scale)
    {
        var reach = Math.Sin(_phase * Math.PI * 2);
        PetBob.Y = reach * -2.5 * scale;
        PetTilt.Angle = _dir * 10 + reach * 3;
        PetSquash.ScaleY = 1.04;
        PetSquash.ScaleX = 0.96;
        if (_clips.TryGetValue("climb", out var frames) && frames.Length > 0 && PetImage.Source != frames[0]) PetImage.Source = frames[0];
    }

    private void PoseDrag(double scale)
    {
        var wobble = Math.Sin(_clock * 9);
        PetBob.Y = -2 * scale;
        PetBob.X = wobble * 1.5 * scale;
        PetTilt.Angle = wobble * 6;
        PetSquash.ScaleX = 0.96;
        PetSquash.ScaleY = 1.05;
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
        if (act == Act.Fall) _vy = 80;
        if (act == Act.Walk) PetFlip.ScaleX = _dir > 0 ? -1 : 1;
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
        var size = new MenuItem { Header = $"Kích thước ({NormalizedSize()}%)" };
        foreach (var preset in new[] { 40, 55, 70, 100 }) size.Items.Add(Item(preset + "%", () => SetSize(preset)));
        size.Items.Add(Item("Nhỏ hơn", () => SetSize(_services.Settings.General.SizePercent - 8)));
        size.Items.Add(Item("To hơn", () => SetSize(_services.Settings.General.SizePercent + 8)));
        menu.Items.Add(size);
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

    private int NormalizedSize()
    {
        var percent = _services.Settings.General.SizePercent;
        return percent is < 35 or > 140 ? 48 : percent;
    }

    private static MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested) { e.Cancel = true; Hide(); }
        _moveTimer.Stop();
        _services.Save();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
