using ExplorerDirs.App;

internal static class ExplorerPaneVisibilitySmoke
{
    internal static void Run()
    {
        Require(ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.PreviewPaneId, ExplorerShellPane.None) == 0x00030002,
            "preview pane starts hidden when not requested");
        Require(ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.PreviewPaneId, ExplorerShellPane.Preview) == 0x00030001,
            "preview pane can be requested for the native frame");
        Require(ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.DetailsPaneId, ExplorerShellPane.None) == 0x00030002,
            "details pane starts hidden when not requested");
        Require(ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.DetailsPaneId, ExplorerShellPane.Details) == 0x00030001,
            "details pane can be requested for the native frame");
        Require(ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.PreviewPaneId, ExplorerShellPane.Details) == 0x00030002 &&
                ExplorerPaneVisibility.GetState(ExplorerPaneVisibility.DetailsPaneId, ExplorerShellPane.Preview) == 0x00030002,
            "preview and details native frames remain mutually exclusive");
        foreach (var pane in new[]
        {
            ExplorerPaneVisibility.DetailsPaneId, ExplorerPaneVisibility.NavigationPaneId,
            ExplorerPaneVisibility.CommandsPaneId, ExplorerPaneVisibility.OrganizeCommandsPaneId,
            ExplorerPaneVisibility.ViewCommandsPaneId, ExplorerPaneVisibility.QueryPaneId,
            ExplorerPaneVisibility.AdvancedQueryPaneId, ExplorerPaneVisibility.StatusBarPaneId,
            ExplorerPaneVisibility.RibbonPaneId
        })
            Require(ExplorerPaneVisibility.GetState(pane, ExplorerShellPane.Preview) == 0x00030002,
                $"native pane {pane:B} remains hidden in the custom multi-pane layout");
        Require(ExplorerPaneVisibility.GetState(Guid.NewGuid(), ExplorerShellPane.Preview) == 0,
            "unknown Shell panes retain their default state");
        Console.WriteLine("Explorer pane visibility smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
