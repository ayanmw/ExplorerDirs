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
        Console.WriteLine($"Native Shell tree icon smoke: PASS (normal={normal}, open={open})");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
