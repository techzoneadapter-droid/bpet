using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using MenuItem = System.Windows.Controls.MenuItem;

namespace BPet;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _behaviorTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly PetStateMachine _stateMachine;
    private bool _dragging;
    private bool _exitRequested;
    private PetState _state = PetState.Idle;
    private const int GwlExStyle = -20, WsExTransparent = 0x20;

    public MainWindow(AppServices services)
    {
        InitializeComponent(); _services = services; _stateMachine = new PetStateMachine(services);
        Loaded += (_, _) => { RestoreVisiblePosition(); ApplyOptions(); ApplyCharacter(); Show(); Activate(); _behaviorTimer.Start(); };
        LocationChanged += (_, _) => { _services.Settings.General.Left = Left; _services.Settings.General.Top = Top; };
        _speechTimer.Tick += (_, _) => { SpeechBubble.Visibility = Visibility.Collapsed; SetState(PetState.Idle); _speechTimer.Stop(); };
        _behaviorTimer.Tick += (_, _) => RunBehavior();
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
        var saved = _services.Settings.General;
        var leftLimit = SystemParameters.VirtualScreenLeft;
        var topLimit = SystemParameters.VirtualScreenTop;
        var rightLimit = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width;
        var bottomLimit = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height;
        var desiredLeft = saved.Left;
        var desiredTop = saved.Top;
        var isOutside = double.IsNaN(desiredLeft) || double.IsNaN(desiredTop) || desiredLeft < leftLimit || desiredLeft > rightLimit || desiredTop < topLimit || desiredTop > bottomLimit;
        Left = isOutside ? Math.Max(leftLimit + 20, rightLimit - 28) : Math.Clamp(desiredLeft, leftLimit, rightLimit);
        Top = isOutside ? Math.Max(topLimit + 20, bottomLimit - 28) : Math.Clamp(desiredTop, topLimit, bottomLimit);
        saved.Left = Left; saved.Top = Top;
    }

    public void ApplyCharacter()
    {
        var selected = _services.Characters.Find(_services.Settings.General.CharacterId) ?? _services.Characters.All.First();
        _services.Settings.General.CharacterId = selected.Id;
        var colors = selected.Id switch
        {
            "bpet-chibi" => ("#FF8FA3", "#E25B77"),
            "bpet-knight" => ("#496F9D", "#2D466B"),
            _ => ("#8D72F8", "#7055D6")
        };
        PetBody.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(colors.Item1)!;
        LeftEar.Fill = RightEar.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(colors.Item2)!;
        SetState(PetState.Idle);
    }

    public void ShowSpeech(string text, PetState state = PetState.Talking)
    {
        SpeechText.Text = text; SpeechBubble.Visibility = Visibility.Visible; SetState(state); _speechTimer.Stop(); _speechTimer.Start();
    }
    public void RequestExit() => _exitRequested = true;
    private void SetState(PetState state) { _state = state; StateText.Text = $"{_services.Settings.Personality.PetName} · {state}"; }
    private void RunBehavior()
    {
        if (_dragging || SpeechBubble.Visibility == Visibility.Visible) return;
        var next = _stateMachine.Decide();
        SetState(next);
        if (next == PetState.Walk)
        {
            var bounds = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth - Width, SystemParameters.VirtualScreenHeight - Height);
            Left = Math.Clamp(Left + 3, bounds.Left, bounds.Right);
        }
    }
    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragging = true; SetState(PetState.Dragged); CaptureMouse(); }
    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (_dragging && e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _dragging = false; ReleaseMouseCapture(); SetState(PetState.Idle); }
    private void Pet_MouseDoubleClick(object sender, MouseButtonEventArgs e) => new ChatWindow(_services).Show();
    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Chat", () => new ChatWindow(_services).Show()));
        menu.Items.Add(Item("Cài đặt", () => new SettingsWindow(_services).Show()));
        menu.Items.Add(Item("Kiểm tra cập nhật", () => new UpdateWindow().Show()));
        var profiles = new MenuItem { Header = "Tính cách" };
        foreach (var profile in _services.Settings.Profiles) profiles.Items.Add(Item(profile.Name, () => { _services.ActivatePersonalityProfile(profile); ShowSpeech($"Đã chuyển sang {profile.Name}.", PetState.Happy); }));
        profiles.Items.Add(Item("Chỉnh sửa profile…", () => new SettingsWindow(_services).Show()));
        menu.Items.Add(profiles);
        menu.Items.Add(Item(_services.Settings.General.AlwaysOnTop ? "Bỏ luôn trên cùng" : "Luôn trên cùng", () => { _services.Settings.General.AlwaysOnTop = !_services.Settings.General.AlwaysOnTop; ApplyOptions(); }));
        menu.Items.Add(Item("Ngủ một lát", () => ShowSpeech("Em ngủ một lát nha.", PetState.Sleep)));
        menu.IsOpen = true;
    }
    private static MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_exitRequested) { e.Cancel = true; Hide(); } _behaviorTimer.Stop(); _services.Save(); }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
