using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MenuItem = System.Windows.Controls.MenuItem;

namespace BPet;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _dragging;
    private bool _exitRequested;
    private PetState _state = PetState.Idle;
    private const int GwlExStyle = -20, WsExTransparent = 0x20;

    public MainWindow(AppServices services)
    {
        InitializeComponent(); _services = services;
        Loaded += (_, _) => { Left = _services.Settings.General.Left; Top = _services.Settings.General.Top; ApplyOptions(); };
        LocationChanged += (_, _) => { _services.Settings.General.Left = Left; _services.Settings.General.Top = Top; };
        _speechTimer.Tick += (_, _) => { SpeechBubble.Visibility = Visibility.Collapsed; SetState(PetState.Idle); _speechTimer.Stop(); };
    }

    public void ApplyOptions()
    {
        Topmost = _services.Settings.General.AlwaysOnTop;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, _services.Settings.General.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent);
    }

    public void ShowSpeech(string text, PetState state = PetState.Talking)
    {
        SpeechText.Text = text; SpeechBubble.Visibility = Visibility.Visible; SetState(state); _speechTimer.Stop(); _speechTimer.Start();
    }
    public void RequestExit() => _exitRequested = true;
    private void SetState(PetState state) { _state = state; StateText.Text = $"{_services.Settings.Personality.PetName} · {state}"; }
    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragging = true; SetState(PetState.Dragged); CaptureMouse(); }
    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (_dragging && e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _dragging = false; ReleaseMouseCapture(); SetState(PetState.Idle); }
    private void Pet_MouseDoubleClick(object sender, MouseButtonEventArgs e) => new ChatWindow(_services).Show();
    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Chat", () => new ChatWindow(_services).Show()));
        menu.Items.Add(Item("Cài đặt", () => new SettingsWindow(_services).Show()));
        menu.Items.Add(Item(_services.Settings.General.AlwaysOnTop ? "Bỏ luôn trên cùng" : "Luôn trên cùng", () => { _services.Settings.General.AlwaysOnTop = !_services.Settings.General.AlwaysOnTop; ApplyOptions(); }));
        menu.Items.Add(Item("Ngủ một lát", () => ShowSpeech("Em ngủ một lát nha.", PetState.Sleep)));
        menu.IsOpen = true;
    }
    private static MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_exitRequested) { e.Cancel = true; Hide(); } _services.Save(); }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
