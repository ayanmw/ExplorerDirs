using ExplorerDirs.App;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class QdrExportSmoke
{
    internal static void Run(string root)
    {
        var key = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
        var typeKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);
        var view = new ViewState(1, 96, [new(key, -1)], key, false, [new(key, 240)]);
        var state = new SessionState(5, 3,
            [[new(@"C:\中文 & a=b", view, quickFilter: new QuickFilterSettings("*.txt;*.md", false, true,
                  [new ColumnValueFilter(typeKey, ["TXT 文件", "BIN 文件"])]), previewPaneVisible: true),
              new(@"D:\other", ViewState.Default(), new QuickFilterSettings("*.tmp", true, false), detailsPaneVisible: true)],
             [new(@"Z:\offline", view, new QuickFilterSettings("*.csv", false, false))], [new(@"\\server\share", view)],
             [new(@"C:\hidden", view)]], 2, [1, 0, 0, 0], PaneTreeVisibility: [true, false, true, false],
            TreeDisplayMode: TreeDisplayMode.Shared);
        var opaqueState = state with
        {
            QdrOpaqueMetadata = new QdrOpaqueMetadata(
                [new QdrOpaqueField("FutureRootOption", "value=with=equals")],
                [new QdrOpaqueSection("FutureSection", ["Option=42", "Dir2=C:\\not-a-root-tab"])])
        };
        var opaqueText = QdrExporter.Render(opaqueState, []);
        var opaqueRoundTrip = QdrImporter.Parse(opaqueText, (value, _) => value);
        var opaqueSession = SessionStore.ParseComplete(SessionStore.Render(opaqueState));
        Check(opaqueText.Contains("FutureRootOption=value=with=equals\r\n") &&
            opaqueText.Contains("[FutureSection]\r\nOption=42\r\nDir2=C:\\not-a-root-tab\r\n") &&
            SameOpaqueMetadata(opaqueRoundTrip.QdrOpaqueMetadata, opaqueState.QdrOpaqueMetadata) &&
            SameOpaqueMetadata(opaqueRoundTrip.ExtendedState?.QdrOpaqueMetadata, opaqueState.QdrOpaqueMetadata) &&
            SameOpaqueMetadata(opaqueSession?.QdrOpaqueMetadata, opaqueState.QdrOpaqueMetadata),
            "unsupported Q-Dir root fields and sections survive export/import and portable session persistence");
        var editedOpaqueText = opaqueText.Replace("FutureRootOption=value=with=equals", "FutureRootOption=external-edit", StringComparison.Ordinal);
        var editedOpaque = QdrImporter.Parse(editedOpaqueText, (value, _) => value);
        Check(editedOpaque.ExtendedState is null &&
            editedOpaque.QdrOpaqueMetadata?.RootFields.Single().Value == "external-edit",
            "editing an opaque Q-Dir field invalidates stale Explorer Dirs extension data");
        var editedOpaqueSection = QdrImporter.Parse(
            opaqueText.Replace("Option=42", "Option=43", StringComparison.Ordinal), (value, _) => value);
        Check(editedOpaqueSection.ExtendedState is null &&
            editedOpaqueSection.QdrOpaqueMetadata?.Sections.Single().Rows[0] == "Option=43",
            "editing an opaque Q-Dir section invalidates stale Explorer Dirs extension data");
        var pins = new[] { new PinnedTabEntry(0, 0, state.Panes[0][0].Path) };
        var treeWidths = NavigationTreeWidths.Defaults with
        { Shared = 245, Pane0 = 135, Pane1 = 175, Pane2 = 215, Pane3 = 285 };
        var proportions = new Dictionary<int, LayoutProportions> { [state.Layout] = new(0.6, 0.4) };
        var widthText = QdrExporter.Render(state, pins, proportions, treeWidths);
        var widthRoundTrip = QdrImporter.Parse(widthText, (value, _) => value);
        Check(widthText.Contains("ExplorerDirs.FormatVersion=4\r\n") &&
            widthRoundTrip.NavigationTreeWidths == treeWidths &&
            widthRoundTrip.Proportions?.GetValueOrDefault(state.Layout) == proportions[state.Layout],
            "extension v4 round-trips all five independent navigation-tree widths with layout proportions");
        var splitState = state with
        { QdrTreeSplits = new QdrTreeSplitState("002500", null, "17", "2147483647") };
        var splitText = QdrExporter.Render(splitState, pins);
        var splitRoundTrip = QdrImporter.Parse(splitText, (value, _) => value);
        Check(splitText.Contains("Tree1split=002500\r\n") &&
            !splitText.Contains("Tree2split=", StringComparison.Ordinal) &&
            splitText.Contains("Tree3split=17\r\n") &&
            splitRoundTrip.QdrTreeSplits == splitState.QdrTreeSplits &&
            splitRoundTrip.ExtendedState?.QdrTreeSplits == splitState.QdrTreeSplits,
            "opaque Q-Dir split values preserve exact native pane keys and text through export/import");
        var colorState = state with
        { QdrColorMetadata = new QdrColorMetadata("1", "0", "dark", ["1=*.png=1=00FF00=-1=1=-1=0", "0=#BG-A=1=-1=-1=1=-1=0"]) };
        var colorText = QdrExporter.Render(colorState, pins);
        var colorRoundTrip = QdrImporter.Parse(colorText, (value, _) => value);
        Check(colorText.Contains("\r\nuseColor=1\r\n") && colorText.Contains("\r\nuseTreeColor=0\r\n") &&
            colorText.Contains("\r\ncolorthema=dark\r\n") &&
            colorText.Contains("\r\n[Filter2]\r\n1=*.png=1=00FF00=-1=1=-1=0\r\n0=#BG-A=1=-1=-1=1=-1=0\r\n"),
            "opaque Q-Dir color switches, theme, and ordered Filter2 rows export as Q-Dir base fields");
        var sameBaseColor = SameColorMetadata(colorRoundTrip.QdrColorMetadata, colorState.QdrColorMetadata);
        var sameExtensionColor = SameColorMetadata(colorRoundTrip.ExtendedState?.QdrColorMetadata, colorState.QdrColorMetadata);
        Check(sameBaseColor && sameExtensionColor,
            $"opaque Q-Dir color metadata round-trips in the base import and portable extension (base={sameBaseColor}, extension={sameExtensionColor}, extended={colorRoundTrip.ExtendedState is not null}, warnings={string.Join(" | ", colorRoundTrip.Warnings)})");
        var editedColorRule = colorText.Replace("1=*.png=1=00FF00", "1=*.png=1=FF00FF", StringComparison.Ordinal);
        var editedColorImport = QdrImporter.Parse(editedColorRule, (value, _) => value);
        Check(editedColorImport.ExtendedState is null && editedColorImport.QdrColorMetadata?.Filter2Rows[0].Contains("FF00FF") == true,
            "editing an opaque Filter2 rule invalidates the stale Explorer Dirs extension while retaining edited base data");
        var baseOnlyColorText = string.Join('\n', colorText.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
        Check(SameColorMetadata(QdrImporter.Parse(baseOnlyColorText, (value, _) => value).QdrColorMetadata,
                colorState.QdrColorMetadata),
            "Q-Dir color metadata remains available when the application extension is removed");
        var editedSplitText = splitText.Replace("Tree1split=002500", "Tree1split=2500", StringComparison.Ordinal);
        var editedSplit = QdrImporter.Parse(editedSplitText, (value, _) => value);
        Check(editedSplit.ExtendedState is null && editedSplit.QdrTreeSplits?.Pane1 == "2500",
            "editing a preserved Q-Dir split base field invalidates the stale application extension");
        var backupPath = Path.Combine(root, "qdr-background-backup", "session.bak");
        var backupWarnings = Task.Run(() => QdrExporter.SaveBackup(backupPath, state, pins,
            proportions, treeWidths)).GetAwaiter().GetResult();
        var backupImport = QdrImporter.Parse(QdrImporter.ReadText(backupPath + ".qdr"), (value, _) => value);
        Check(backupWarnings == 0 && File.ReadAllText(backupPath) == SessionStore.Render(state) &&
            backupImport.ExtendedState is not null && backupImport.NavigationTreeWidths == treeWidths &&
            backupImport.Proportions?.GetValueOrDefault(state.Layout) == proportions[state.Layout],
            "background import backup preserves the exact session and complete QDR snapshot");
        var failedBackup = Path.Combine(root, "qdr-background-backup", "partial.bak");
        Directory.CreateDirectory(failedBackup + ".qdr");
        var backupFailed = false;
        try { Task.Run(() => QdrExporter.SaveBackup(failedBackup, state, pins,
            proportions, treeWidths)).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { backupFailed = true; }
        Check(backupFailed && File.ReadAllText(failedBackup) == SessionStore.Render(state),
            "a QDR backup write failure preserves the exact session recovery file and reports failure");        var proportionsOnlyText = QdrExporter.Render(state, pins, proportions);
        var v3State = state with { Layout = 4 };
        var v3Text = QdrExporter.Render(v3State, pins,
            new Dictionary<int, LayoutProportions> { [4] = new(0.6, 0.4, BottomHorizontal: 0.55) });
        Check(v3Text.Contains("ExplorerDirs.FormatVersion=3\r\n") &&
            QdrImporter.Parse(v3Text, (value, _) => value).NavigationTreeWidths is null,
            "v3 extensions preserve proportions without overriding current navigation-tree widths");
        Check(widthText.Split("\r\n").Single(line => line.StartsWith(QdrExporter.Prefix + "BaseHash=")) ==
            proportionsOnlyText.Split("\r\n").Single(line => line.StartsWith(QdrExporter.Prefix + "BaseHash=")),
            "adding app-specific tree widths does not change the Q-Dir base hash");
        Check(QdrImporter.Parse(proportionsOnlyText, (value, _) => value).NavigationTreeWidths is null,
            "legacy extension versions do not override current navigation-tree widths");

        var widthPayloadLine = widthText.Split("\r\n").Single(line => line.StartsWith(QdrExporter.Prefix + "State="));
        var widthPayload = Encoding.UTF8.GetString(Convert.FromBase64String(widthPayloadLine[(QdrExporter.Prefix + "State=").Length..]));
        foreach (var (widthKey, original, invalidValue) in new[]
        {
            ("Shared", 245, 95), ("Shared", 245, 601),
            ("Pane0", 135, 95), ("Pane0", 135, 601),
            ("Pane1", 175, 95), ("Pane1", 175, 601),
            ("Pane2", 215, 95), ("Pane2", 215, 601),
            ("Pane3", 285, 95), ("Pane3", 285, 601)
        })
        {
            var invalidWidthPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                widthPayload.Replace($"\"{widthKey}\":{original}", $"\"{widthKey}\":{invalidValue}")));
            var invalidWidthText = widthText.Replace(widthPayloadLine,
                QdrExporter.Prefix + "State=" + invalidWidthPayload);
            var invalidWidthImport = QdrImporter.Parse(invalidWidthText, (value, _) => value);
            Check(invalidWidthImport.ExtendedState is null && invalidWidthImport.NavigationTreeWidths is null &&
                invalidWidthImport.Warnings.Any(warning => warning.Contains("扩展损坏")),
                $"out-of-range {widthKey} tree width rejects the whole custom snapshot and preserves base import");
        }
        var serializedTreeWidths = "\"NavigationTreeWidths\":{\"Shared\":245,\"Pane0\":135,\"Pane1\":175,\"Pane2\":215,\"Pane3\":285}";
        var missingWidthPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            widthPayload.Replace("," + serializedTreeWidths, "")));
        var missingWidthImport = QdrImporter.Parse(widthText.Replace(widthPayloadLine,
            QdrExporter.Prefix + "State=" + missingWidthPayload), (value, _) => value);
        Check(missingWidthImport.ExtendedState is null && missingWidthImport.NavigationTreeWidths is null &&
            missingWidthImport.Warnings.Any(warning => warning.Contains("扩展损坏")),
            "missing v4 tree widths reject the whole custom snapshot and preserve base import");

        var text = QdrExporter.Render(state, pins);
        var ordinaryPrepared = QdrExporter.Prepare(state, pins);
        Check(ordinaryPrepared.Text == text && ordinaryPrepared.UnencodedLocationCount == 0,
            "ordinary and offline paths without percent signs retain the original export without Shell binding");
        var contentModePanes = state.Panes.Select(pane => pane.ToList()).ToList();
        contentModePanes[0][0] = contentModePanes[0][0] with { View = view with { Mode = 8 } };
        var contentModeState = state with { Panes = contentModePanes };
        var contentModeText = QdrExporter.Render(contentModeState, pins);
        var contentModeRoundTrip = QdrImporter.Parse(contentModeText, (value, _) => value);
        var contentModeBase = QdrImporter.Parse(string.Join('\n', contentModeText.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase))), (value, _) => value);
        var contentNativeBlob = contentModeText.Split("\r\n").Single(line => line.StartsWith("ColsAndSort1_0="))["ColsAndSort1_0=".Length..];
        var hasDetailsNativeBlob = QdrViewSettings.TryRead(contentNativeBlob, out var contentBaseView) &&
            contentBaseView.Mode == 4;
        Check(contentModeText.Contains("\r\nViewMode1_0=4\r\n") &&
            contentModeRoundTrip.Panes[0][0].View.Mode == 8 &&
            contentModeBase.Panes[0][0].View.Mode == 4 && hasDetailsNativeBlob,
            "Q-Dir base export maps unsupported FVM_CONTENT to Details in both view fields while the app extension retains Content");
        Check(text.Contains("\r\nTree1=1\r\n") && text.Contains("\r\nTree2=0\r\n") &&
            text.Contains("\r\nTree3=0\r\n") && text.Contains("\r\nTree4=1\r\n"),
            "export writes Q-Dir base tree flags in native pane order");
        Check(text.Contains("\r\nVorschau=0\r\n"),
            "Q-Dir base preview field reflects the active pane's selected tab");
        var previewText = QdrExporter.Render(state with { ActivePane = 0, ActiveTabIndices = [0, 0, 0, 0] }, pins);
        Check(previewText.Contains("\r\nVorschau=1\r\n"),
            "Q-Dir base preview field is enabled when the active tab uses preview");
        var previewBase = string.Join('\n', previewText.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
        var previewBaseImport = QdrImporter.Parse(previewBase, (value, _) => value);
        Check(previewBaseImport.Panes.SelectMany(pane => pane).All(tab => tab.PreviewPaneVisible),
            "Q-Dir base preview state imports to every tab when our extension is absent");
        var changedPreview = QdrImporter.Parse(previewText.Replace("\r\nVorschau=1\r\n", "\r\nVorschau=0\r\n"),
            (value, _) => value);
        Check(changedPreview.ExtendedState is null &&
            changedPreview.Panes.SelectMany(pane => pane).All(tab => !tab.PreviewPaneVisible),
            "editing the Q-Dir base preview field invalidates stale extension state");
        var legacyPreviewLines = text.Split("\r\n").Where(line => !line.StartsWith("Vorschau=", StringComparison.Ordinal)).ToArray();
        var legacyPreviewBase = legacyPreviewLines.Where(line => line.Contains('=') &&
                !line.StartsWith(QdrExporter.Prefix) && !line.StartsWith(';'))
            .Select(line => line.Split('=', 2)).OrderBy(pair => pair[0], StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair[0].ToUpperInvariant() + "=" + pair[1]);
        var legacyPreviewHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', legacyPreviewBase))));
        var legacyPreviewText = string.Join("\r\n", legacyPreviewLines.Select(line =>
            line.StartsWith(QdrExporter.Prefix + "BaseHash=")
                ? QdrExporter.Prefix + "BaseHash=" + legacyPreviewHash : line));
        Check(QdrImporter.Parse(legacyPreviewText, (value, _) => value).ExtendedState is not null,
            "older exports without a Q-Dir preview field retain a valid extension hash");
        Check(text.Contains("Dir1=D:\\other") && text.Contains("Dir1_0=C:\\中文 & a=b") &&
            !text.Contains("Dir1_1="), "base fields follow current-tab slot convention");
        var imported = QdrImporter.Parse(text, (value, _) => value);
        Check(imported.PaneTreeVisibility?.SequenceEqual([true, false, true, false]) == true &&
            imported.ExtendedState?.PaneTreeVisibility?.SequenceEqual([true, false, true, false]) == true &&
            imported.TreeDisplayMode == TreeDisplayMode.Shared &&
            imported.ExtendedState?.TreeDisplayMode == TreeDisplayMode.Shared,
            "tree display mode and per-pane visibility survive the Explorer Dirs extension round-trip");
        foreach (var mode in Enum.GetValues<TreeDisplayMode>())
        {
            var modeText = QdrExporter.Render(state with { TreeDisplayMode = mode }, pins);
            var nativeMode = mode switch
            {
                TreeDisplayMode.None => 0,
                TreeDisplayMode.Shared => 1,
                _ => 2
            };
            Check(modeText.Contains($"\r\ntree_mode={nativeMode}\r\n"),
                $"Q-Dir base field exports {mode} as tree_mode={nativeMode}");
            var modeRoundTrip = QdrImporter.Parse(modeText, (value, _) => value);
            Check(modeRoundTrip.TreeDisplayMode == mode && modeRoundTrip.ExtendedState?.TreeDisplayMode == mode,
                $"the {mode} directory-tree mode survives the Q-Dir application extension round-trip");
            var baseOnlyModeText = string.Join('\n', modeText.Split('\n').Where(line =>
                !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
            Check(QdrImporter.Parse(baseOnlyModeText, (value, _) => value).TreeDisplayMode == mode,
                $"Q-Dir base-only tree_mode restores {mode} without the Explorer Dirs extension");
        }
        var editedTreeMode = QdrImporter.Parse(text.Replace("\r\ntree_mode=1\r\n", "\r\ntree_mode=2\r\n"),
            (value, _) => value);
        Check(editedTreeMode.ExtendedState is null && editedTreeMode.TreeDisplayMode == TreeDisplayMode.PerPane,
            "editing the Q-Dir tree_mode field invalidates stale Explorer Dirs extension state");
        var nativeRatios = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=3\nSP0=5000\nSP1=3000\nSP2=5000\nDir1=C:\\fixture\n", (value, _) => value);
        Check(!nativeRatios.Warnings.Any(w => w.Contains("不导入") && w.Contains("分隔比例")),
            "supported native divider proportions must no longer be omitted");
        // Recreate the previous v1 envelope, whose base fields omitted Focus.
        // Its digest must remain valid; adding Focus later is an external edit.
        var legacyLines = text.Split("\r\n").Where(line => !line.StartsWith("Focus=")).ToArray();
        var legacyBase = legacyLines.Where(line => line.Contains('=') &&
                !line.StartsWith(QdrExporter.Prefix) && !line.StartsWith(';'))
            .Select(line => line.Split('=', 2)).OrderBy(pair => pair[0], StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair[0].ToUpperInvariant() + "=" + pair[1]);
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', legacyBase))));
        var legacyText = string.Join("\r\n", legacyLines.Select(line =>
            line.StartsWith(QdrExporter.Prefix + "BaseHash=") ? QdrExporter.Prefix + "BaseHash=" + legacyHash : line));
        var legacy = QdrImporter.Parse(legacyText, (value, _) => value);
        Check(legacy.ExtendedState is not null && legacy.ActivePane == 2,
            "previous extension without native Focus still restores its application active pane");
        var legacyEdited = QdrImporter.Parse(legacyText + "Focus=0\r\n", (value, _) => value);
        Check(legacyEdited.ExtendedState is null && legacyEdited.ActivePane == 0,
            "adding native Focus to an old export invalidates its stale extension");
        Check(imported.Layout == 5 && imported.Panes[3][0].Path == @"C:\hidden" &&
            imported.SelectedTabs[0] == 1, "layout, hidden pane and selected tab round-trip");
        Check(imported.Panes[0][0].View.SortColumns.SequenceEqual(view.SortColumns) &&
            imported.Panes[0][0].View.DisplayColumns!.SequenceEqual(view.DisplayColumns!),
            "sort and columns survive extension round-trip");
        Check(imported.Panes[0][0].PreviewPaneVisible && !imported.Panes[0][1].PreviewPaneVisible,
            "per-tab preview visibility survives the QDR extension round-trip");
        Check(imported.Panes[0][1].DetailsPaneVisible && !imported.Panes[0][1].PreviewPaneVisible,
            "per-tab details visibility survives the QDR extension round-trip");
        Check(SameFilter(imported.Panes[0][0].QuickFilter, state.Panes[0][0].QuickFilter) &&
            SameFilter(imported.Panes[0][1].QuickFilter, state.Panes[0][1].QuickFilter) &&
            SameFilter(imported.Panes[1][0].QuickFilter, state.Panes[1][0].QuickFilter),
            "per-tab quick filters survive the Explorer Dirs QDR extension round-trip");
        Check(imported.Warnings.Any(warning => warning.Contains("每标签快速筛选", StringComparison.Ordinal)),
            "QDR import preview names per-tab quick-filter state restored from the application extension");
        Check(imported.ExtendedState?.ActivePane == 2 && imported.ExtendedState.DefaultViewMode == 3 &&
            imported.Pins!.SequenceEqual(pins), "active pane, default view and pins survive");
        var shellPanes = state.Panes.Select(pane => pane.ToList()).ToList();
        shellPanes[0].Add(new("shell:RecycleBinFolder", ViewState.Default()));
        var shellState = state with { Panes = shellPanes, ActiveTabIndices = [1, 0, 0, 0] };
        var shellText = QdrExporter.Render(shellState, []);
        var shellRoundTrip = QdrImporter.Parse(shellText,
            (value, _) => ShellLocationIdentity.TryNormalize(value, out var identity, out _) ? identity : null);
        Check(shellText.Contains("Dir1_2=shell:RecycleBinFolder") &&
            shellRoundTrip.Panes.SelectMany(pane => pane).Any(tab => tab.Path == "shell:RecycleBinFolder"),
            "supported Shell identities survive Q-Dir base and application-extension round-trips");
        shellPanes[0].Add(new("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", ViewState.Default()));
        var absoluteShellState = state with { Panes = shellPanes, ActiveTabIndices = [3, 0, 0, 0] };
        var absoluteShellText = QdrExporter.Render(absoluteShellState, []);
        var absoluteShellRoundTrip = QdrImporter.Parse(absoluteShellText,
            (value, _) => ShellLocationIdentity.TryNormalize(value, out var identity, out _) ? identity : null);
        var absoluteShellBasicText = string.Join('\n', absoluteShellText.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
        var absoluteShellBasic = QdrImporter.Parse(absoluteShellBasicText,
            (value, _) => ShellLocationIdentity.TryNormalize(value, out var identity, out _) ? identity : null);
        Check(absoluteShellText.Contains("Dir1=shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            "absolute Shell parsing names use Q-Dir's documented shell:::{GUID} text form in base fields");
        Check(absoluteShellRoundTrip.Panes[0][absoluteShellRoundTrip.SelectedTabs[0]].Path ==
                "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            "absolute Shell parsing names and the active tab import with the app extension");
        Check(absoluteShellBasic.Panes[0][absoluteShellBasic.SelectedTabs[0]].Path ==
                "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            "Q-Dir-compatible absolute Shell text imports from base fields without the app extension");
        foreach (var layout in LayoutPreset.All)
        {
            var sample = state with { Layout = layout.Id, ActivePane = 0 };
            var read = QdrImporter.Parse(QdrExporter.Render(sample, pins), (value, _) => value);
            Check(SessionStore.Render(read.ExtendedState!) == SessionStore.Render(sample),
                "all application layouts and complete view state round-trip");
        }
        var basicText = string.Join('\n', text.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
        var basic = QdrImporter.Parse(basicText, (value, _) => value);
        Check(basic.ExtendedState is null && basic.Layout == 5 && basic.TabCount == 5 &&
            basic.SelectedTabs[0] == 1 && basic.Panes[0][0].Path == state.Panes[0][0].Path,
            "base fields remain usable without the extension");
        Check(basic.Panes[0][0].View.IconSize == view.IconSize &&
            basic.Panes[0][0].View.GroupKey == view.GroupKey &&
            basic.Panes[0][0].View.GroupAscending == view.GroupAscending &&
            basic.Panes[0][0].View.SortColumns.SequenceEqual(view.SortColumns) &&
            basic.Panes[0][0].View.DisplayColumns?.SequenceEqual(view.DisplayColumns!) == true,
            "native base fields preserve complete view settings after removing our extension");
        Check(!basic.Panes[0][0].QuickFilter.HasPattern && !basic.Panes[0][1].QuickFilter.HasPattern,
            "Q-Dir base-only import does not invent Explorer Dirs quick-filter settings");
        Check(text.Contains("\r\nColsAndSort1_0=") && !text.Contains("\r\nColsAndSort1="),
            "native view fields follow the selected-tab suffix and omit uncaptured column layouts");
        var changedViewText = text.Replace("\r\nColsAndSort1_0=" + QdrViewWriter.Write(view),
            "\r\nColsAndSort1_0=" + QdrViewWriter.Write(view with { IconSize = 64 }));
        var changedView = QdrImporter.Parse(changedViewText, (value, _) => value);
        Check(changedView.ExtendedState is null && changedView.Panes[0][0].View.IconSize == 64 &&
            changedView.Warnings.Any(w => w.Contains("扩展损坏")),
            "external native view edits invalidate the old extension and remain authoritative");
        var singleText = QdrExporter.Render(state with { Layout = 1, ActivePane = 0 }, pins);
        Check(singleText.Split('\n').Contains("ViewMode=11\r"),
            "single-pane base export uses the explicit official ID, not only our extension");
        var singleBasic = QdrImporter.Parse(string.Join('\n', singleText.Split('\n').Where(line =>
            !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase))), (value, _) => value);
        Check(singleBasic.ExtendedState is null && singleBasic.Layout == 1 && singleBasic.TabCount == 5 &&
            singleBasic.Panes[3][0].Path == @"C:\hidden", "single-pane base preserves hidden pane data");
        var changedLayout = QdrImporter.Parse(singleText.Replace("\r\nViewMode=11\r\n", "\r\nViewMode=0\r\n"), (v, _) => v);
        Check(changedLayout.ExtendedState is null && changedLayout.Layout == 4,
            "external base layout changes invalidate stale extension layout");
        foreach (var damaged in new[]
        {
            text.Replace("ExplorerDirs.FormatVersion=1", "ExplorerDirs.FormatVersion=2"),
            text.Replace("ExplorerDirs.State=", "ExplorerDirs.State=%%%INVALID"),
            text + "ExplorerDirs.FormatVersion=1\r\n",
            text.Replace("Dir1=D:\\other", "Dir1=D:\\changed")
        })
        {
            var fallback = QdrImporter.Parse(damaged, (value, _) => value);
            Check(fallback.ExtendedState is null && fallback.Warnings.Any(w => w.Contains("扩展损坏")),
                "future, corrupt, duplicate or stale extensions fall back visibly");
        }
        Check(QdrImporter.Parse(text.Replace("Dir1=D:\\other", "Dir1=D:\\changed"), (v, _) => v)
            .Panes[0][1].Path == @"D:\changed", "external base edits win over stale exported state");
        Reject(() => QdrExporter.Render(state, [new(0, 1, @"D:\other")]), "non-prefix pins");
        var invalid = state with { Panes = state.Panes.Select(pane => pane.ToList()).ToList() };
        invalid.Panes[0][0] = new("C:\\bad\nDir2=C:\\injected", view);
        Reject(() => QdrExporter.Render(invalid, []), "newline injection");
        var full = state with
        {
            Panes = state.Panes.Select(pane => pane.ToList()).ToList(),
            ActiveTabIndices = new[] { 99, 0, 0, 0 }
        };
        full.Panes[0] = Enumerable.Range(0, 100).Select(i => new TabSettings(@"C:\tab" + i, view)).ToList();
        var fullRead = QdrImporter.Parse(QdrExporter.Render(full, []), (value, _) => value);
        Check(fullRead.Panes[0].Count == 100 && fullRead.SelectedTabs[0] == 99, "100-tab boundary keeps active slot");
        full.Panes[0].Add(new(@"C:\overflow", view));
        Reject(() => QdrExporter.Render(full, []), "over-limit tabs");

        var file = Path.Combine(root, "export.qdr");
        if (File.Exists(file)) File.Delete(file);
        QdrExporter.Save(file, null, text);
        var bytes = File.ReadAllBytes(file);
        Check(bytes[0] == 0xff && bytes[1] == 0xfe && QdrImporter.Load(file).ExtendedState is not null,
            "UTF16 BOM export loads through the real file decoder");
        File.WriteAllText(file, "external edit", Encoding.Unicode);
        try { QdrExporter.Save(file, text, text); throw new Exception("concurrent change overwritten"); }
        catch (IOException) { Check(File.ReadAllText(file) == "external edit", "conflict preserves original target"); }
        QdrExporter.Save(file, "external edit", text);
        Check(File.ReadAllText(file) == text, "explicit matching overwrite succeeds");
        var newlyCreated = Path.Combine(root, "new-target-race.qdr");
        File.WriteAllText(newlyCreated, "", Encoding.Unicode);
        try { QdrExporter.Save(newlyCreated, null, text); throw new Exception("new external target overwritten"); }
        catch (IOException)
        {
            Check(File.Exists(newlyCreated) && File.ReadAllText(newlyCreated) == "",
                "an externally created empty target conflicts with an expected missing file");
        }
        var removed = Path.Combine(root, "removed-target-race.qdr");
        QdrExporter.Save(removed, null, text);
        File.Delete(removed); // This test's newly created fixture only.
        try { QdrExporter.Save(removed, text, text); throw new Exception("removed target silently recreated"); }
        catch (IOException) { Check(!File.Exists(removed), "external deletion is preserved"); }
        Reject(() => QdrExporter.Save(Path.Combine(root, "not-qdr.ini"), null, text), "wrong export extension");
    }

    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("QDR export accepted " + label);
    }
    private static void Check(bool valid, string label)
    {
        if (!valid) throw new InvalidOperationException("QDR export: " + label);
    }

    private static bool SameFilter(QuickFilterSettings left, QuickFilterSettings right) =>
        left.Pattern == right.Pattern && left.ExcludeMatches == right.ExcludeMatches &&
        left.FilterFolders == right.FilterFolders && left.ColumnFilters.Count == right.ColumnFilters.Count &&
        left.ColumnFilters.Zip(right.ColumnFilters).All(pair => pair.First.Key == pair.Second.Key &&
            pair.First.Values.SequenceEqual(pair.Second.Values));

    private static bool SameColorMetadata(QdrColorMetadata? left, QdrColorMetadata? right) =>
        left is null ? right is null : right is not null && left.UseColor == right.UseColor &&
        left.UseTreeColor == right.UseTreeColor && left.ColorTheme == right.ColorTheme &&
        left.Filter2Rows.SequenceEqual(right.Filter2Rows);

    private static bool SameOpaqueMetadata(QdrOpaqueMetadata? left, QdrOpaqueMetadata? right) =>
        left is null ? right is null : right is not null &&
        left.RootFields.SequenceEqual(right.RootFields) && left.Sections.Count == right.Sections.Count &&
        left.Sections.Zip(right.Sections).All(pair => pair.First.Name == pair.Second.Name &&
            pair.First.Rows.SequenceEqual(pair.Second.Rows));
}
