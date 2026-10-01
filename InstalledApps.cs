using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BPet;

public sealed record InstalledApp(string Name, string Path);
public static class InstalledApps
{
    private static List<InstalledApp>? _cache;
    private static DateTime _cachedAt;
    public static string Open(string query)
    {
        query = DesktopAssistant.Normalize(query);
        if (string.IsNullOrWhiteSpace(query)) return "Anh muốn mở ứng dụng nào? Ví dụ: mở Photoshop hoặc mở Zalo.";
        var apps = Find();
        var matches = Match(apps, query);
        if (matches.Count == 0) return $"Chưa tìm thấy ứng dụng “{query}” trong Start Menu hoặc danh sách ứng dụng của Windows. Lệnh này được xử lý trên máy, không gửi tới AI.";
        if (matches.Count > 1)
            return "Có nhiều ứng dụng khớp tên. Anh nhắn mở kèm đúng tên:\n" + string.Join("\n", matches.Take(6).Select(a => "• " + a.Name));
        var app = matches[0];
        Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
        return $"Đã gửi lệnh mở {app.Name}.";
    }
    public static List<InstalledApp> Match(IEnumerable<InstalledApp> apps, string query)
    {
        var all = apps.Where(a => !Regex.IsMatch(DesktopAssistant.Normalize(a.Name), @"\b(uninstall|uninstaller|go cai dat|remove|setup|installer)\b")).ToList();
        var exact = all.Where(a => DesktopAssistant.Normalize(a.Name) == query).ToList();
        var matches = exact.Count > 0 ? exact : all.Where(a => Regex.IsMatch(DesktopAssistant.Normalize(a.Name), @"\b" + Regex.Escape(query) + @"\b")).ToList();
        return matches.GroupBy(a => DesktopAssistant.Normalize(a.Name)).Select(g => g.First()).ToList();
    }
    private static List<InstalledApp> Find()
    {
        if (_cache is not null && DateTime.Now - _cachedAt < TimeSpan.FromMinutes(2)) return _cache;
        var list = new List<InstalledApp>();
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }.Distinct())
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(folder, "*.lnk", options).Take(1500))
                    list.Add(new(Path.GetFileNameWithoutExtension(file), file));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var paths = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (paths is null) continue;
                foreach (var name in paths.GetSubKeyNames())
                {
                    using var entry = paths.OpenSubKey(name);
                    var file = Environment.ExpandEnvironmentVariables((entry?.GetValue("") as string ?? "").Trim('"'));
                    if (Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                        list.Add(new(Path.GetFileNameWithoutExtension(name), file));
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        _cache = list;
        _cachedAt = DateTime.Now;
        return list;
    }
}
