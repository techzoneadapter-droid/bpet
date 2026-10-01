using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace BPet;

public sealed class DesktopAssistant
{
    private static CancellationTokenSource? _powerCountdown;
    private readonly AppServices _services;
    public DesktopAssistant(AppServices services) => _services = services;
    public static string Normalize(string text) => Regex.Replace(new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd'), @"\s+", " ").Trim();

    public async Task<string?> TryRunAsync(string text, Window owner, CancellationToken ct)
    {
        var command = Normalize(text);
        command = Regex.Replace(command, @"^(em oi|bpet oi|bpet|vo oi)[, !:]*", "");
        command = Regex.Replace(command, @"^(hay |em |giup anh |giup toi )+", "");
        if (command.StartsWith("huy tat may") || command.StartsWith("dung tat may") || command.StartsWith("huy khoi dong lai"))
        {
            if (_powerCountdown is not null) { _powerCountdown.Cancel(); return "Đã hủy lịch tắt/khởi động lại máy của BPet."; }
            return await PowerAsync("/a", "Đã hủy lịch tắt/khởi động lại của Windows.", ct);
        }
        if (Regex.IsMatch(command, @"^(tat may|tat may tinh|shutdown)(\b|$)"))
        {
            if (!Confirm(owner, "Tắt máy sau 60 giây? Hãy lưu công việc đang làm. Có thể nhắn ‘hủy tắt máy’ để dừng.")) return "Đã hủy yêu cầu tắt máy.";
            return SchedulePower("/s /t 0", "Máy sẽ tắt sau 60 giây. Nhắn ‘hủy tắt máy’ để dừng. Giữ BPet mở trong lúc đếm ngược.");
        }
        if (command.StartsWith("khoi dong lai may"))
        {
            if (!Confirm(owner, "Khởi động lại sau 60 giây? Hãy lưu công việc trước.")) return "Đã hủy yêu cầu.";
            return SchedulePower("/r /t 0", "Máy sẽ khởi động lại sau 60 giây. Nhắn ‘hủy khởi động lại’ để dừng. Giữ BPet mở trong lúc đếm ngược.");
        }
        if (command.StartsWith("khoa may") || command.StartsWith("khoa man hinh"))
            return LockWorkStation() ? "Đã khóa màn hình." : "Windows chưa khóa được màn hình.";
        if (command is "tro giup" or "lenh" || command.StartsWith("ban lam duoc gi")) return Help;
        if (command.StartsWith("ket noi trinh duyet") || command.StartsWith("cai tien ich trinh duyet")) { BrowserIntegration.ShowSetup(owner); return "Đã mở hướng dẫn kết nối trình duyệt. Chỉ cần cài tiện ích một lần cho mỗi profile muốn lưu."; }
        if (command.StartsWith("trang thai trinh duyet"))
            return BrowserSessions.Locked(() => BrowserSessions.ReadFile<BrowserResult>("result.json") is { } result ? $"{result.At.LocalDateTime:HH:mm dd/MM}: {result.Message}" : "Chưa có kết quả khôi phục. Nếu vừa yêu cầu, chờ tối đa khoảng 30 giây và mở đúng profile có tiện ích BPet.");
        if (command.StartsWith("xoa lich su trinh duyet bpet"))
        {
            if (!Confirm(owner, "Xóa các phiên do BPet lưu trên máy? Lịch sử của Chrome/Edge không bị xóa.")) return "Đã hủy.";
            BrowserSessions.Locked(() => { BrowserSessions.Write("snapshots.json", new List<BrowserSnapshot>()); BrowserSessions.Write("jobs.json", new List<BrowserJob>()); return true; });
            return "Đã xóa phiên đã lưu của BPet. Gỡ tiện ích nếu muốn ngừng lưu phiên mới.";
        }
        if (Regex.IsMatch(command, @"^(mo|khoi phuc|bat)\b") && Regex.IsMatch(command, @"\b(chrome|edge|coc coc|trinh duyet|tab)\b"))
            return OpenBrowser(command, owner);
        if (command.StartsWith("mo "))
        {
            if (Regex.IsMatch(command, @"^mo (may tinh|calculator)\b")) { Launch("calc.exe"); return "Đã gửi lệnh mở Máy tính."; }
            if (Regex.IsMatch(command, @"^mo (notepad|ghi chu)\b")) { Launch("notepad.exe"); return "Đã gửi lệnh mở Notepad."; }
            if (Regex.IsMatch(command, @"^mo (thu muc tai ve|downloads)\b")) { Launch("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); return "Đã mở thư mục Tải về."; }
            if (Regex.IsMatch(command, @"^mo (thu muc|explorer)\b")) { Launch("explorer.exe"); return "Đã gửi lệnh mở File Explorer."; }
            if (Regex.IsMatch(command, @"^mo (cai dat windows|settings)\b")) { Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }); return "Đã mở Cài đặt Windows."; }
            var url = Regex.Match(text, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase).Value.TrimEnd('.', ',');
            if (BrowserSessions.SafeUrl(url)) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return "Đã gửi trang web tới trình duyệt mặc định."; }
        }
        if (command.StartsWith("nhac "))
        {
            var delay = Regex.Match(command, @"\bsau (\d{1,4}) (phut|gio|giay)\b");
            if (delay.Success)
            {
                var n = int.Parse(delay.Groups[1].Value);
                var seconds = n * (delay.Groups[2].Value == "gio" ? 3600 : delay.Groups[2].Value == "phut" ? 60 : 1);
                if (seconds is < 1 or > 604800) return "Hãy đặt nhắc việc trong khoảng 1 giây đến 7 ngày.";
                _services.Reminders.Create(text, DateTime.Now.AddSeconds(seconds));
                return $"Đã đặt nhắc việc lúc {DateTime.Now.AddSeconds(seconds):HH:mm:ss dd/MM}. BPet phải đang chạy để nhắc.";
            }
        }
        // Do not let an AI reply pretend it executed an unsupported desktop command.
        if (Regex.IsMatch(command, @"^(mo|tat|xoa|cai|khoi phuc|khoa|nhac)\b"))
            return "Em chưa thực hiện được lệnh này. Nhắn ‘trợ giúp’ để xem các lệnh đã hỗ trợ; em không chạy lệnh hệ thống tùy ý từ câu trả lời AI.";
        return null;
    }

    private string OpenBrowser(string command, Window owner)
    {
        var browser = command.Contains("chrome") ? "chrome" : command.Contains("edge") ? "edge" : command.Contains("coc coc") ? "coccoc" : null;
        var dated = command.Contains("hom qua") || command.Contains("toi qua") || Regex.IsMatch(command, @"\d{4}-\d{2}-\d{2}");
        var restore = dated || command.Contains("cu") || command.Contains("khoi phuc") || command.Contains("gan nhat") || command.Contains("tab");
        if (!restore)
        {
            if (browser is null) browser = "chrome";
            BrowserIntegration.Open(browser, false);
            return $"Đã gửi lệnh mở {browser}.";
        }
        var list = BrowserSessions.Locked(BrowserSessions.Read);
        if (browser is not null) list = list.Where(s => s.Browser == browser).ToList();
        if (dated)
        {
            var date = DateTime.Today.AddDays(-1);
            var match = Regex.Match(command, @"\d{4}-\d{2}-\d{2}");
            if (match.Success && !DateTime.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return "Ngày chưa hợp lệ; dùng dạng 2026-10-01.";
            list = list.Where(s => s.At.LocalDateTime.Date == date.Date && (!command.Contains("toi qua") || s.At.LocalDateTime.Hour >= 18)).ToList();
        }
        var snapshots = list.GroupBy(s => s.Profile).Select(g => g.OrderBy(s => s.At).Last()).ToList();
        if (snapshots.Count == 0)
        {
            if (dated) return "Chưa có phiên được lưu cho ngày/khung giờ đó. BPet không thể biết tab hôm qua trước khi cài tiện ích. Bấm ‘Kết nối trình duyệt’ để bắt đầu lưu; hoặc nhắn ‘mở Chrome gần nhất’ để dùng phiên Chrome còn giữ.";
            if (browser is null) return "Chưa có phiên đã lưu. Hãy nói rõ ‘mở Chrome gần nhất’ hoặc ‘mở Edge gần nhất’, hoặc bấm ‘Kết nối trình duyệt’.";
            BrowserIntegration.Open(browser, true);
            return $"Đã yêu cầu {browser} khôi phục phiên gần nhất do trình duyệt giữ. Nếu trình duyệt đang mở, lệnh này không bảo đảm khôi phục tab đã đóng.";
        }
        var count = snapshots.Sum(s => s.Windows.Sum(w => w.Tabs.Count));
        var summary = string.Join("\n", snapshots.Select(s => $"{s.Browser} • {s.At.LocalDateTime:HH:mm dd/MM/yyyy} • {s.Windows.Sum(w => w.Tabs.Count)} tab"));
        if (!Confirm(owner, $"Mở lại {count} tab trong {snapshots.Count} profile?\n{summary}\n\nCác tab hiện tại vẫn giữ nguyên. Profile có tiện ích cần được mở để nhận yêu cầu.")) return "Đã hủy mở phiên.";
        BrowserSessions.Locked(() => BrowserSessions.Queue(snapshots));
        foreach (var kind in snapshots.Select(s => s.Browser).Distinct()) BrowserIntegration.Open(kind, true);
        return $"Đã xếp yêu cầu mở {count} tab đúng profile, giữ nhóm cửa sổ, thứ tự và tab ghim. Tiện ích nhận lệnh trong khoảng 30 giây; mở đúng profile nếu chưa chạy. Nhắn ‘trạng thái trình duyệt’ để xem kết quả. Không khôi phục nội dung biểu mẫu chưa lưu hoặc vị trí cuộn.";
    }
    private static bool Confirm(Window owner, string message) => System.Windows.MessageBox.Show(owner, message, "BPet · Xác nhận thao tác", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private static string SchedulePower(string arguments, string message)
    {
        _powerCountdown?.Cancel();
        var countdown = new CancellationTokenSource();
        _powerCountdown = countdown;
        _ = RunCountdown();
        return message;
        async Task RunCountdown()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), countdown.Token);
                // /t 0 without /f lets Windows protect unsaved work.
                var result = await PowerAsync(arguments, "Đã gửi yêu cầu tới Windows.", countdown.Token);
                if (result.StartsWith("Windows chưa")) System.Windows.MessageBox.Show(result, "BPet");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { System.Windows.MessageBox.Show("Chưa thực hiện được: " + error.Message, "BPet"); }
            finally { if (ReferenceEquals(_powerCountdown, countdown)) _powerCountdown = null; countdown.Dispose(); }
        }
    }
    private static async Task<string> PowerAsync(string arguments, string success, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true });
        if (process is null) return "Không chạy được lệnh Windows.";
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 ? success : $"Windows chưa thực hiện được lệnh (mã {process.ExitCode}). Có thể không có lịch tắt máy hoặc thiếu quyền.";
    }
    private static void Launch(string file, string? argument = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = true };
        if (argument is not null) start.ArgumentList.Add(argument);
        if (Process.Start(start) is null) throw new IOException("Chưa mở được ứng dụng.");
    }
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    public const string Help = "Bạn có thể nhắn:\n• Tắt máy cho anh / hủy tắt máy\n• Khởi động lại máy / khóa màn hình\n• Mở Chrome / Edge / Cốc Cốc\n• Mở Chrome hôm qua / trình duyệt tối qua\n• Mở trình duyệt ngày 2026-10-01\n• Trạng thái trình duyệt\n• Mở máy tính / Notepad / thư mục tải về\n• Mở https://example.com\n• Nhắc anh uống nước sau 20 phút\n\nPhiên theo ngày cần tiện ích BPet, lưu tối đa 30 ngày trên máy. Không lưu tab ẩn danh, mật khẩu hay nội dung trang. Các lệnh tiện ích chạy được cả khi API AI lỗi.";
}

public static class BrowserIntegration
{
    private const string ExtensionId = "pkedlajnecjglakmiagcefgobccobmgb";
    public static void Register()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "browser-host", "BPet.BrowserHost.exe");
        if (!File.Exists(host)) return;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet");
        Directory.CreateDirectory(folder);
        var manifest = Path.Combine(folder, "browser-host.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { name = "com.bpet.sessions", description = "BPet local browser sessions", path = host, type = "stdio", allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" } }));
        foreach (var vendor in new[] { @"Google\Chrome", @"Microsoft\Edge", @"CocCoc\Browser" })
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\" + vendor + @"\NativeMessagingHosts\com.bpet.sessions");
            key?.SetValue("", manifest);
        }
    }
    public static void ShowSetup(Window owner)
    {
        Register();
        var folder = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        System.Windows.MessageBox.Show(owner, "Kết nối một lần cho mỗi profile Chrome/Edge/Cốc Cốc:\n\n1. Mở trang quản lý tiện ích (chrome://extensions hoặc edge://extensions).\n2. Bật ‘Chế độ dành cho nhà phát triển’.\n3. Chọn ‘Tải tiện ích đã giải nén’ và chọn thư mục sắp mở.\n4. Bấm biểu tượng BPet để lưu phiên đầu tiên.\n\nTiện ích lưu URL, tiêu đề, tab ghim và nhóm cửa sổ trên máy trong 30 ngày, không gửi tới AI. Không đọc nội dung trang, mật khẩu hoặc tab ẩn danh. Gỡ tiện ích để ngừng lưu. Không thể khôi phục ngày chưa có dữ liệu.", "BPet · Kết nối trình duyệt");
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { folder } });
    }
    public static void Open(string browser, bool restore)
    {
        var relative = browser switch { "edge" => @"Microsoft\Edge\Application\msedge.exe", "coccoc" => @"CocCoc\Browser\Application\browser.exe", _ => @"Google\Chrome\Application\chrome.exe" };
        var candidates = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Select(p => Path.Combine(p, relative));
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null) throw new IOException($"Chưa tìm thấy {browser} ở thư mục cài chuẩn.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        if (restore) start.ArgumentList.Add("--restore-last-session");
        Process.Start(start);
    }
}
