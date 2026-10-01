using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BPet;

static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
var assembly = typeof(AppServices).Assembly;
var method = assembly.GetType("BPet.AiHttp")!.GetMethod("SendAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
async Task<HttpResponseMessage> Request(HttpClient client, CancellationToken ct = default) => await (Task<HttpResponseMessage>)method.Invoke(null, new object[] { client, (Func<HttpRequestMessage>)(() => new(HttpMethod.Get, "https://example.invalid")), ct })!;
Check(DesktopAssistant.LocalCommand("mở cho anh cái photoshop") == "mo photoshop", "polite Photoshop command normalized locally");
Check(DesktopAssistant.LocalCommand("em ơi mở Zalo cho anh") == "mo zalo", "polite app suffix removed");
Check(DesktopAssistant.IsLocalRequest("sleep máy giúp a"), "sleep routes locally without AI");
Check(DesktopAssistant.IsLocalRequest("bật ứng dụng Zalo cho anh"), "launch synonym routes locally");
Check(!DesktopAssistant.IsLocalRequest("Photoshop là gì?"), "question remains AI chat");
var fakeApps = new[] { new InstalledApp("Adobe Photoshop 2026", @"C:\test\Photoshop.lnk"), new InstalledApp("Uninstall Photoshop", @"C:\test\uninstall.lnk") };
Check(InstalledApps.Match(fakeApps, "photoshop").Single().Name == "Adobe Photoshop 2026", "finds installed Photoshop and excludes uninstaller");

Check(!DesktopAssistant.DirectAction("sleep may la gi?", @"sleep(?: may)?"), "sleep question never executes sleep");
Exception? uiError = null;
var uiThread = new Thread(() =>
{
    try
    {
        using var services = new AppServices();
        var window = new ChatDock(services);
        Check(window.Width == 420 && window.Height == 390, "compact chat default size");
        var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
        var input = (System.Windows.Controls.TextBox)window.FindName("Input");
        input.Text = "trợ giúp";
        var enter = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, System.Windows.Input.Key.Enter)
        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(enter);
        Check(enter.Handled && input.Text.Length == 0, "Enter preview sends a local command without newline or AI");
        window.Close();
    }
    catch (Exception error) { uiError = error; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start(); uiThread.Join();
if (uiError is not null) throw uiError;
var handler = new SequenceHandler(503, 200);
using (var client = new HttpClient(handler)) { using var response = await Request(client); Check(response.IsSuccessStatusCode && handler.Calls == 2, "503 retries then succeeds"); }
handler = new SequenceHandler(401);
using (var client = new HttpClient(handler)) { using var response = await Request(client); Check(response.StatusCode == HttpStatusCode.Unauthorized && handler.Calls == 1, "401 does not retry"); }
handler = new SequenceHandler(503, 503, 503);
using (var client = new HttpClient(handler)) { using var response = await Request(client); Check((int)response.StatusCode == 503 && handler.Calls == 3, "transient retries are bounded"); }
handler = new SequenceHandler(503);
using (var client = new HttpClient(handler))
using (var cancel = new CancellationTokenSource(50))
{
    try { using var response = await Request(client, cancel.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "retry delay respects cancellation"); }
}
var modelMethod = typeof(GeminiProvider).GetMethod("GeminiModelName", BindingFlags.Static | BindingFlags.NonPublic)!;
Check((string)modelMethod.Invoke(null, new object[] { "gemini-2.5-flash" })! == "gemini-2.5-flash", "keeps user-selected Gemini model");
Check(DesktopAssistant.Normalize("Mở Chrome tối qua cho anh") == "mo chrome toi qua cho anh", "Vietnamese command normalization");
Check(!BrowserSessions.SafeUrl("file:///C:/secret") && !BrowserSessions.SafeUrl("javascript:alert(1)") && BrowserSessions.SafeUrl("https://example.com"), "session restore accepts HTTP(S) only");

// Exercise the actual published bridge binary and binary protocol without opening a browser.
var profile = Guid.NewGuid().ToString();
var snapshot = new BrowserSnapshot(profile, "chrome", DateTimeOffset.Now, new() { new(new() { new("https://example.com", "Test", true, true), new("file:///C:/secret", "Excluded", false, false) }) });
var payload = JsonSerializer.SerializeToUtf8Bytes(new { profile, snapshot }, BrowserSessions.Json);
var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath("publish/browser-host/BPet.BrowserHost.exe")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
using var host = System.Diagnostics.Process.Start(start)!;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var prefix = BitConverter.GetBytes(payload.Length);
await host.StandardInput.BaseStream.WriteAsync(prefix, timeout.Token);
await host.StandardInput.BaseStream.WriteAsync(payload, timeout.Token);
await host.StandardInput.BaseStream.FlushAsync(timeout.Token);
host.StandardInput.Close();
await host.StandardOutput.BaseStream.ReadExactlyAsync(prefix, timeout.Token);
var result = new byte[BitConverter.ToInt32(prefix)];
await host.StandardOutput.BaseStream.ReadExactlyAsync(result, timeout.Token);
await host.WaitForExitAsync(timeout.Token);
using var resultJson = JsonDocument.Parse(result);
Check(resultJson.RootElement.GetProperty("ok").GetBoolean(), "native host binary handshake");
BrowserSessions.Locked(() =>
{
    var list = BrowserSessions.Read();
    var saved = list.Single(s => s.Profile == profile);
    Check(saved.Windows[0].Tabs.Count == 1 && saved.Windows[0].Tabs[0].Pinned, "bridge saves safe tabs and pin state");
    BrowserSessions.Queue(new[] { saved });
    var job = BrowserSessions.Pending(profile)!;
    Check(job.Windows[0].Tabs[0].Url == "https://example.com", "restore queue targets same profile");
    BrowserSessions.Complete(new(job.Id, profile, DateTimeOffset.Now, "test"));
    Check(BrowserSessions.Pending(profile) is null, "acknowledgement clears pending restore");
    list.RemoveAll(s => s.Profile == profile); BrowserSessions.Write("snapshots.json", list);
    return true;
});

sealed class SequenceHandler(params int[] codes) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var code = codes[Math.Min(Calls++, codes.Length - 1)];
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") });
    }
}
