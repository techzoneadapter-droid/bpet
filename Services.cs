using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BPet;

public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public SettingsStore Store { get; } = new();
    public CredentialVault Credentials { get; } = new();
    public PersonalityPromptBuilder PromptBuilder { get; } = new();
    public ReminderService Reminders { get; }
    public TrayService Tray { get; }

    public AppServices()
    {
        Reminders = new ReminderService(this);
        Tray = new TrayService(this);
    }

    public void Load() { Settings = Store.Load(); Reminders.Start(); }
    public void Save() => Store.Save(Settings);
    public IAIProvider CurrentProvider() => Settings.Ai.Provider switch
    {
        AiProviderKind.OpenAI => new OpenAiProvider(Credentials, Settings.Ai),
        AiProviderKind.Gemini => new GeminiProvider(Credentials, Settings.Ai),
        AiProviderKind.OpenAiCompatible => new OpenAiProvider(Credentials, Settings.Ai, true),
        _ => new OfflineProvider()
    };
    public void Dispose() { Reminders.Dispose(); Tray.Dispose(); }
}

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public AppSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

// API keys are stored with Windows DPAPI. They are never placed in settings.json or logs.
public sealed class CredentialVault
{
    private readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "secrets");
    public void Save(string provider, string secret)
    {
        Directory.CreateDirectory(_dir);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_dir, provider + ".bin"), protectedBytes);
    }
    public string? Read(string provider)
    {
        try
        {
            var path = Path.Combine(_dir, provider + ".bin");
            return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : null;
        }
        catch { return null; }
    }
}

public sealed class PersonalityPromptBuilder
{
    public string Build(PersonalitySettings p) => $"""
        You are {p.PetName}, an AI desktop companion. Speak naturally in Vietnamese unless the user asks otherwise.
        The user's name is {p.UserName}. You call yourself '{p.PetPronoun}' and call the user '{p.UserPronoun}'.
        Relationship style: {p.RelationshipDescription}. Attitude preset: {p.Attitude}.
        Affection {p.Affection}/100, humor {p.Humor}/100, formality {p.Formality}/100, talkativeness {p.Talkativeness}/100, proactiveness {p.Proactiveness}/100, emoji usage {p.EmojiUsage}/100.
        Be helpful, warm, concise when appropriate, and never sound like a generic chatbot. Do not repeat names unnecessarily.
        Relationship style changes only language and tone, never safety rules, access permissions, or system privileges.
        {p.CustomInstructions}
        """;
}

public interface IAIProvider
{
    string DisplayName { get; }
    Task<string> CompleteAsync(string systemPrompt, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
    Task<bool> TestAsync(CancellationToken cancellationToken);
}

public sealed class OfflineProvider : IAIProvider
{
    public string DisplayName => "AI chưa thiết lập";
    public Task<bool> TestAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<string> CompleteAsync(string systemPrompt, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => Task.FromResult("Mình vẫn ở đây nè. Bạn vào Cài đặt → AI Provider để kết nối OpenAI hoặc Gemini nhé.");
}

public sealed class OpenAiProvider : IAIProvider
{
    private readonly CredentialVault _vault; private readonly AiSettings _settings; private readonly bool _custom;
    public OpenAiProvider(CredentialVault vault, AiSettings settings, bool custom = false) => (_vault, _settings, _custom) = (vault, settings, custom);
    public string DisplayName => _custom ? "OpenAI-compatible" : "OpenAI";
    public async Task<bool> TestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_vault.Read(_custom ? "custom" : "openai"))) return false;
        try { _ = await CompleteAsync("Reply with OK.", new[] { new ChatMessage("user", "ping") }, ct); return true; } catch { return false; }
    }
    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var key = _vault.Read(_custom ? "custom" : "openai") ?? throw new InvalidOperationException("Chưa có API key.");
        var baseUrl = _custom && !string.IsNullOrWhiteSpace(_settings.CustomBaseUrl) ? _settings.CustomBaseUrl.TrimEnd('/') : "https://api.openai.com";
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var model = string.IsNullOrWhiteSpace(_settings.CustomModelId) ? _settings.OpenAiModel : _settings.CustomModelId;
        var body = new { model, messages = new[] { new { role = "system", content = system } }.Concat(messages.Select(x => new { role = x.Role, content = x.Content })).ToArray() };
        using var response = await client.PostAsync("/v1/chat/completions", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

public sealed class GeminiProvider : IAIProvider
{
    private readonly CredentialVault _vault; private readonly AiSettings _settings;
    public GeminiProvider(CredentialVault vault, AiSettings settings) => (_vault, _settings) = (vault, settings);
    public string DisplayName => "Google Gemini";
    public async Task<bool> TestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_vault.Read("gemini"))) return false;
        try { _ = await CompleteAsync("Reply with OK.", new[] { new ChatMessage("user", "ping") }, ct); return true; } catch { return false; }
    }
    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var key = _vault.Read("gemini") ?? throw new InvalidOperationException("Chưa có Gemini API key.");
        var model = string.IsNullOrWhiteSpace(_settings.CustomModelId) ? _settings.GeminiModel : _settings.CustomModelId;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var contents = messages.Select(x => new { role = x.Role == "assistant" ? "model" : "user", parts = new[] { new { text = x.Content } } });
        var body = new { system_instruction = new { parts = new[] { new { text = system } } }, contents };
        using var response = await client.PostAsync($"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
    }
}

public sealed class ReminderService : IDisposable
{
    private readonly AppServices _services; private readonly System.Timers.Timer _timer = new(15000);
    public ReminderService(AppServices services) { _services = services; _timer.Elapsed += (_, _) => Check(); }
    public void Start() => _timer.Start();
    public void Create(string message, DateTime dueAt) { _services.Settings.Reminders.Add(new Reminder { Message = message, DueAt = dueAt }); _services.Save(); }
    private void Check()
    {
        var due = _services.Settings.Reminders.Where(x => !x.Delivered && x.DueAt <= DateTime.Now).ToList();
        foreach (var reminder in due)
        {
            reminder.Delivered = true; _services.Save();
            Application.Current.Dispatcher.Invoke(() => ((MainWindow)Application.Current.MainWindow).ShowSpeech($"{_services.Settings.Personality.UserPronoun} ơi, tới giờ {reminder.Message} rồi nè.", PetState.Happy));
        }
    }
    public void Dispose() => _timer.Dispose();
}

public sealed class TrayService : IDisposable
{
    private readonly AppServices _services; private readonly Forms.NotifyIcon _icon;
    public TrayService(AppServices services)
    {
        _services = services;
        _icon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "BPet — AI Desktop Companion", Visible = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Hiện BPet", null, (_, _) => ShowPet());
        menu.Items.Add("Chat", null, (_, _) => Application.Current.Dispatcher.Invoke(() => new ChatWindow(_services).Show()));
        menu.Items.Add("Cài đặt", null, (_, _) => Application.Current.Dispatcher.Invoke(() => new SettingsWindow(_services).Show()));
        menu.Items.Add("Luôn trên cùng", null, (_, _) => { _services.Settings.General.AlwaysOnTop = !_services.Settings.General.AlwaysOnTop; ApplyPetOptions(); });
        menu.Items.Add("Click through", null, (_, _) => { _services.Settings.General.ClickThrough = !_services.Settings.General.ClickThrough; ApplyPetOptions(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => Application.Current.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).RequestExit(); Application.Current.Shutdown(); }));
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowPet();
    }
    public void Show() => _icon.Visible = true;
    private void ShowPet() => Application.Current.Dispatcher.Invoke(() => { var pet = (MainWindow)Application.Current.MainWindow; pet.Show(); pet.Activate(); });
    private void ApplyPetOptions() => Application.Current.Dispatcher.Invoke(() => ((MainWindow)Application.Current.MainWindow).ApplyOptions());
    public void Dispose() => _icon.Dispose();
}
