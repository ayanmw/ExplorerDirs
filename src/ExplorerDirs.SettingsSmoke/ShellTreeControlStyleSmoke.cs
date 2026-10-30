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
    }
}
