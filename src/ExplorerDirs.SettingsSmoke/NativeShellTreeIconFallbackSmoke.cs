using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class NativeShellTreeIconFallbackSmoke
{
    internal static void Run()
    {
        var folder = Path.GetTempPath();
        Check(ShellTreeIconIndexResolver.TryResolve(folder, out var normal, out var open),
            "SHGetFileInfo resolves the production Shell tree icon indices for a real folder");
        Check(normal >= 0 && open >= 0,
            $"Shell tree image-list indices are nonnegative (normal={normal}, open={open})");
        var systemImageList = ShellTreeIconIndexResolver.GetSystemImageList();
        Check(systemImageList != 0, "the system image list used to resolve Shell indices is available");
        var imageCount = ImageList_GetImageCount(systemImageList);
        Check(normal < imageCount && open < imageCount,
            $"resolved folder icons exist in the system image list (normal={normal}, open={open}, count={imageCount})");
        Console.WriteLine($"Native Shell tree icon smoke: PASS (normal={normal}, open={open}, system-image-count={imageCount})");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern int ImageList_GetImageCount(nint imageList);
}
