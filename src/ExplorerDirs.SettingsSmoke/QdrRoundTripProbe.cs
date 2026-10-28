using System.IO;
using ExplorerDirs.App;

internal static class QdrRoundTripProbe
{
    public static void Run(string path)
    {
        var imported = QdrImporter.Load(path);
        if (imported.Layout is not { } layout)
            throw new InvalidDataException("Q-Dir 布局未映射，无法验证应用状态往返。");

        var state = QdrLaunchSession.Create(imported,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var sourceState = SessionStore.Render(state);
        var exported = QdrExporter.Render(state, imported.Pins ?? [], imported.Proportions,
            imported.NavigationTreeWidths);
        var roundTripped = QdrImporter.Parse(exported, QdrImporter.ResolveLocation);
        if (roundTripped.ExtendedState is not { } restored ||
            SessionStore.Render(restored) != sourceState)
            throw new InvalidDataException("本程序导出的扩展状态未能完整往返。");
        VerifyBaseProjection(state, imported, exported);

        Console.WriteLine($"QDR round-trip: tabs={imported.TabCount}; panes=" +
            string.Join('/', imported.Panes.Select(pane => pane.Count)) +
            $"; native-view-snapshots={imported.Panes.SelectMany(pane => pane).Count(tab => tab.View.DisplayColumns is not null)}; " +
            $"layout={layout}; extended-session-match=True; base-qdr-fields-match=True");
    }

    private static void VerifyBaseProjection(SessionState expected, QdrImportResult source, string exported)
    {
        var baseOnlyText = string.Join('\n', exported.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
        var basic = QdrImporter.Parse(baseOnlyText, QdrImporter.ResolveLocation);
        if (basic.ExtendedState is not null || basic.Layout != expected.Layout ||
            basic.TabCount != expected.Panes.Sum(pane => pane.Count) ||
            !basic.Panes.Select(pane => pane.Count).SequenceEqual(expected.Panes.Select(pane => pane.Count)) ||
            !basic.SelectedTabs.SequenceEqual(expected.ActiveTabIndices!) || basic.ActivePane != expected.ActivePane)
            throw new InvalidDataException("Q-Dir 基础字段未能保留布局、窗格、标签选择或活动窗格。");

        var expectedTreeMode = expected.TreeDisplayMode ?? TreeDisplayMode.PerPane;
        var expectedTrees = expected.PaneTreeVisibility ?? [false, false, false, false];
        if (basic.TreeDisplayMode != expectedTreeMode ||
            !basic.PaneTreeVisibility!.SequenceEqual(expectedTrees))
            throw new InvalidDataException("Q-Dir 基础字段未能保留目录树状态。");

        for (var pane = 0; pane < expected.Panes.Count; pane++)
        for (var tab = 0; tab < expected.Panes[pane].Count; tab++)
        {
            var expectedTab = expected.Panes[pane][tab];
            var actualTab = basic.Panes[pane][tab];
            var expectedMode = expectedTab.View.Mode == 8 ? 4 : expectedTab.View.Mode;
            if (!string.Equals(actualTab.Path, expectedTab.Path, StringComparison.OrdinalIgnoreCase) ||
                actualTab.View.Mode != expectedMode)
                throw new InvalidDataException("Q-Dir 基础字段未能保留目录或查看方式。");

            if (expectedTab.View.DisplayColumns is null) continue;
            if (actualTab.View.IconSize != expectedTab.View.IconSize ||
                actualTab.View.GroupKey != expectedTab.View.GroupKey ||
                actualTab.View.GroupAscending != expectedTab.View.GroupAscending ||
                !actualTab.View.SortColumns.SequenceEqual(expectedTab.View.SortColumns) ||
                actualTab.View.DisplayColumns is not { } columns ||
                !columns.SequenceEqual(expectedTab.View.DisplayColumns))
                throw new InvalidDataException("Q-Dir 基础字段未能保留原生图标、排序、分组或列设置。");
        }

        if (source.Proportions is { } proportions &&
            (basic.Proportions is null || proportions.Any(pair =>
                !basic.Proportions.TryGetValue(pair.Key, out var value) || value != pair.Value)))
            throw new InvalidDataException("Q-Dir 基础字段未能保留布局分隔比例。");
    }
}
