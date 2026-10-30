namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTreeControlStyleSmoke
{
    internal static void Run()
    {
        const uint springExpand = 0x10;
        const uint disableTreeReorder = 0x1000;
        const uint noLabelEdit = 0x10000;
        var styles = ExplorerDirs.App.NativeShellTree.BuildControlStyles();
        if ((styles & springExpand) == 0)
            throw new InvalidOperationException("Native Shell tree must expand a folder while an external item is dragged over it.");
        if ((styles & disableTreeReorder) == 0 || (styles & noLabelEdit) == 0)
            throw new InvalidOperationException("Spring expansion must not enable tree-item reordering or label editing.");
        Console.WriteLine("Shell tree control style smoke: PASS (spring-expand; internal drag/drop and label edit disabled)");
    }
}
