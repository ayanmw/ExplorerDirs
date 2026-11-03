using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class SoftRefreshRoutingSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var pane = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var host = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));

        var preferenceRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.SoftRefreshSmoke.{Guid.NewGuid():N}");
        Directory.CreateDirectory(preferenceRoot);
        try
        {
            var preferencePath = Path.Combine(preferenceRoot, "SoftRefresh.ini");
            var missingPreference = new SoftRefreshStore(preferencePath);
            Check(missingPreference.Load(), "missing Soft-Refresh preference defaults on");
            missingPreference.Save(false);
            Check(!new SoftRefreshStore(preferencePath).Load(), "an explicitly disabled preference is preserved");
            missingPreference.Save(true);
            Check(new SoftRefreshStore(preferencePath).Load(), "Soft-Refresh persists through its portable profile INI");
        }
        finally { Directory.Delete(preferenceRoot, recursive: true); }

        Check(markup.Contains("x:Name=\"SoftRefreshMenu\"", StringComparison.Ordinal) &&
              markup.Contains("Header=\"自动软刷新（F5 完整刷新）\"", StringComparison.Ordinal) &&
              markup.Contains("ToolTip=\"自动更新只处理列表变化", StringComparison.Ordinal),
            "View menu names Soft-Refresh and explains re-enumeration behavior");
        Check(window.Contains("SoftRefresh.ini", StringComparison.Ordinal) &&
              window.Contains("pane.SetSoftRefresh(next)", StringComparison.Ordinal),
            "the global portable preference is applied to every existing pane");
        Check(pane.Contains("_softRefresh);", StringComparison.Ordinal) &&
              window.Contains("pane.SetSoftRefresh(next)", StringComparison.Ordinal) &&
              host.Contains("SetNoEnumRefresh(view, _softRefresh)", StringComparison.Ordinal) &&
              host.Contains("ShellFolderViewOptions.SetNoEnumRefresh(view, _softRefresh)", StringComparison.Ordinal),
            "existing and newly created Shell views receive the Soft-Refresh flag");

        var calls = new List<string>();
        int SetFlag(bool enabled) { calls.Add(enabled ? "soft-on" : "soft-off"); return 0; }
        int Refresh() { calls.Add("refresh"); return 0; }
        Check(ShellRefreshPolicy.Invoke(true, true, SetFlag, Refresh, () => true, () => true) == 0 &&
            calls.SequenceEqual(new[] { "soft-off", "refresh", "soft-on" }),
            "manual F5 bypasses soft enumeration suppression and restores the preference");
        calls.Clear();
        Check(ShellRefreshPolicy.Invoke(false, true, SetFlag, Refresh, () => true, () => true) == 0 &&
            calls.SequenceEqual(new[] { "refresh" }), "nonmanual refresh never disables the soft flag");
        calls.Clear();
        Check(ShellRefreshPolicy.Invoke(true, true, _ => unchecked((int)0x80004005), Refresh, () => true, () => true) < 0 &&
            calls.Count == 0, "failed full-refresh preparation does not pretend a complete refresh happened");
        calls.Clear();
        try
        {
            ShellRefreshPolicy.Invoke(true, true, SetFlag, () => throw new InvalidOperationException("fixture"), () => true, () => true);
            throw new Exception("Refresh failure was swallowed.");
        }
        catch (InvalidOperationException error) when (error.Message == "fixture") { }
        Check(calls.SequenceEqual(new[] { "soft-off", "soft-on" }), "a failing manual refresh restores automatic soft updates");
        calls.Clear();
        var current = true;
        Check(ShellRefreshPolicy.Invoke(true, true, enabled => { calls.Add("prepare"); current = false; return 0; },
            Refresh, () => current, () => true) < 0 && calls.SequenceEqual(new[] { "prepare" }),
            "navigation during COM preparation prevents refresh and restore on a retired view");
        calls.Clear();
        var preference = true;
        Check(ShellRefreshPolicy.Invoke(true, true, SetFlag, () => { preference = false; return Refresh(); },
            () => true, () => preference) == 0 && calls.SequenceEqual(new[] { "soft-off", "refresh", "soft-off" }),
            "a preference changed during manual refresh is not overwritten by stale soft-on");
        Check(host.Contains("automatic-soft-update; full-refresh=manual", StringComparison.Ordinal) &&
            host.Contains("if (needsDirectorySortUpdate && !_softRefresh)", StringComparison.Ordinal),
            "automatic monitoring and metadata changes avoid complete refresh under the soft preference");
        Console.WriteLine("Soft-refresh routing smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "MainWindow.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Soft-refresh routing smoke: " + message);
            Console.WriteLine("PASS " + message);
        }
    }
}
