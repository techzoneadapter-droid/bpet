using System.Text;
using System.Text.Json;
using BPet;

// Native messaging uses binary little-endian length-prefixed UTF-8 on stdio.
try
{
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    var header = new byte[4];
    input.ReadExactly(header);
    var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length is < 1 or > 1024 * 1024) return;
    var bytes = new byte[length];
    input.ReadExactly(bytes);
    using var json = JsonDocument.Parse(bytes);
    var root = json.RootElement;
    var reply = BrowserSessions.Locked(() =>
    {
        if (root.TryGetProperty("snapshot", out var snap))
            BrowserSessions.Save(snap.Deserialize<BrowserSnapshot>(BrowserSessions.Json)!);
        if (root.TryGetProperty("result", out var result))
            BrowserSessions.Complete(result.Deserialize<BrowserResult>(BrowserSessions.Json)!);
        var profile = root.GetProperty("profile").GetString() ?? "";
        if (!BrowserSessions.ValidProfile(profile)) throw new IOException("Invalid profile");
        return JsonSerializer.SerializeToUtf8Bytes(new { ok = true, job = BrowserSessions.Pending(profile) }, BrowserSessions.Json);
    });
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, reply.Length);
    output.Write(header); output.Write(reply); output.Flush();
}
catch (Exception error)
{
    // Never put diagnostic text on the native messaging stdout channel.
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
}
