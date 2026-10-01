using System.Windows;
using System.Windows.Input;

namespace BPet;

public partial class ChatDock : Window
{
    private readonly AppServices _services;
    private readonly List<ChatMessage> _history = new();
    private bool _busy;

    public ChatDock(AppServices services)
    {
        InitializeComponent();
        _services = services;
        var persona = services.Settings.Personality;
        var self = string.IsNullOrWhiteSpace(persona.PetPronoun) ? "mình" : persona.PetPronoun.Trim();
        var user = string.IsNullOrWhiteSpace(persona.UserPronoun) ? "bạn" : persona.UserPronoun.Trim();
        Write($"{persona.PetName}: {user} ơi, {self} nghe.");
        Loaded += (_, _) => Place();
        Input.Focus();
    }

    public void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(12, (area.Width - Width) / 2);
        Top = area.Bottom - Height - 10;
    }

    public void Ask(string text)
    {
        Input.Text = text;
        _ = SendAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        if (_busy) return;
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        Input.Clear();
        var persona = _services.Settings.Personality;
        Write($"{persona.UserName}: {text}");
        _history.Add(new ChatMessage("user", text));
        _busy = true;
        try
        {
            var reply = await _services.CurrentProvider().CompleteAsync(_services.PromptBuilder.Build(persona), _history, CancellationToken.None);
            _history.Add(new ChatMessage("assistant", reply));
            Write($"{persona.PetName}: {reply}");
        }
        catch (Exception error)
        {
            Write($"{persona.PetName}: Chưa trả lời được. {error.Message}");
        }
        finally { _busy = false; }
    }

    private void Write(string line)
    {
        Log.Text = string.IsNullOrEmpty(Log.Text) ? line : Log.Text + "\n" + line;
        LogScroll.ScrollToEnd();
    }

    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
