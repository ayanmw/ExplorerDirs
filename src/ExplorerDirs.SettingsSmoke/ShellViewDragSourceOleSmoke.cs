using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellViewDragSourceOleSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.NativeProbe", "ShellViewDragSource.cs"));
        var start = source.IndexOf("internal static int RunOleLoop(", StringComparison.Ordinal);
        var end = source.IndexOf("internal static void VerifyContract", start, StringComparison.Ordinal);
        var method = start >= 0 && end > start ? source[start..end] : string.Empty;

        Check(method.Contains("OleInitialize(", StringComparison.Ordinal),
            "the OLE drag loop initializes OLE at its own entry point");
        Check(method.Contains("finally", StringComparison.Ordinal) &&
              method.Contains("OleUninitialize()", StringComparison.Ordinal),
            "the OLE drag loop balances initialization after success or failure");
        Console.WriteLine("Shell view drag source OLE smoke: PASS (source contract; no drag loop or window created)");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell view drag source OLE smoke failed: " + behavior);
        Console.WriteLine("PASS " + behavior);
    }
}
