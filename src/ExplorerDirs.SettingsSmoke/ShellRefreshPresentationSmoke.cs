using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellRefreshPresentationSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var host = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        var modeStart = host.IndexOf("public bool SetViewStyle(ViewStyle style)", StringComparison.Ordinal);
        var modeEnd = host.IndexOf("private void QueueViewStyleCapture", modeStart, StringComparison.Ordinal);
        var modePath = modeStart >= 0 && modeEnd > modeStart ? host[modeStart..modeEnd] : string.Empty;
        var refreshStart = host.IndexOf("private void Refresh(Action? afterRefresh, bool traceAsUserRefresh", StringComparison.Ordinal);
        var refreshEnd = host.IndexOf("private (bool SelectionRestored", refreshStart, StringComparison.Ordinal);
        var refreshPath = refreshStart >= 0 && refreshEnd > refreshStart ? host[refreshStart..refreshEnd] : string.Empty;

        Check(modePath.Contains("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) &&
            modePath.IndexOf("ShellViewRedrawLease.TrySuppress(view)", StringComparison.Ordinal) <
                modePath.IndexOf("ApplyMode(", StringComparison.Ordinal) &&
            modePath.Contains("ApplyMode(", StringComparison.Ordinal) &&
            !modePath.Contains("Refresh(", StringComparison.Ordinal) && !modePath.Contains("Navigate(", StringComparison.Ordinal),
            "view-style changes suppress native redraw only around the existing-view mode setter");
        Check(refreshPath.Contains("shellView.Refresh()", StringComparison.Ordinal) &&
            !refreshPath.Contains("SetRedraw", StringComparison.Ordinal) &&
            !refreshPath.Contains("TrySuppressCurrentViewRedraw", StringComparison.Ordinal),
            "F5 refreshes the existing Shell view while leaving native redraw enabled");
        Console.WriteLine("Shell refresh presentation smoke: PASS (source contract; no Shell view created)");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell refresh presentation smoke failed: " + behavior);
        Console.WriteLine("PASS " + behavior);
    }
}
