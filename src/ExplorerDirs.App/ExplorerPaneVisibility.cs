namespace ExplorerDirs.App;

internal enum ExplorerShellPane
{
    None,
    Preview,
    Details
}

internal static class ExplorerPaneVisibility
{
    internal static readonly Guid PreviewPaneId = new("893C63D1-45C8-4D17-BE19-223BE71BE365");
    internal static readonly Guid DetailsPaneId = new("43ABF98B-89B8-472D-B9CE-E69B8229F019");
    internal static readonly Guid NavigationPaneId = new("CB316B22-25F7-42B8-8A09-540D23A43C2F");
    internal static readonly Guid CommandsPaneId = new("D9745868-CA5F-4A76-91CD-F5A129FBB076");
    internal static readonly Guid OrganizeCommandsPaneId = new("72E81700-E3EC-4660-BF24-3C3B7B648806");
    internal static readonly Guid ViewCommandsPaneId = new("21F7C32D-EEAA-439B-BB51-37B96FD6A943");
    internal static readonly Guid QueryPaneId = new("65BCDE4F-4F07-4F27-83A7-1AFCA4DF7DDD");
    internal static readonly Guid AdvancedQueryPaneId = new("B4E9DB8B-34BA-4C39-B5CC-16A1BD2C411C");
    internal static readonly Guid StatusBarPaneId = new("65FE56CE-5CFE-4BC4-AD8A-7AE3FE7E8F7C");
    internal static readonly Guid RibbonPaneId = new("D27524A8-C9F2-4834-A106-DF8889FD4F37");

    internal static uint GetState(Guid pane, ExplorerShellPane visiblePane)
    {
        if (pane == PreviewPaneId)
            return visiblePane == ExplorerShellPane.Preview
                ? 0x00030001u : 0x00030002u; // EPS_FORCE | INITIALSTATE | DEFAULT_ON/OFF

        if (pane == DetailsPaneId)
            return visiblePane == ExplorerShellPane.Details
                ? 0x00030001u : 0x00030002u; // EPS_FORCE | INITIALSTATE | DEFAULT_ON/OFF

        if (pane == NavigationPaneId || pane == CommandsPaneId ||
            pane == OrganizeCommandsPaneId || pane == ViewCommandsPaneId || pane == QueryPaneId ||
            pane == AdvancedQueryPaneId || pane == StatusBarPaneId || pane == RibbonPaneId)
            return 0x00030002u; // Keep ExplorerBrowser's other frame panes out of our custom layout.

        return 0u; // EPS_DONTCARE
    }
}
