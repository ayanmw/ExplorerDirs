using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTreeControlStyleSmoke
{
    internal static void Run()
    {
        const uint springExpand = 0x10;
        const uint disableTreeReorder = 0x1000;
        const uint noLabelEdit = 0x10000;
        var styles = ExplorerDirs.App.NativeShellTree.BuildControlStyles();
        if ((styles & springExpand) != 0)
            throw new InvalidOperationException("NSTCS_SPRINGEXPAND collapses other expanded nodes; it does not control drag-hover expansion.");
        if ((styles & disableTreeReorder) == 0 || (styles & noLabelEdit) == 0)
            throw new InvalidOperationException("Native Shell tree must keep internal drag/drop and label editing disabled.");
        Console.WriteLine("Shell tree control style smoke: PASS (no unrelated spring-expand behavior; internal reorder and label edit disabled)");

        nint hovered = 0;
        var leaves = 0;
        var events = new ExplorerDirs.App.NativeTreeEvents(() => { }, () => { }, (_, _, _) => false,
            item => hovered = item, () => leaves++, useExplicitShellIcons: false);
        ExplorerDirs.App.INativeTreeDropHandler drop = events;
        uint effect = 3;
        if (drop.OnDragEnter(123, 0, 1, 0, ref effect) != 0 || hovered != 123 || effect != 3 ||
            drop.OnDragOver(456, 0, 0, ref effect) != 0 || hovered != 456 || effect != 3)
            throw new InvalidOperationException("Namespace tree drag callbacks must report the hovered Shell item without changing the Shell-selected effect.");
        if (drop.OnDrop(456, 0, -1, 0, ref effect) != unchecked((int)0x80004001) || leaves != 1 || effect != 3)
            throw new InvalidOperationException("The custom hover handler must leave the final drop operation to the Shell control.");
        if (drop.OnDragLeave(456) != 0 || leaves != 2)
            throw new InvalidOperationException("Leaving the tree must cancel pending hover expansion.");
        Console.WriteLine("Shell tree drag-hover callback smoke: PASS (hover item routed, effect preserved, native drop not handled)");
        NavigationTreeDragHoverDelaySmoke.Run();

        var repoRoot = FindRepoRoot();
        var windowMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        foreach (var delay in ExplorerDirs.App.NavigationTreeDragHoverDelay.AllowedMilliseconds)
            if (!windowMarkup.Contains($"Tag=\"{delay}\"", StringComparison.Ordinal) ||
                !windowMarkup.Contains("Click=\"TreeDragHoverDelay_Click\"", StringComparison.Ordinal))
                throw new InvalidOperationException("The navigation menu must expose every supported drag-hover expansion delay.");

        var mainWindowSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var delayHandlerStart = mainWindowSource.IndexOf("private void TreeDragHoverDelay_Click(", StringComparison.Ordinal);
        var delayHandlerEnd = delayHandlerStart < 0 ? -1 :
            mainWindowSource.IndexOf("private void TreeSelectionAutoExpand_Click(", delayHandlerStart, StringComparison.Ordinal);
        var delayHandler = delayHandlerStart >= 0 && delayHandlerEnd > delayHandlerStart
            ? mainWindowSource[delayHandlerStart..delayHandlerEnd]
            : string.Empty;
        if (!mainWindowSource.Contains("_treeDragHoverExpandDelayMilliseconds = _treeDragHoverDelayStore.Load()", StringComparison.Ordinal) ||
            !delayHandler.Contains("_treeDragHoverDelayStore.Save(milliseconds)", StringComparison.Ordinal) ||
            !delayHandler.Contains("foreach (var pane in _panes) pane.SetTreeDragHoverExpandDelay(milliseconds)", StringComparison.Ordinal) ||
            !delayHandler.Contains("_sharedTreeHost?.SetDragHoverExpandDelay(milliseconds)", StringComparison.Ordinal) ||
            !mainWindowSource.Contains("_sharedTreeHost.SetDragHoverExpandDelay(_treeDragHoverExpandDelayMilliseconds)", StringComparison.Ordinal))
            throw new InvalidOperationException("The selected portable delay must load, persist, and reach existing and new shared trees.");

        var paneSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        if (!paneSource.Contains("_treeHost?.SetDragHoverExpandDelay(milliseconds)", StringComparison.Ordinal) ||
            !paneSource.Contains("_treeHost.SetDragHoverExpandDelay(_treeDragHoverExpandDelayMilliseconds)", StringComparison.Ordinal))
            throw new InvalidOperationException("The selected portable delay must reach existing and newly created per-pane trees.");
        Console.WriteLine("Shell tree drag-hover preference smoke: PASS (six menu delays route to shared and per-pane trees)");

        var hostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ShellTreeHost.cs"));
        var hideHandlerStart = hostSource.IndexOf("IsVisibleChanged += (_, _) =>", StringComparison.Ordinal);
        var hideHandlerEnd = hostSource.IndexOf("};", hideHandlerStart, StringComparison.Ordinal);
        var hideHandler = hideHandlerStart >= 0 && hideHandlerEnd > hideHandlerStart
            ? hostSource[hideHandlerStart..hideHandlerEnd]
            : string.Empty;
        if (!hideHandler.Contains("CancelDragHoverExpansion()", StringComparison.Ordinal))
            throw new InvalidOperationException("Hiding the namespace tree must cancel its pending drag-hover expansion.");
        Console.WriteLine("Shell tree drag-hover lifecycle smoke: PASS (pending expansion canceled when hidden)");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }
}
