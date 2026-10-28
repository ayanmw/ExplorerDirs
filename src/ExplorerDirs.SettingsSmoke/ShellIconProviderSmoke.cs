using ExplorerDirs.App;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellIconProviderSmoke
{
    internal static void Run()
    {
        var folder = ShellIconProvider.GetIcon(Environment.SystemDirectory, isDirectory: true);
        var thisPc = ShellIconProvider.GetIcon("shell:MyComputerFolder", isDirectory: true);
        var recycleBin = ShellIconProvider.GetIcon("shell:RecycleBinFolder", isDirectory: true);
        Check(folder is not null && thisPc is not null && recycleBin is not null,
            "file-system and virtual Shell folder icons are available");

        var folderPixels = GetPixels(folder!);
        var thisPcPixels = GetPixels(thisPc!);
        var recycleBinPixels = GetPixels(recycleBin!);
        Check(!folderPixels.SequenceEqual(thisPcPixels),
            "This PC uses its Shell icon rather than the generic folder icon");
        Check(!folderPixels.SequenceEqual(recycleBinPixels),
            "Recycle Bin uses its Shell icon rather than the generic folder icon");
        Check(!thisPcPixels.SequenceEqual(recycleBinPixels),
            "distinct virtual folders retain distinct Shell icons");
        var cache = typeof(ShellIconProvider).GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var item = cache.GetType().GetProperty("Item", members)!;
        for (var index = 0; index < 1024; index++)
            item.SetValue(cache, folder, [$"<pressure>{index}"]);
        var retained = (int)cache.GetType().GetProperty("Count", members)!.GetValue(cache)!;
        Check(retained <= 256, $"the production icon cache retains at most 256 entries after 1024 unique keys (actual={retained})");
        Console.WriteLine($"Production icon cache after 1024 unique keys: {retained} entries");
        var reloaded = ShellIconProvider.GetIcon("shell:MyComputerFolder", isDirectory: true);
        Check(reloaded is not null && GetPixels(reloaded).SequenceEqual(thisPcPixels),
            "an evicted Shell icon reloads with the same pixel content");
        CheckCachePolicy();
        Console.WriteLine("Shell icon provider smoke: PASS (folder, This PC and Recycle Bin icons are distinct)");
    }

    private static byte[] GetPixels(ImageSource source)
    {
        if (source is not BitmapSource bitmap) throw new InvalidOperationException("Shell icon is not a bitmap.");
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    private static void CheckCachePolicy()
    {
        var cache = new ShellIconCache();
        ImageSource Icon()
        {
            var icon = new DrawingImage();
            icon.Freeze();
            return icon;
        }
        var first = Icon();
        cache["icon-0"] = first;
        for (var index = 1; index < 256; index++) cache[$"icon-{index}"] = Icon();
        Check(cache.TryGetValue("ICON-0", out var hit) && ReferenceEquals(hit, first),
            "cache hits are case-insensitive and retain image identity");
        cache["icon-256"] = Icon();
        Check(cache.Count == 256 && cache.TryGetValue("icon-0", out _) && !cache.TryGetValue("icon-1", out _),
            "capacity pressure evicts the least recently used icon rather than a recent hit");
        var replacement = Icon();
        cache["ICON-0"] = replacement;
        Check(cache.Count == 256 && cache.TryGetValue("icon-0", out hit) && ReferenceEquals(hit, replacement),
            "replacing a case-equivalent key does not consume another cache slot");
        var consumer = new ImageBrush(replacement);
        consumer.Freeze();
        for (var index = 257; index < 4353; index++) cache[$"icon-{index}"] = Icon();
        Check(cache.Count == 256 && !cache.TryGetValue("icon-0", out _) &&
            ReferenceEquals(consumer.ImageSource, replacement) && replacement.IsFrozen,
            "4096 more keys remain bounded without clearing an existing image consumer's reference");
        Check(!cache.TryGetValue("not-present", out hit) && hit is null && cache.Count == 256,
            "a cache miss does not add an entry");
        Console.WriteLine("Shell icon cache: PASS (bounded references and LRU; no Shell file view or GC trimming)");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Shell icon provider: {message}");
    }
}
