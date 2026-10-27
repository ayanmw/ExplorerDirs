using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class CrossPaneTabTransferSmoke
{
    internal static void Run()
    {
        Check(TabTransferPolicy.Resolve(true, 1, false, false, false) == TabTransferOperation.Reorder,
            "same-pane drags keep the existing reorder path");
        Check(TabTransferPolicy.Resolve(false, 2, false, false, false) == TabTransferOperation.Move,
            "cross-pane drag moves the original tab when its source retains another tab");
        Check(TabTransferPolicy.Resolve(false, 1, false, false, false) == TabTransferOperation.Reject,
            "a pane cannot lose its last tab through a move");
        Check(TabTransferPolicy.Resolve(false, 1, false, false, true) == TabTransferOperation.Copy,
            "Ctrl-drag copies even when the source has one tab");
        Check(TabTransferPolicy.Resolve(false, 2, true, false, true) == TabTransferOperation.Reject,
            "pinned and unpinned tab regions cannot be mixed by a drag");
        Check(TabTransferPolicy.Resolve(false, 2, true, true, false) == TabTransferOperation.Move,
            "pinned tabs can move within the pinned region");
        CheckWiring();
        Console.WriteLine("Cross-pane tab transfer smoke: PASS");
    }

    private static void CheckWiring()
    {
        var repoRoot = FindRepoRoot();
        var pane = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var browser = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        Check(pane.Contains("TabTransferPolicy.Resolve", StringComparison.Ordinal) &&
              pane.Contains("TransferTabTo", StringComparison.Ordinal) &&
              pane.Contains("TabDragPayload", StringComparison.Ordinal),
            "tab drag payload routes between pane owners");
        Check(pane.Contains("PrepareForRehost", StringComparison.Ordinal) &&
              browser.Contains("_retainBrowserOnDetach", StringComparison.Ordinal),
            "moving a tab retains its native ExplorerBrowser host for reparenting");
        Check(pane.Contains("CopyTabFrom", StringComparison.Ordinal) &&
              pane.Contains("AddressHistory.Copy()", StringComparison.Ordinal),
            "copying a tab creates an independent view from its captured settings");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml.cs")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
