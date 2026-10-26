using ExplorerDirs.App;

internal static class QdrLayoutSmoke
{
    internal static void Run()
    {
        // Independent expectations from Q-Dir 12.79 command and splitter geometry inspection.
        // Each entry gives the native pane numbers in application display order,
        // including hidden panes after the visible ones.
        var cases = new (int Native, int App, int[] Order)[]
        {
            (7, 9, [1, 2, 3, 4]),
            (8, 10, [1, 2, 3, 4]),
            (5, 11, [1, 2, 3, 4]),
            (6, 12, [1, 2, 3, 4]),
            (10, 7, [1, 2, 3, 4]),
            (2, 8, [1, 2, 3, 4]),
            (4, 3, [1, 3, 2, 4]),
            (1, 6, [1, 3, 4, 2]),
            (3, 2, [1, 2, 3, 4]),
            (9, 5, [1, 3, 4, 2]),
            (0, 4, [1, 2, 3, 4]),
            (11, 1, [1, 2, 3, 4])
        };
        foreach (var sample in cases)
        {
            var input = $"Q-Dir Version=12.79\nViewMode={sample.Native}\n";
            for (var pane = 1; pane <= 4; pane++)
            {
                var selected = pane % 2;
                input += $"Dir{pane}=C:\\native{pane}-active\nCurPos{pane}={selected}\nViewMode{pane}=3\n" +
                    $"Tree{pane}={(pane % 2 == 1 ? 1 : 0)}\n" +
                    $"Dir{pane}_{1 - selected}=C:\\native{pane}-other\n" +
                    $"CurPos{pane}_{1 - selected}={1 - selected}\nViewMode{pane}_{1 - selected}=4\n";
            }
            var imported = QdrImporter.Parse(input, (path, _) => path);
            Check(imported.Layout == sample.App, $"native layout {sample.Native} maps to {sample.App}");
            for (var appPane = 0; appPane < 4; appPane++)
            {
                var nativePane = sample.Order[appPane];
                Check(imported.SelectedTabs[appPane] == nativePane % 2 &&
                    imported.Panes[appPane][nativePane % 2].Path == $@"C:\native{nativePane}-active" &&
                    imported.Panes[appPane][nativePane % 2].View.Mode == 3 &&
                    imported.Panes[appPane][1 - nativePane % 2].Path == $@"C:\native{nativePane}-other",
                    $"native layout {sample.Native} preserves pane identity, tabs and selection");
                Check(imported.PaneTreeVisibility?[appPane] == (sample.Order[appPane] % 2 == 1),
                    $"native layout {sample.Native} keeps each TreeN flag with its mapped pane");
            }

            var panes = Enumerable.Range(0, 4).Select(pane => new List<TabSettings>
            {
                new($@"C:\app{pane}-first", ViewState.Default(3)),
                new($@"C:\app{pane}-second", ViewState.Default(4))
            }).ToList();
            var paneTrees = new[] { true, false, true, false };
            var state = new SessionState(sample.App, 4, panes, 0, [1, 0, 1, 0]) with
            {
                PaneTreeVisibility = paneTrees,
                TreeDisplayMode = TreeDisplayMode.PerPane
            };
            var pin = new PinnedTabEntry(2, 0, panes[2][0].Path);
            var text = QdrExporter.Render(state, [pin]);
            var fields = text.Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => line.Contains('=')).Select(line => line.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => pair[1]);
            Check(fields["ViewMode"] == sample.Native.ToString(), $"base export layout {sample.App}");
            for (var appPane = 0; appPane < 4; appPane++)
            {
                var nativePane = sample.Order[appPane];
                Check(fields[$"Tree{nativePane}"] == (paneTrees[appPane] ? "1" : "0"),
                    $"base export layout {sample.App} keeps each tree flag with its mapped pane");
            }
            for (var activePane = 0; activePane < LayoutPreset.Find(sample.App).Slots.Length; activePane++)
            {
                var focusedText = QdrExporter.Render(state with { ActivePane = activePane }, []);
                Check(focusedText.Split('\n').Contains($"Focus={sample.Order[activePane] - 1}\r"),
                    $"base export layout {sample.App} preserves active pane {activePane}");
                var nativeInput = QdrImporter.Parse(input + $"Focus={sample.Order[activePane] - 1}\n",
                    (path, _) => path);
                Check(nativeInput.ActivePane == activePane, "native Focus maps to the visible application pane");
                var focusedBasic = QdrImporter.Parse(string.Join('\n', focusedText.Split('\n').Where(line =>
                    !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase))), (path, _) => path);
                Check(focusedBasic.ActivePane == activePane, "base-only active pane survives round trip");
            }
            for (var appPane = 0; appPane < 4; appPane++)
            {
                var nativePane = sample.Order[appPane];
                var active = state.ActiveTabIndices![appPane];
                Check(fields[$"Dir{nativePane}"] == panes[appPane][active].Path &&
                    fields[$"CurPos{nativePane}"] == active.ToString() &&
                    fields[$"Dir{nativePane}_{1 - active}"] == panes[appPane][1 - active].Path,
                    $"export layout {sample.App} writes native pane fields without losing hidden panes");
            }
            var basic = QdrImporter.Parse(string.Join('\n', text.Split('\n').Where(line =>
                !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase))), (path, _) => path);
            Check(basic.Layout == state.Layout && basic.SelectedTabs.SequenceEqual(state.ActiveTabIndices!),
                "base-only import restores layout and active tabs");
            for (var pane = 0; pane < 4; pane++)
                Check(basic.Panes[pane].Select(tab => (tab.Path, tab.View.Mode))
                    .SequenceEqual(panes[pane].Select(tab => (tab.Path, tab.View.Mode))),
                    "base-only import restores all pane contents");
            var full = QdrImporter.Parse(text, (path, _) => path);
            Check(SessionStore.Render(full.ExtendedState!) == SessionStore.Render(state) &&
                full.Pins!.SequenceEqual([pin]) && full.ActivePane == state.ActivePane,
                "extension retains application pane and pin identities");
        }
        var sparse = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=4\n" +
            "Dir1=C:\\top\nDir3=C:\\bottom\n", (path, _) => path);
        Check(sparse.Layout == 3 && sparse.Panes[0][0].Path == @"C:\top" &&
            sparse.Panes[1][0].Path == @"C:\bottom" && sparse.Panes[2].Count == 0 &&
            sparse.Panes[3].Count == 0, "absent hidden panes do not shift the visible bottom pane");

        var stacked = new SessionState(3, 4, Enumerable.Range(0, 4)
            .Select(pane => new List<TabSettings> { new($@"C:\pane{pane}", ViewState.Default()) }).ToList(),
            0, [0, 0, 0, 0]);
        var edited = QdrExporter.Render(stacked, []).Replace("Dir3=C:\\pane1", "Dir3=C:\\external-bottom");
        var fallback = QdrImporter.Parse(edited, (path, _) => path);
        Check(fallback.ExtendedState is null && fallback.Layout == 3 &&
            fallback.Panes[1][0].Path == @"C:\external-bottom" &&
            fallback.Panes[2][0].Path == @"C:\pane2",
            "external native-pane edits invalidate extensions and respect geometry mapping");

        var focusEdited = QdrExporter.Render(stacked, []).Replace("Focus=0\r\n", "Focus=2\r\n");
        var focusFallback = QdrImporter.Parse(focusEdited, (path, _) => path);
        Check(focusFallback.ExtendedState is null && focusFallback.ActivePane == 1 &&
            focusFallback.Warnings.Any(warning => warning.Contains("扩展损坏")),
            "external Focus edits invalidate the old extension and select the mapped bottom pane");
        foreach (var focus in new[] { "1", "3", "4", "-2", "99999999999", "", "invalid" })
        {
            var rejected = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=4\nDir1=C:\\top\n" +
                "Dir3=C:\\bottom\nFocus=" + focus, (path, _) => path);
            Check(rejected.ActivePane is null && rejected.Warnings.Any(warning => warning.Contains("活动窗格")),
                "hidden or malformed focus is ignored visibly without losing tabs");
            Check(rejected.TabCount == 2, "invalid focus does not discard imported tabs");
        }
        foreach (var focus in new[] { "", "Focus=-1\n" })
        {
            var unspecified = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=4\nDir1=C:\\top\n" + focus,
                (path, _) => path);
            Check(unspecified.ActivePane is null && !unspecified.Warnings.Any(warning => warning.Contains("活动窗格")),
                "missing Focus and Q-Dir's -1 no-focus sentinel preserve current activity");
        }
        var emptyFocus = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=4\nDir1=C:\\top\nFocus=2",
            (path, _) => path);
        Check(emptyFocus.ActivePane is null && emptyFocus.Warnings.Any(warning => warning.Contains("活动窗格")),
            "focus on an empty imported pane cannot activate an unrelated existing tab");
        var unknownLayout = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=99\nDir1=C:\\top\nFocus=0",
            (path, _) => path);
        Check(unknownLayout.ActivePane is null && unknownLayout.Warnings.Any(warning => warning.Contains("活动窗格")),
            "unknown geometry does not guess active pane mapping");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("QDR layout: " + message);
    }
}
