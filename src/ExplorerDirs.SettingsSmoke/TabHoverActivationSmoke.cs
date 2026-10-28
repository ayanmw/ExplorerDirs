using ExplorerDirs.App;
using System.IO;

internal static class TabHoverActivationSmoke
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "tab-hover", "TabHoverActivation.ini");
        var store = new TabHoverActivationPreference(path);
        Require(store.Load() == TabHoverActivationPreference.DefaultDelayMilliseconds,
            "missing preference defaults to a one-second hover delay");
        Require(TabHoverActivationPreference.AllowedDelayMilliseconds.SequenceEqual([0, 200, 500, 1000, 1500, 2000, 3000]),
            "preference exposes disabled and Q-Dir delay choices");

        store.Save(500);
        Require(store.Load() == 500, "hover delay round-trips through its portable INI");
        Throws<ArgumentOutOfRangeException>(() => store.Save(750), "unsupported delay is rejected");

        var futurePath = Path.Combine(Path.GetDirectoryName(path)!, "future.ini");
        const string future = "[TabHoverActivation]\nFormatVersion=2\nDelayMilliseconds=500\n";
        File.WriteAllText(futurePath, future);
        var futureStore = new TabHoverActivationPreference(futurePath);
        Require(futureStore.Load() == TabHoverActivationPreference.DefaultDelayMilliseconds,
            "future preference falls back to the safe default");
        Throws<IOException>(() => futureStore.Save(200), "future preference is protected from writes");
        Require(File.ReadAllText(futurePath) == future, "future preference remains byte-for-byte unchanged");

        var conflictPath = Path.Combine(Path.GetDirectoryName(path)!, "conflict.ini");
        var firstWriter = new TabHoverActivationPreference(conflictPath);
        firstWriter.Load();
        firstWriter.Save(500);
        var staleWriter = new TabHoverActivationPreference(conflictPath);
        staleWriter.Load();
        File.WriteAllText(conflictPath, "external edit");
        Throws<IOException>(() => staleWriter.Save(200), "external preference edits are not overwritten");
        Require(File.ReadAllText(conflictPath) == "external edit", "external preference contents remain unchanged");

        var invalidPath = Path.Combine(Path.GetDirectoryName(path)!, "invalid.ini");
        File.WriteAllText(invalidPath, "[TabHoverActivation]\nFormatVersion=1\nDelayMilliseconds=750\n");
        var invalidStore = new TabHoverActivationPreference(invalidPath);
        Require(invalidStore.Load() == TabHoverActivationPreference.DefaultDelayMilliseconds &&
                invalidStore.LoadWarning is not null,
            "unsupported stored delays are backed up and fall back to the safe default");

        var first = new object();
        var second = new object();
        var gate = new TabHoverActivationGate();
        Require(!gate.Begin(first, isOwnTabDrag: true, hasShellLocationData: true, delayMilliseconds: 1000) &&
                !gate.Begin(first, isOwnTabDrag: false, hasShellLocationData: false, delayMilliseconds: 1000) &&
                !gate.Begin(first, isOwnTabDrag: false, hasShellLocationData: true, delayMilliseconds: 0),
            "own-tab, non-Shell and disabled drags do not start hover activation");
        Require(gate.Begin(first, isOwnTabDrag: false, hasShellLocationData: true, delayMilliseconds: 1000) &&
                !gate.Begin(first, isOwnTabDrag: false, hasShellLocationData: true, delayMilliseconds: 1000),
            "eligible hover starts once and repeated DragOver does not reset the target");
        Require(gate.Begin(second, isOwnTabDrag: false, hasShellLocationData: true, delayMilliseconds: 1000) &&
                !gate.Leave(first) && ReferenceEquals(gate.TakeTarget(), second) && gate.TakeTarget() is null,
            "a stale leave cannot cancel a newer target and elapsed activation is consumed once");
        gate.Begin(first, isOwnTabDrag: false, hasShellLocationData: true, delayMilliseconds: 1000);
        Require(gate.Leave(first) && gate.TakeTarget() is null,
            "leaving or dropping cancels the pending target");

        var repoRoot = FindRepositoryRoot();
        var windowMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        Require(windowMarkup.Contains("Header=\"拖动悬停自动切换\"", StringComparison.Ordinal) &&
                windowMarkup.Contains("Tag=\"3000\"", StringComparison.Ordinal) &&
                windowMarkup.Contains("TabHoverActivation_Click", StringComparison.Ordinal),
            "Tab menu exposes the delay choices and connects the preference command");
        Require(paneCode.Contains("state.Owner.Tab_DragOver(sender, e)", StringComparison.Ordinal) &&
                paneCode.Contains("state.Owner.Tab_DragLeave(sender, e)", StringComparison.Ordinal) &&
                paneCode.Contains("_tabHoverActivationTimer.Start()", StringComparison.Ordinal) &&
                paneCode.Contains("Tabs.SelectedItem = target", StringComparison.Ordinal),
            "pane tab drag-over starts a cancellable timer and selects its current target");

        Console.WriteLine("Tab hover activation smoke: PASS");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Tab hover activation: " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Tab hover activation: " + message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the Explorer Dirs repository root.");
    }
}
