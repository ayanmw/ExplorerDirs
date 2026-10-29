namespace ExplorerDirs.App;

internal static class QdrLaunchSession
{
    internal static SessionState Create(QdrImportResult import, string homeDirectory)
    {
        if (import.ExtendedState is { } extended) return extended;

        var panes = import.Panes.Select(pane => pane.Count > 0
            ? pane.ToList()
            : [new TabSettings(homeDirectory, ViewState.Default())]).ToList();
        return new SessionState(import.Layout ?? 4, 4, panes,
            Math.Clamp(import.ActivePane ?? 0, 0, 3), import.SelectedTabs.ToArray(), -1,
            import.PaneTreeVisibility, import.TreeDisplayMode, import.QdrTreeSplits, import.QdrColorMetadata);
    }
}
