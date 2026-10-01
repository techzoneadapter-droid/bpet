using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BPet;

public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public SettingsStore Store { get; } = new();
    public CredentialVault Credentials { get; } = new();
    public PersonalityPromptBuilder PromptBuilder { get; } = new();
    public CharacterManager Characters { get; } = new();
    public ReminderService Reminders { get; }
    public NewsService News { get; }
    public TrayService Tray { get; }

    public AppServices()
    {
        Reminders = new ReminderService(this);
        News = new NewsService(this);
        Tray = new TrayService(this);
    }

    public void Load()
    {
        Settings = Store.Load();
        Characters.Reload();
        if (!Settings.General.PickedMy)
        {
            Settings.General.CharacterId = "bpet-my";
            Settings.General.PickedMy = true;
        }
        if (Characters.Find(Settings.General.CharacterId) is null && Characters.All.Count > 0)
            Settings.General.CharacterId = Characters.All[0].Id;
        if (Settings.Profiles.Count == 0)
            Settings.Profiles.Add(new PersonalityProfile { Name = "Mặc định", Personality = Clone(Settings.Personality), Provider = Settings.Ai.Provider });
        WindowsStartup.Apply(Settings.General.LaunchWithWindows);
        Reminders.Start();
        News.Start();
    }
    public void Save() => Store.Save(Settings);
    public void SavePersonalityProfile(string name)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0) throw new ArgumentException("Hãy đặt tên cho profile.");
        var profile = Settings.Profiles.FirstOrDefault(x => x.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        var snapshot = Clone(Settings.Personality);
        if (profile is null) Settings.Profiles.Add(new PersonalityProfile { Name = normalized, Personality = snapshot, Provider = Settings.Ai.Provider });
        else { profile.Personality = snapshot; profile.Provider = Settings.Ai.Provider; }
        Save();
    }
    public void ActivatePersonalityProfile(PersonalityProfile profile)
    {
        Settings.Personality = Clone(profile.Personality);
        if (profile.Provider is not null) Settings.Ai.Provider = profile.Provider.Value;
        Save();
    }
    private static PersonalitySettings Clone(PersonalitySettings source) => JsonSerializer.Deserialize<PersonalitySettings>(JsonSerializer.Serialize(source)) ?? new PersonalitySettings();
    public IAIProvider CurrentProvider() => Settings.Ai.Provider switch
    {
        AiProviderKind.OpenAI => new OpenAiProvider(Credentials, Settings.Ai),
        AiProviderKind.Gemini => new GeminiProvider(Credentials, Settings.Ai),
        AiProviderKind.OpenAiCompatible => new OpenAiProvider(Credentials, Settings.Ai, true),
        _ => new OfflineProvider()
    };
    public void Dispose() { Reminders.Dispose(); News.Dispose(); Tray.Dispose(); }
}

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    public AppSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save(AppSettings settings)
    {
        if (!double.IsFinite(settings.General.Left)) settings.General.Left = 48;
        if (!double.IsFinite(settings.General.Top)) settings.General.Top = 48;
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
    public string Build(PersonalitySettings p)
    {
        var self = string.IsNullOrWhiteSpace(p.PetPronoun) ? "mình" : p.PetPronoun.Trim();
        var user = string.IsNullOrWhiteSpace(p.UserPronoun) ? "bạn" : p.UserPronoun.Trim();
        var tone = p.Attitude switch
        {
            "Cute" => "dễ thương, nhẹ nhàng",
            "Gentle" => "dịu dàng",
            "Funny" => "hài hước vừa phải",
            "Energetic" => "nhanh, có sức sống",
            "Calm" => "điềm tĩnh",
            "Professional" or "Serious" => "gọn và chỉn chu",
            "Tsundere" => "hơi bướng nhưng vẫn quan tâm",
            "Romantic" => "ấm và gần",
            "Caring" => "quan tâm, không sến",
            _ => "tự nhiên"
        };
        return $"""
            Bạn là {p.PetName}, bạn đồng hành trên màn hình. Trả lời bằng tiếng Việt.
            XƯNG HÔ BẮT BUỘC, không được đổi:
            - Tự xưng đúng từ "{self}". Cấm đổi sang từ khác.
            - Gọi người đối diện đúng từ "{user}". Tên của họ là {p.UserName}, chỉ dùng tên khi thật sự cần.
            - Câu mẫu đúng: "{user} ơi, {self} ở đây."
            Giọng: {tone}. Quan hệ: {p.RelationshipPreset}. {p.RelationshipDescription}.
            Thân mật {p.Affection}/100, hài {p.Humor}/100, trang trọng {p.Formality}/100, độ dài câu theo mức nói nhiều {p.Talkativeness}/100, emoji {p.EmojiUsage}/100.
            Nói như người thật, ngắn, không mở đầu bằng "chắc chắn rồi" hay "với tư cách là AI".
            {p.CustomInstructions}
            """;
    }
}

public sealed class CharacterManager
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "Characters");
    private readonly List<CharacterInfo> _characters = new();
    public IReadOnlyList<CharacterInfo> All => _characters;

    public void Reload()
    {
        _characters.Clear();
        foreach (var item in BuiltIns()) _characters.Add(item);
        Directory.CreateDirectory(_root);
        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            try
            {
                var manifestPath = Path.Combine(folder, "manifest.json");
                var manifest = JsonSerializer.Deserialize<CharacterManifest>(File.ReadAllText(manifestPath));
                if (IsValid(manifest, folder, out _)) _characters.Add(new CharacterInfo(manifest!.Id, manifest.Name, manifest.RecommendedAttitude, false, folder, manifest));
            }
            catch { /* A broken custom character is ignored and built-ins remain available. */ }
        }
    }

    public CharacterInfo? Find(string id) => _characters.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public CharacterInfo ImportFolder(string sourceFolder)
    {
        var manifestPath = Path.Combine(sourceFolder, "manifest.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Character pack thiếu manifest.json.");
        var manifest = JsonSerializer.Deserialize<CharacterManifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("manifest.json không hợp lệ.");
        if (!IsValid(manifest, sourceFolder, out var error)) throw new InvalidDataException(error);
        var safeId = string.Concat(manifest.Id.Where(char.IsLetterOrDigit).Append('-')).TrimEnd('-');
        if (safeId.Length == 0) throw new InvalidDataException("Character ID không hợp lệ.");
        var destination = Path.Combine(_root, safeId);
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        CopyDirectory(sourceFolder, destination);
        Reload();
        return Find(manifest.Id) ?? throw new InvalidDataException("Không thể cài character pack.");
    }

    public CharacterInfo ImportZip(string zipPath)
    {
        var staging = Path.Combine(Path.GetTempPath(), "BPet", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging);
            var packRoot = File.Exists(Path.Combine(staging, "manifest.json")) ? staging : Directory.EnumerateDirectories(staging).FirstOrDefault(x => File.Exists(Path.Combine(x, "manifest.json")));
            if (packRoot is null) throw new InvalidDataException("ZIP không có manifest.json ở thư mục gốc.");
            return ImportFolder(packRoot);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static bool IsValid(CharacterManifest? manifest, string folder, out string error)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name)) { error = "manifest phải có id và name."; return false; }
        if (manifest.Animations.Count > 0 && !manifest.Animations.Values.All(x => File.Exists(Path.Combine(folder, x)))) { error = "Một hoặc nhiều tệp animation không tồn tại."; return false; }
        error = ""; return true;
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var child in Directory.EnumerateDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }
    private static IEnumerable<CharacterInfo> BuiltIns()
    {
        foreach (var (id, name, attitude) in new[] { ("bpet-my", "Trà My", "Cute"), ("bpet-cat", "BPet", "Cute") })
        {
            var manifest = new CharacterManifest { Id = id, Name = name, RecommendedAttitude = attitude, Animations = Enum.GetNames<PetState>().ToDictionary(x => x, _ => "builtin", StringComparer.OrdinalIgnoreCase) };
            yield return new CharacterInfo(id, name, attitude, true, null, manifest);
        }
    }
}

public sealed class PetStateMachine
{
    private readonly AppServices _services;
    private readonly Random _random = new();
    private DateTime _nextDecision = DateTime.MinValue;
    public PetState Current { get; private set; } = PetState.Idle;
    public PetStateMachine(AppServices services) => _services = services;
    public PetState Decide()
    {
        var behavior = _services.Settings.PetBehavior;
        if (!behavior.AutoMovement) return Set(PetState.Idle);
        if (behavior.SimulationEnabled && behavior.Energy < 20) return Set(PetState.Sleep);
        if (DateTime.Now < _nextDecision) return Current;
        _nextDecision = DateTime.Now.AddSeconds(_random.Next(6, 14));
        return Set(_random.Next(100) < behavior.MovementFrequency ? PetState.Walk : PetState.Idle);
    }
    public PetState Set(PetState desired)
    {
        var character = _services.Characters.Find(_services.Settings.General.CharacterId);
        Current = character is not null && character.Manifest.Animations.ContainsKey(desired.ToString()) ? desired : PetState.Idle;
        return Current;
    }
}

public sealed record UpdateInfo(Version Version, string InstallerUrl, string Notes);

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/techzoneadapter-droid/bpet/releases/latest";
    public Version CurrentVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BPet-Updater/1.0");
        using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (!Version.TryParse(tag, out var latest) || latest <= CurrentVersion) return null;
        var asset = json.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(x => x.GetProperty("name").GetString()?.Equals("BPet-Setup.exe", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) return null;
        return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, json.RootElement.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    public async Task DownloadAndLaunchAsync(UpdateInfo update, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(Path.GetTempPath(), $"BPet-Setup-{update.Version}.exe");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BPet-Updater/1.0");
        using var response = await client.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (length is > 0) progress?.Report((int)(total * 100 / length.Value));
            }
            await output.FlushAsync(cancellationToken);
        }

        var script = Path.Combine(Path.GetTempPath(), "bpet-update.cmd");
        var pid = Environment.ProcessId;
        File.WriteAllText(script, "@echo off\r\n:wait\r\ntasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\nif %errorlevel%==0 (\r\n  ping 127.0.0.1 -n 2 >nul\r\n  goto wait\r\n)\r\nstart \"\" \"" + destination + "\"\r\n");
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + script + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        });
    }
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
        await ApiErrors.EnsureSuccess(response, ct);
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
        var model = GeminiModelName(_settings.GeminiModel);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        var contents = messages.Select(x => new { role = x.Role == "assistant" ? "model" : "user", parts = new[] { new { text = x.Content } } });
        var body = new { system_instruction = new { parts = new[] { new { text = system } } }, contents };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, ct);
        await ApiErrors.EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini không trả về nội dung. Hãy kiểm tra API key và model.");
        var texts = new List<string>();
        foreach (var part in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
            if (part.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } value) texts.Add(value);
        }
        if (texts.Count == 0) throw new InvalidOperationException("Gemini trả về rỗng. Thử model gemini-3.8-flash.");
        return string.Join("", texts);
    }

    internal static string GeminiModelName(string? model)
    {
        var value = model?.Trim() ?? "";
        if (value.Length == 0 || !value.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)) return "gemini-3.8-flash";
        return value is "gemini-2.5-flash" or "gemini-2.5-flash-lite" or "gemini-2.0-flash" or "gemini-1.5-flash" or "gemini-1.5-pro" ? "gemini-3.8-flash" : value;
    }
}

internal static class ApiErrors
{
    public static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = body;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var nested)) message = nested.GetString() ?? body;
                else if (error.ValueKind == JsonValueKind.String) message = error.GetString() ?? body;
            }
        }
        catch { /* The body was not JSON. */ }
        if (message.Length > 240) message = message[..240];
        throw new InvalidOperationException($"{(int)response.StatusCode}: {message}");
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
        try
        {
            var now = DateTime.Now;
            var due = _services.Settings.Reminders.Where(x => !x.Delivered && x.DueAt <= now).ToList();
            var jobs = _services.Settings.DailyTasks.Where(task => task.Enabled && task.LastRun.Date != now.Date && now.Hour == task.Hour && now.Minute == task.Minute).ToList();
            if (due.Count == 0 && jobs.Count == 0) return;
            foreach (var reminder in due) reminder.Delivered = true;
            foreach (var task in jobs) task.LastRun = now;
            _services.Save();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is not MainWindow pet) return;
                if (due.Count > 0)
                    pet.ShowSpeech($"{_services.Settings.Personality.UserPronoun} ơi, tới giờ {reminderMessage(due)} rồi nè.", PetState.Happy);
                foreach (var task in jobs) pet.RunDailyTask(task);
            });
        }
        catch { /* A reminder must never take the pet down. */ }
    }
    private static string reminderMessage(List<Reminder> due) => due.Count == 1 ? due[0].Message : string.Join(", ", due.Select(x => x.Message));
    public void Dispose() => _timer.Dispose();
}

public sealed class NewsService : IDisposable
{
    private readonly AppServices _services;
    private readonly System.Timers.Timer _timer = new(15000);
    private DateTime _next = DateTime.Now.AddSeconds(25);
    private int _cursor;
    private int _busy;

    public NewsService(AppServices services)
    {
        _services = services;
        _timer.Elapsed += (_, _) => Tick();
    }

    public void Start() => _timer.Start();
    public void ReportSoon() => _next = DateTime.Now;

    private async void Tick()
    {
        if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (DateTime.Now < _next) return;
            var news = _services.Settings.News;
            if (!news.Enabled) { _next = DateTime.Now.AddMinutes(1); return; }
            var kinds = new List<string>();
            if (news.Gold) kinds.Add("gold");
            if (news.Ai) kinds.Add("ai");
            if (news.Marketing) kinds.Add("mkt");
            if (kinds.Count == 0) { _next = DateTime.Now.AddMinutes(5); return; }
            var kind = kinds[_cursor % kinds.Count];
            _cursor++;
            var line = await Fetch(kind);
            var minutes = Math.Clamp(news.IntervalMinutes, 10, 180);
            _next = DateTime.Now.AddMinutes(string.IsNullOrWhiteSpace(line) ? 2 : minutes);
            if (string.IsNullOrWhiteSpace(line)) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow pet) pet.ShowSpeech(line, PetState.Talking, 14);
            });
        }
        catch { _next = DateTime.Now.AddMinutes(2); }
        finally { _busy = 0; }
    }

    private static async Task<string?> Fetch(string kind)
    {
        var (prefix, query) = kind switch
        {
            "gold" => ("Vàng", "giá vàng SJC hôm nay"),
            "ai" => ("AI", "tin tức trí tuệ nhân tạo"),
            _ => ("MKT", "tin tức marketing quảng cáo")
        };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BPet-News/1.0");
        var xml = await client.GetStringAsync("https://news.google.com/rss/search?q=" + Uri.EscapeDataString(query) + "&hl=vi&gl=VN&ceid=VN:vi");
        var match = Regex.Match(xml, @"<item>\s*<title>([^<]+)</title>");
        if (!match.Success) return null;
        var title = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        if (title.Length > 120) title = title[..117] + "…";
        return prefix + " · " + title;
    }

    public void Dispose() => _timer.Dispose();
}

public sealed class TrayService : IDisposable
{
    private readonly AppServices _services;
    private readonly Forms.NotifyIcon? _icon;
    private readonly Forms.ToolStripMenuItem? _startupItem;

    public TrayService(AppServices services)
    {
        _services = services;
        Forms.NotifyIcon? icon = null;
        Forms.ToolStripMenuItem? startupItem = null;
        try
        {
            icon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "BPet — AI Desktop Companion", Visible = false };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Hiện BPet", null, (_, _) => ShowPet());
            menu.Items.Add("Chat", null, (_, _) => Ui(() => { if (Application.Current?.MainWindow is MainWindow pet) pet.OpenChat(); }));
            menu.Items.Add("Cài đặt", null, (_, _) => Ui(() => new SettingsWindow(_services).Show()));
            menu.Items.Add("Luôn trên cùng", null, (_, _) => { _services.Settings.General.AlwaysOnTop = !_services.Settings.General.AlwaysOnTop; ApplyPetOptions(); });
            menu.Items.Add("Click through", null, (_, _) => { _services.Settings.General.ClickThrough = !_services.Settings.General.ClickThrough; ApplyPetOptions(); });
            startupItem = new Forms.ToolStripMenuItem("Khởi động cùng Windows");
            startupItem.Click += (_, _) => Ui(ToggleStartup);
            menu.Items.Add(startupItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Thoát", null, (_, _) => Ui(() =>
            {
                if (Application.Current.MainWindow is MainWindow pet) pet.RequestExit();
                Application.Current.Shutdown();
            }));
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (_, _) => ShowPet();
        }
        catch { icon?.Dispose(); icon = null; startupItem = null; }
        _icon = icon;
        _startupItem = startupItem;
        SyncStartupItem();
    }

    public void Show() { if (_icon is not null) _icon.Visible = true; SyncStartupItem(); }
    public void SyncStartupItem() { if (_startupItem is not null) _startupItem.Checked = _services.Settings.General.LaunchWithWindows; }
    private void ToggleStartup()
    {
        _services.Settings.General.LaunchWithWindows = !_services.Settings.General.LaunchWithWindows;
        WindowsStartup.Apply(_services.Settings.General.LaunchWithWindows);
        _services.Save();
        SyncStartupItem();
    }
    private void ShowPet() => Ui(() =>
    {
        if (Application.Current.MainWindow is not MainWindow pet) return;
        pet.Show();
        pet.Activate();
    });
    private void ApplyPetOptions() => Ui(() => { if (Application.Current.MainWindow is MainWindow pet) pet.ApplyOptions(); });
    private static void Ui(Action action)
    {
        if (Application.Current is null) return;
        if (Application.Current.Dispatcher.CheckAccess()) action();
        else Application.Current.Dispatcher.Invoke(action);
    }
    public void Dispose() => _icon?.Dispose();
}

public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BPet";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;
            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return;
            }
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            key.SetValue(ValueName, "\"" + exe + "\"");
        }
        catch { /* HKCU Run is normally writable. A failure here must not block the pet. */ }
    }
}
