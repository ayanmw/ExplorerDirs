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
    }
}
