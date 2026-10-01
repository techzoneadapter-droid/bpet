using System.Windows;
using System.Windows.Media.Imaging;

namespace BPet;

/// <summary>Prebaked, aligned frames. No decoding or image morphing in the render loop.</summary>
public static class ChibiAnimation
{
    public const int FrameWidth = 224, FrameHeight = 288, FrameCount = 32;
    public static readonly string[] Names = { "idle", "walk", "climb", "crawl", "sleep", "play" };
    private static readonly Dictionary<string, BitmapSource[]> Cache = new();

    public static double Duration(string clip) => clip switch
    {
        "walk" => .88, "climb" => 1.25, "crawl" => 1.5,
        "sleep" => 3.6, "play" or "raised" => .8, _ => 4.8
    };

    public static int FrameIndex(string clip, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return 0;
        if (clip == "idle")
        {
            var blink = seconds % Duration(clip);
            return blink < 4.45 ? 0 : Math.Min(24, 16 + (int)((blink - 4.45) / .35 * 8));
        }
        return Math.Min(FrameCount - 1, (int)((seconds % Duration(clip)) / Duration(clip) * FrameCount));
    }

    public static BitmapSource[] Load(string clip)
    {
        if (Cache.TryGetValue(clip, out var frames)) return frames;
        if (!Names.Contains(clip)) throw new ArgumentException("Unknown chibi clip", nameof(clip));
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = new Uri($"pack://application:,,,/BPet;component/Assets/chibi/{clip}.png");
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.EndInit();
        sheet.Freeze();
        if (sheet.PixelWidth != FrameWidth * 8 || sheet.PixelHeight != FrameHeight * 4)
            throw new InvalidDataException($"Invalid chibi sheet: {clip}");
        frames = new BitmapSource[FrameCount];
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = new CroppedBitmap(sheet, new Int32Rect(i % 8 * FrameWidth, i / 8 * FrameHeight, FrameWidth, FrameHeight));
            frame.Freeze();
            frames[i] = frame;
        }
        Cache.Add(clip, frames);
        return frames;
    }
}
