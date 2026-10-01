using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace BPet;

public sealed record SavedTab(string Url, string Title, bool Pinned, bool Active);
public sealed record SavedWindow(List<SavedTab> Tabs);
public sealed record BrowserSnapshot(string Profile, string Browser, DateTimeOffset At, List<SavedWindow> Windows);
public sealed record BrowserJob(string Id, string Profile, DateTimeOffset At, List<SavedWindow> Windows);
public sealed record BrowserResult(string Id, string Profile, DateTimeOffset At, string Message);

public static class BrowserSessions
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "browser-sessions");
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Local\BPet.BrowserSessions");
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(5000); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new IOException("Bộ nhớ trình duyệt đang bận. Hãy thử lại.");
            Directory.CreateDirectory(Folder);
            return action();
        }
        finally { if (owned) mutex.ReleaseMutex(); }
    }
    public static bool ValidProfile(string id) => Guid.TryParse(id, out _);
    public static bool SafeUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    public static List<BrowserSnapshot> Read() => ReadFile<List<BrowserSnapshot>>("snapshots.json") ?? new();
    public static T? ReadFile<T>(string name)
    {
        var path = Path.Combine(Folder, name);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default;
    }
    public static void Write<T>(string name, T value)
    {
        var path = Path.Combine(Folder, name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
    public static void Save(BrowserSnapshot snapshot)
    {
        if (!ValidProfile(snapshot.Profile) || snapshot.Browser is not ("chrome" or "edge" or "coccoc")) throw new IOException("Phiên trình duyệt không hợp lệ.");
        var windows = snapshot.Windows.Take(30).Select(w => new SavedWindow(w.Tabs.Where(t => SafeUrl(t.Url)).Take(150).Select(t => t with { Title = t.Title[..Math.Min(t.Title.Length, 200)] }).ToList())).Where(w => w.Tabs.Count > 0).ToList();
        if (windows.Count == 0) return; // Preserve the last useful session when the browser closes.
        var list = Read();
        var last = list.LastOrDefault(x => x.Profile == snapshot.Profile);
        var now = DateTimeOffset.Now;
        if (last is not null && now - last.At < TimeSpan.FromMinutes(5) && JsonSerializer.Serialize(last.Windows, Json) == JsonSerializer.Serialize(windows, Json)) return;
        list.Add(snapshot with { At = now, Windows = windows });
        list = list.Where(x => x.At >= now.AddDays(-30)).TakeLast(3000).ToList();
        Write("snapshots.json", list);
    }
    public static BrowserJob? Pending(string profile) => (ReadFile<List<BrowserJob>>("jobs.json") ?? new()).FirstOrDefault(j => j.Profile == profile && DateTimeOffset.Now - j.At < TimeSpan.FromMinutes(10));
    public static void Complete(BrowserResult result)
    {
        var jobs = ReadFile<List<BrowserJob>>("jobs.json") ?? new();
        if (!jobs.Any(j => j.Id == result.Id && j.Profile == result.Profile)) return;
        jobs.RemoveAll(j => j.Id == result.Id && j.Profile == result.Profile);
        Write("jobs.json", jobs);
        Write("result.json", result with { At = DateTimeOffset.Now });
    }
    public static int Queue(IEnumerable<BrowserSnapshot> snapshots)
    {
        var jobs = ReadFile<List<BrowserJob>>("jobs.json") ?? new();
        jobs.RemoveAll(j => DateTimeOffset.Now - j.At > TimeSpan.FromMinutes(10));
        int count = 0;
        foreach (var snapshot in snapshots)
        {
            if (jobs.Any(j => j.Profile == snapshot.Profile)) throw new IOException("Profile này đang chờ mở phiên trước. Đợi trình duyệt xử lý rồi thử lại.");
            jobs.Add(new(Guid.NewGuid().ToString("N"), snapshot.Profile, DateTimeOffset.Now, snapshot.Windows));
            count += snapshot.Windows.Sum(w => w.Tabs.Count);
        }
        Write("jobs.json", jobs);
        return count;
    }
}
