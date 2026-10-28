using System.IO;
using System.Xml.Linq;

internal static class QDirBlankTabDoubleClickSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var markup = XDocument.Load(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var tabHeaderScroll = markup.Descendants().Single(element =>
            (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "TabHeaderScroll");
        var blankHeaderRightClickStart = paneCode.IndexOf("private void TabHeaderScroll_PreviewMouseRightButtonUp", StringComparison.Ordinal);
        var blankHeaderRightClickEnd = paneCode.IndexOf("private void Tabs_PreviewKeyDown", blankHeaderRightClickStart, StringComparison.Ordinal);
        var blankHeaderRightClickHandler = blankHeaderRightClickStart >= 0 && blankHeaderRightClickEnd > blankHeaderRightClickStart
            ? paneCode[blankHeaderRightClickStart..blankHeaderRightClickEnd] : string.Empty;
        var newTabHandlerStart = paneCode.IndexOf("private void TabHeaderScroll_PreviewMouseLeftButtonDown", StringComparison.Ordinal);
        var newTabHandlerEnd = paneCode.IndexOf("private void Tabs_PreviewKeyDown", newTabHandlerStart, StringComparison.Ordinal);
        var newTabHandler = newTabHandlerStart >= 0 && newTabHandlerEnd > newTabHandlerStart
            ? paneCode[newTabHandlerStart..newTabHandlerEnd] : string.Empty;
        var headerCloseStart = paneCode.IndexOf("header.MouseDown +=", StringComparison.Ordinal);
        var headerCloseEnd = paneCode.IndexOf("state.Tab.Header = header;", headerCloseStart, StringComparison.Ordinal);
        var headerCloseHandler = headerCloseStart >= 0 && headerCloseEnd > headerCloseStart
            ? paneCode[headerCloseStart..headerCloseEnd] : string.Empty;

        Require((string?)tabHeaderScroll.Attribute("PreviewMouseLeftButtonDown") ==
                "TabHeaderScroll_PreviewMouseLeftButtonDown",
            "blank tab-header area is wired to the double-click handler");
        Require(newTabHandler.Contains("e.ClickCount != 2", StringComparison.Ordinal) &&
                newTabHandler.Contains("e.ChangedButton != MouseButton.Left", StringComparison.Ordinal) &&
                newTabHandler.Contains("ItemsControl.ContainerFromElement(Tabs", StringComparison.Ordinal) &&
                newTabHandler.Contains("is TabItem", StringComparison.Ordinal) &&
                newTabHandler.Contains("NewTab();", StringComparison.Ordinal) &&
                newTabHandler.Contains("e.Handled = true", StringComparison.Ordinal),
            "tab-item double-click is excluded while blank-header double-click uses the existing new-tab command");
        Require(headerCloseHandler.Contains("e.ClickCount == 2", StringComparison.Ordinal) &&
                headerCloseHandler.Contains("CloseTab(state);", StringComparison.Ordinal),
            "the existing double-click on a tab item still closes that tab");

        Require((string?)tabHeaderScroll.Attribute("PreviewMouseRightButtonUp") ==
                "TabHeaderScroll_PreviewMouseRightButtonUp" &&
                blankHeaderRightClickHandler.Contains("ItemsControl.ContainerFromElement(Tabs", StringComparison.Ordinal) &&
                blankHeaderRightClickHandler.Contains("is TabItem", StringComparison.Ordinal) &&
                blankHeaderRightClickHandler.Contains("Tabs.SelectedItem", StringComparison.Ordinal) &&
                blankHeaderRightClickHandler.Contains("CreateTabContextMenu(state)", StringComparison.Ordinal) &&
                blankHeaderRightClickHandler.Contains("menu.PlacementTarget = TabHeaderScroll", StringComparison.Ordinal) &&
                blankHeaderRightClickHandler.Contains("menu.IsOpen = true", StringComparison.Ordinal),
            "right-clicking blank tab-strip space opens the selected tab's menu while tab-item menus remain native to their header");

        var tabContextMenuStart = paneCode.IndexOf("private ContextMenu CreateTabContextMenu(TabState state)", StringComparison.Ordinal);
        var tabContextMenuEnd = paneCode.IndexOf("private static void AddMenuItem", tabContextMenuStart, StringComparison.Ordinal);
        var tabContextMenu = tabContextMenuStart >= 0 && tabContextMenuEnd > tabContextMenuStart
            ? paneCode[tabContextMenuStart..tabContextMenuEnd] : string.Empty;
        Require(tabContextMenu.IndexOf("AddMenuItem(menu, \"使用 Explorer 浏览\"", StringComparison.Ordinal) is var explorerIndex && explorerIndex >= 0 &&
                explorerIndex < tabContextMenu.IndexOf("AddMenuItem(menu, \"新建标签页\"", StringComparison.Ordinal) &&
                tabContextMenu.Contains("OpenInSystemExplorer(state)", StringComparison.Ordinal),
            "system Explorer browsing is the first custom tab-context command and uses that tab's location");

        ExplorerDirs.SettingsSmoke.SystemExplorerLauncherSmoke.Run();
        Console.WriteLine("Q-Dir blank-tab double-click smoke: PASS");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
