using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BPet;

public partial class ChatDock : Window
{
    private readonly AppServices _services;
    private readonly DesktopAssistant _assistant;
    private readonly List<ChatMessage> _history = new();
    private readonly string _historyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "chat-history.json");
    private CancellationTokenSource? _request;
    private bool _busy;
    private TaskCompletionSource<bool>? _idle;
    private string? _failed;

    public ChatDock(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _assistant = new DesktopAssistant(services);
        PetTitle.Text = services.Settings.Personality.PetName;
        RefreshProvider();
        try
        {
            if (File.Exists(_historyPath)) _history.AddRange((JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(_historyPath)) ?? new()).TakeLast(100));
        }
        catch { Status.Text = "Chưa đọc được hội thoại cũ; vẫn có thể chat mới."; }
        foreach (var message in _history) Draw(message.Role, message.Content);
        if (_history.Count == 0) Draw("assistant", "Em ở đây. Anh có thể trò chuyện hoặc nhờ em mở ứng dụng, nhắc việc, khôi phục trình duyệt. Bấm ‘Các lệnh’ để xem ví dụ.");
        Loaded += (_, _) => { Place(); Input.Focus(); LogScroll.ScrollToEnd(); };
        Closed += (_, _) => _request?.Cancel();
    }

    private void RefreshProvider() => ProviderLabel.Text = $"Trợ lý trên máy · {_services.CurrentProvider().DisplayName} · v{new UpdateService().CurrentVersion.ToString(3)}";
    public void Place()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 32);
        Left = Math.Max(area.Left, area.Right - Width - 22);
        Top = Math.Max(area.Top, area.Bottom - Height - 14);
    }
    public void Ask(string text) { if (_busy) { Draw("local", "Đang xử lý yêu cầu khác; hãy gửi lại việc này sau: " + text); return; } Input.Text = text; _ = SendAsync(); }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async Task SendAsync(string? retry = null)
    {
        var text = (retry ?? Input.Text).Trim();
        if (_busy)
        {
            if (!DesktopAssistant.IsLocalRequest(text)) { Status.Text = "Đang trả lời. Bấm Dừng hoặc đợi xong để gửi câu hỏi mới."; return; }
            var waiting = _idle?.Task;
            _request?.Cancel();
            if (waiting is not null) await waiting;
            await SendAsync(text);
            return;
        }
        if (text.Length == 0) return;
        if (text.Length > 12000) { Status.Text = "Tin nhắn quá dài. Hãy chia thành đoạn ngắn hơn."; return; }
        Input.Clear();
        Draw("user", text);
        _busy = true; _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); SendButton.IsEnabled = true; CancelButton.Visibility = Visibility.Visible; RetryButton.Visibility = Visibility.Collapsed;
        _request = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        RefreshProvider();
        Status.Text = "Đang thực hiện trên máy…";
        try
        {
            var local = await _assistant.TryRunAsync(text, this, _request.Token);
            var role = local is null ? "assistant" : "local";
            string reply;
            if (local is not null) reply = local;
            else
            {
                Status.Text = "Đang hỏi AI… Lệnh trên máy vẫn có thể gửi ngay."; 
                var context = _history.Where(m => m.Role is "user" or "assistant").TakeLast(30).Append(new ChatMessage("user", text)).ToList();
                var prompt = _services.PromptBuilder.Build(_services.Settings.Personality) + "\nBạn chỉ trả lời trò chuyện. Các thao tác Windows được bộ lệnh riêng xử lý. Không khẳng định đã mở ứng dụng, tắt máy hay sửa file khi bạn không có kết quả thực thi. Không tự bịa khả năng điều khiển máy.";
                reply = await _services.CurrentProvider().CompleteAsync(prompt, context, _request.Token);
                if (string.IsNullOrWhiteSpace(reply)) throw new IOException("AI trả về nội dung trống. Bấm Thử lại.");
            }
            _history.Add(new(local is null ? "user" : "local-user", text));
            _history.Add(new(role, reply));
            SaveHistory();
            Draw(role, reply);
            _failed = null;
            Status.Text = local is null ? "Đã trả lời" : "Đã xử lý lệnh tiện ích";
        }
        catch (OperationCanceledException) { Draw("error", "Đã dừng hoặc hết thời gian chờ. Bạn có thể thử lại."); _failed = text; }
        catch (Exception error) { Draw("error", error.Message); _failed = text; }
        finally
        {
            _request.Dispose(); _request = null;
            _busy = false; SendButton.IsEnabled = true; CancelButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = _failed is null ? Visibility.Collapsed : Visibility.Visible;
            if (_failed is not null) Status.Text = "Chưa hoàn tất · có thể thử lại hoặc vào Cài đặt";
            Input.Focus();
            _idle?.TrySetResult(true);
        }
    }
    private void SaveHistory()
    {
        if (_history.Count > 200) _history.RemoveRange(0, _history.Count - 200);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!); File.WriteAllText(_historyPath, JsonSerializer.Serialize(_history)); }
        catch { Status.Text = "Đã trả lời nhưng chưa lưu được hội thoại trên máy."; }
    }
    private void Draw(string role, string text)
    {
        var user = role is "user" or "local-user";
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = user ? _services.Settings.Personality.UserName : role == "error" ? "Chưa hoàn tất" : _services.Settings.Personality.PetName, Foreground = new SolidColorBrush(user ? System.Windows.Media.Colors.White : System.Windows.Media.Color.FromRgb(117, 98, 144)), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 14, Foreground = user ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 38, 62)), IsReadOnlyCaretVisible = false });
        Messages.Children.Add(new Border { Child = stack, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(user ? 52 : 0, 0, user ? 0 : 35, 12), Background = new SolidColorBrush(user ? System.Windows.Media.Color.FromRgb(119, 86, 213) : role == "error" ? System.Windows.Media.Color.FromRgb(255, 237, 233) : System.Windows.Media.Colors.White) });
        if (Messages.Children.Count > 220) Messages.Children.RemoveAt(0);
        LogScroll.ScrollToEnd();
    }
    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await SendAsync(); } }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (_failed is { } text) await SendAsync(text); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
    private void Settings_Click(object sender, RoutedEventArgs e) { var window = new SettingsWindow(_services); window.Closed += (_, _) => RefreshProvider(); window.Show(); }
    private void Help_Click(object sender, RoutedEventArgs e) => Draw("local", DesktopAssistant.Help);
    private void Browser_Click(object sender, RoutedEventArgs e) { try { BrowserIntegration.ShowSetup(this); } catch (Exception error) { Draw("error", error.Message); } }
}
