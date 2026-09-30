using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BPet;

public partial class ChatWindow : Window
{
    private readonly AppServices _services; private readonly List<ChatMessage> _history = new();
    public ChatWindow(AppServices services) { InitializeComponent(); _services = services; ProviderLabel.Text = _services.CurrentProvider().DisplayName; Add("assistant", $"{_services.Settings.Personality.PetName} ở đây nè. Bạn cần mình giúp gì?"); }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async Task SendAsync()
    {
        var text = Input.Text.Trim(); if (text.Length == 0) return; Input.Clear(); Add("user", text); _history.Add(new("user", text));
        try { var reply = await _services.CurrentProvider().CompleteAsync(_services.PromptBuilder.Build(_services.Settings.Personality), _history, CancellationToken.None); _history.Add(new("assistant", reply)); Add("assistant", reply); ((MainWindow)Application.Current.MainWindow).ShowSpeech(reply, PetState.Talking); }
        catch { Add("assistant", "Mình chưa kết nối được AI. Bạn kiểm tra lại API key hoặc mạng trong Cài đặt nhé."); }
    }
    private void Add(string role, string text) => Messages.Children.Add(new Border { Background = role == "user" ? System.Windows.Media.Brushes.MediumPurple : System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(role == "user" ? 70 : 0, 5, role == "user" ? 0 : 70, 5), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = role == "user" ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.MidnightBlue } });
    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await SendAsync(); } }
}
