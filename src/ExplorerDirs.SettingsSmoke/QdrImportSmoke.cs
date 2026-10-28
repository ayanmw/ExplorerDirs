using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.App;

internal static class QdrImportSmoke
{
    public static void Run(string root)
    {
        // The 8 MB input bound must also keep line parsing from allocating a second object per newline.
        var blankLines = new string('\n', 1_000_000);
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            QdrImporter.Parse(blankLines, (value, _) => value);
            throw new Exception("QDR without a version marker was accepted");
        }
        catch (InvalidDataException)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Require(allocated < 2_000_000,
                $"QDR line scanning avoids allocating a split array for newline-heavy input (allocated {allocated} bytes)");
            Console.WriteLine($"Q-Dir parser newline scan allocation: {allocated} bytes for 1,000,000 LF characters");
        }

        var plain = "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\current\nDir1_0=C:\\first\nDir1_2=C:\\third\n" +
            "ViewMode1=3\nCurPos1_2=0\nCurPos1_0=2\nDir2=D:\\名字 & 目录\nColsAndSort1=unknown";
        var imported = QdrImporter.Parse(plain, (value, _) => value);
        Require(imported.TabCount == 4 && imported.Layout == 4 && imported.Panes[0][0].Path == @"C:\third" &&
            imported.Panes[0][1].Path == @"C:\current" && imported.SelectedTabs[0] == 1 &&
            imported.Panes[0][1].View.Mode == 3 && imported.Panes[1][0].Path == @"D:\名字 & 目录",
            "QDR preserves pane mapping, active tab gap, reordered tabs, Unicode and ampersand paths");
        Require(imported.Warnings.Any(warning => warning.Contains("排序")), "private view blobs are reported as unsupported");
        foreach (var locations in new[]
        {
            "Dir1=C:\\text-path\nFolder1=%0600FF00\n",
            "Folder1=%0600FF00\nDir1=C:\\text-path\n"
        })
        {
            var resolverCalls = 0;
            var conflictingLocations = QdrImporter.Parse(
                "Q-Dir Version=12.79\nViewMode=0\n" + locations + "Dir2=D:\\keep\n",
                (value, encoded) =>
                {
                    resolverCalls++;
                    return encoded ? @"C:\encoded-pidl" : value;
                });
            Require(conflictingLocations.TabCount == 1 &&
                conflictingLocations.Panes[1][0].Path == @"D:\keep" && resolverCalls == 1 &&
                conflictingLocations.Warnings.Any(warning => warning.Contains("同时包含") && warning.Contains("已跳过")),
                "ambiguous Dir/Folder aliases skip only the conflicting QDR location regardless of field order");
        }
        var numberedAliasConflict = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\current\nDir1_0=C:\\ambiguous\n" +
            "Folder1_0=%0600FF00\nDir1_1=C:\\keep\nCurPos1=0\nCurPos1_1=1\n",
            (value, encoded) => encoded ? @"C:\encoded-pidl" : value);
        Require(numberedAliasConflict.Panes[0].Count == 2 &&
            numberedAliasConflict.Panes[0][0].Path == @"C:\current" &&
            numberedAliasConflict.Panes[0][1].Path == @"C:\keep" &&
            numberedAliasConflict.SelectedTabs[0] == 0,
            "a numbered Dir/Folder collision is skipped without reusing its index for the current tab");
        var launchState = QdrLaunchSession.Create(imported, @"C:\Users\marvin");
        Require(launchState.Layout == 4 && launchState.Panes[0].Count == 3 &&
                launchState.Panes[1][0].Path == imported.Panes[1][0].Path &&
                launchState.Panes.Skip(2).All(pane => pane.Count == 1 && pane[0].Path == @"C:\Users\marvin") &&
                launchState.ActiveTabIndices![0] == imported.SelectedTabs[0],
            "a launchable Q-Dir session restores imported panes and fills empty panes with the normal home tab");
        var singlePaneRoundTripPath = Path.Combine(root, "single-pane-roundtrip.qdr");
        File.WriteAllText(singlePaneRoundTripPath,
            $"Q-Dir Version=12.79\nViewMode=11\nDir1={root}\nFocus=0\n");
        QdrRoundTripProbe.Run(singlePaneRoundTripPath);
        var unknownFields = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\safe\nViewModeFlags1=7\nVendorPolicy=private-path\n",
            (value, _) => value);
        Require(unknownFields.Warnings.Any(warning => warning.Contains("ViewModeFlags") && warning.Contains("VendorPolicy")) &&
            unknownFields.Warnings.All(warning => !warning.Contains("private-path")),
            "QDR preview names unrecognized fields without echoing their values");
        var unicodeFieldCase = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nUNICODE=1\nunicode=0\nDir1=C:\\left\n", (value, _) => value);
        Require(!unicodeFieldCase.Warnings.Any(warning => warning.Contains("重复字段")),
            "QDR treats case variants of the exempt Unicode marker as the same field");
        var qdrThumbnailView = QdrImporter.Parse(plain.Replace("ViewMode1=3", "ViewMode1=5"), (value, _) => value);
        Require(qdrThumbnailView.Panes[0][1].View.Mode == 1 && qdrThumbnailView.Panes[0][1].View.IconSize == 96,
            "Q-Dir Thumbnail mode maps to Shell Large icons when the native view cannot retain FVM_THUMBNAIL");
        Require(qdrThumbnailView.Warnings.Any(warning => warning.Contains("5") && warning.Contains("大图标")),
            "Q-Dir Thumbnail import preview explains the Shell-compatible fallback");
        var qdrThumbstripView = QdrImporter.Parse(plain.Replace("ViewMode1=3", "ViewMode1=7"), (value, _) => value);
        Require(qdrThumbstripView.Panes[0][1].View.Mode == 6,
            "Q-Dir Thumbstrip mode maps to Shell Tiles when the native view cannot retain FVM_THUMBSTRIP");
        Require(qdrThumbstripView.Warnings.Any(warning => warning.Contains("7") && warning.Contains("平铺")),
            "Q-Dir Thumbstrip import preview explains the Shell-compatible fallback");
        var contentQdrView = QdrImporter.Parse(plain.Replace("ViewMode1=3", "ViewMode1=8"), (value, _) => value);
        Require(contentQdrView.Panes[0][1].View.Mode == 4 &&
                contentQdrView.Warnings.Any(warning => warning.Contains("查看方式")),
            "Q-Dir per-tab mode 8 exceeds its published FVM_LAST and falls back to Details with a warning");
        var viewStream = QdrViewWriter.Write(ViewState.Default() with
        {
            Mode = 6,
            DisplayColumns = [new(new PropertyKeyState(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10), 220)]
        })!;
        var contentWithViewStream = QdrImporter.Parse(
            plain.Replace("ViewMode1=3", "ViewMode1=8").Replace("ColsAndSort1=unknown", "ColsAndSort1=" + viewStream),
            (value, _) => value);
        Require(contentWithViewStream.Panes[0][1].View.Mode == 4,
            "an unsupported explicit Q-Dir mode overrides a different mode in the view-state stream");
        var unknownQdrView = QdrImporter.Parse(plain.Replace("ViewMode1=3", "ViewMode1=9"), (value, _) => value);
        Require(unknownQdrView.Panes[0][1].View.Mode == 4 &&
                unknownQdrView.Warnings.Any(warning => warning.Contains("查看方式")),
            "Q-Dir view mode values beyond FVM_LAST fall back to Details and warn");
        var previewEnabled = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\left\nDir1_1=C:\\left\\child\nDir2=D:\\right\nVorschau=1\n",
            (value, _) => value);
        Require(previewEnabled.Panes.SelectMany(pane => pane).All(tab => tab.PreviewPaneVisible),
            "Q-Dir's global preview flag is applied to every imported tab");
        Require(!imported.Panes.SelectMany(pane => pane).Any(tab => tab.PreviewPaneVisible),
            "a missing Q-Dir preview flag keeps imported tabs' preview panes hidden");
        var qdrColorFlags = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\nuseColor=1\nuseTreeColor=1\nDir1=C:\\left\n",
            (value, _) => value);
        Require(qdrColorFlags.Warnings.Any(warning => warning.Contains("useColor") && warning.Contains("已启用") && warning.Contains("未应用")) &&
                qdrColorFlags.Warnings.Any(warning => warning.Contains("useTreeColor") && warning.Contains("已启用") && warning.Contains("未应用")),
            "enabled Q-Dir color switches are each reported as unapplied until the color-rule payload format is confirmed");
        var qdrColorDisabled = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\nuseColor=0\nuseTreeColor=0\nDir1=C:\\left\n",
            (value, _) => value);
        Require(!qdrColorDisabled.Warnings.Any(warning => warning.Contains("useColor") || warning.Contains("useTreeColor")) &&
                qdrColorDisabled.Warnings.Any(warning => warning.Contains("颜色规则") && warning.Contains("背景值")),
            "explicitly disabled Q-Dir color switches do not imply unapplied active settings, while unsupported payloads remain disclosed");
        var qdrColorInvalid = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\nuseColor=maybe\nDir1=C:\\left\n",
            (value, _) => value);
        Require(qdrColorInvalid.Warnings.Any(warning => warning.Contains("useColor") && warning.Contains("无法识别")) &&
                qdrColorInvalid.Warnings.All(warning => !warning.Contains("maybe")),
            "invalid Q-Dir color switch values are reported without echoing the raw value");
        var qdrColorRules = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\nuseColor=1\nDir1=C:\\left\n" +
            "[Filter2]\n0=*.jpg=1=FF0000=-1=1=-1=0\n1=#BG-A=1=-1=-1=1=-1=0\n",
            (value, _) => value);
        Require(qdrColorRules.Warnings.Any(warning => warning.Contains("Filter2") && warning.Contains("2") &&
                    warning.Contains("颜色规则") && warning.Contains("未导入") &&
                    warning.Contains("*.jpg") && warning.Contains("#BG-A")) &&
                qdrColorRules.Warnings.All(warning => !warning.Contains("FF0000")) &&
                !qdrColorRules.Warnings.Any(warning => warning.Contains("未识别且未应用的字段名")),
            "Q-Dir import preview lists Filter2 rule patterns without interpreting or exposing color values");
        var longFilterPattern = new string('x', 70);
        var manyFilterRules = string.Join("\n", Enumerable.Range(0, 8).Select(index =>
            $"{index}={(index == 0 ? longFilterPattern : $"pattern-{index}")}=1=00AA00=-1=1=-1=0"));
        var boundedColorWarning = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\nDir1=C:\\left\n[Filter2]\n" + manyFilterRules,
            (value, _) => value).Warnings.Single(warning => warning.Contains("Filter2"));
        Require(boundedColorWarning.Length < 400 && boundedColorWarning.Contains(new string('x', 61) + "…") &&
                boundedColorWarning.Contains("pattern-5") && !boundedColorWarning.Contains("pattern-6") &&
                boundedColorWarning.EndsWith("等", StringComparison.Ordinal),
            "Q-Dir Filter2 preview bounds long patterns and the number of listed rules");
        var previewDisabled = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\left\nVorschau=0\n", (value, _) => value);
        Require(!previewDisabled.Panes[0][0].PreviewPaneVisible &&
            !previewDisabled.Warnings.Any(warning => warning.Contains("Vorschau")),
            "Q-Dir's explicit disabled preview flag imports without warning");
        var invalidPreview = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\left\nVorschau=2\n", (value, _) => value);
        Require(!invalidPreview.Panes[0][0].PreviewPaneVisible &&
            invalidPreview.Warnings.Any(warning => warning.Contains("Vorschau")),
            "an unsupported Q-Dir preview value falls back to hidden and warns");
        var invalidPath = QdrImporter.Parse(plain, (value, _) => value == @"C:\first" ? null : value);
        Require(invalidPath.Panes[0][invalidPath.SelectedTabs[0]].Path == @"C:\current", "skipping an invalid tab does not move the original active slot");
        Require(QdrImporter.Parse(plain.Replace("ViewMode=0", "ViewMode=99"), (v, _) => v).Layout is null,
            "unknown QDR layouts do not silently become another layout");
        var shellLocation = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=shell:recyclebinfolder\n",
            (value, _) => ShellLocationIdentity.TryNormalize(value, out var identity, out _) ? identity : null);
        Require(shellLocation.TabCount == 1 && shellLocation.Panes[0][0].Path == "shell:RecycleBinFolder",
            "supported Q-Dir Shell names normalize and remain in the imported tab");
        Require(QdrImporter.ResolveLocation("shell:RecycleBinFolder", encoded: false) == "shell:RecycleBinFolder",
            "native Q-Dir resolution preserves a supported Shell folder identity");
        const string qdrThisPc = "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        var thisPcLocation = QdrImporter.Parse(
            $"Q-Dir Version=12.79\nViewMode=0\nDir1={qdrThisPc}\n",
            QdrImporter.ResolveLocation);
        Require(thisPcLocation.TabCount == 1 && thisPcLocation.Panes[0][0].Path == "shell:MyComputerFolder",
            "Q-Dir shell:::{GUID} absolute names resolve to their confirmed Shell folder");
        var otherVirtualFolder = QdrImporter.ResolveLocation(
            "::{26EE0668-A00A-44D7-9371-BEB064C98683}", encoded: false);
        Require(otherVirtualFolder is not null && otherVirtualFolder.StartsWith("::", StringComparison.Ordinal),
            "a resolvable Shell absolute name outside the built-in aliases is preserved");
        var missingShellFallbackPath = root;
        var missingShellLocation = QdrImporter.Parse(
            $"Q-Dir Version=12.79\nViewMode=0\nDir1=shell:::{{00000000-0000-0000-0000-000000000000}}\nDir2={missingShellFallbackPath}\n",
            QdrImporter.ResolveLocation);
        Require(missingShellLocation.TabCount == 1 &&
            missingShellLocation.Panes.SelectMany(pane => pane).Single().Path == missingShellFallbackPath &&
            missingShellLocation.Warnings.Any(warning => warning.Contains("可用文件夹")),
            "an unresolved Shell absolute name skips only its tab and warns");
        var unsupportedLocation = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nDir1=https://example.invalid\nDir2=C:\\keep\n",
            (value, _) => ShellLocationIdentity.TryNormalize(value, out var identity, out _) ? identity : null);
        Require(unsupportedLocation.TabCount == 1 &&
            unsupportedLocation.Panes.SelectMany(pane => pane).Single().Path == @"C:\keep" &&
            unsupportedLocation.Warnings.Any(warning => warning.Contains("可用文件夹")),
            "unsupported virtual Q-Dir locations warn and skip without dropping valid tabs");
        try
        {
            QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=0\nDir1=https://example.invalid\n",
                QdrImporter.ResolveLocation);
            throw new Exception("invalid-only Q-Dir import accepted");
        }
        catch (InvalidDataException ex)
        {
            Require(ex.Message.Contains("可导入的文件夹", StringComparison.Ordinal) &&
                !ex.Message.Contains("文件系统目录", StringComparison.Ordinal),
                "empty Q-Dir imports describe Shell and filesystem folders consistently");
        }
        var single = QdrImporter.Parse(plain.Replace("ViewMode=0", "ViewMode=11"), (v, _) => v);
        Require(single.Layout == 1 && single.TabCount == imported.TabCount &&
            single.Panes[1][0].Path == imported.Panes[1][0].Path,
            "official single-pane layout preserves hidden panes and all tabs");
        Require(!single.Warnings.Any(w => w.Contains("此布局")), "documented single-pane layout is recognized");
        var importedTree = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\nTree1=1\nTree2=0\nTree3=1\nTree4=0\nDir1=C:\\left\n",
            (value, _) => value);
        Require(importedTree.PaneTreeVisibility?.SequenceEqual([true, true, false, false]) == true,
            "native Q-Dir per-pane tree flags map through the known layout pane order");
        Require(importedTree.Warnings.Any(warning => warning.Contains("Tree1～Tree4") &&
                warning.Contains("尚未通过 Q-Dir 可见互操作确认")),
            "the tentative Q-Dir tree-flag interpretation is disclosed in the import preview");
        var unsupportedTreeSettings = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\ntree_mode=2\nTree1=1\nTree2=1\nTree3=1\nTree4=1\n" +
            "Tree1split=2500\nTree2split=2500\nTree3split=2500\nTree4split=2500\nDir1=C:\\left\n",
            (value, _) => value);
        Require(unsupportedTreeSettings.TreeDisplayMode == TreeDisplayMode.PerPane &&
                !unsupportedTreeSettings.Warnings.Any(warning => warning.Contains("tree_mode")) &&
                unsupportedTreeSettings.Warnings.Any(warning => warning.Contains("Tree1split")),
            "Q-Dir Each has one mode maps to per-pane trees while unknown split widths stay warned");
        var disabledTreeSplits = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\ntree_mode=2\nTree1=0\nTree2=0\nTree3=0\nTree4=0\n" +
            "Tree1split=2500\nTree2split=2500\nTree3split=2500\nTree4split=2500\nDir1=C:\\left\n",
            (value, _) => value);
        Require(disabledTreeSplits.TreeDisplayMode == TreeDisplayMode.PerPane &&
                disabledTreeSplits.PaneTreeVisibility?.All(visible => !visible) == true &&
                !disabledTreeSplits.Warnings.Any(warning => warning.Contains("Tree1split")),
            "Q-Dir split defaults do not warn when every imported pane tree is disabled");
        var treeModeMappings = new (int Native, TreeDisplayMode App)[]
        {
            (0, TreeDisplayMode.None), (1, TreeDisplayMode.Shared), (2, TreeDisplayMode.PerPane)
        };
        foreach (var mapping in treeModeMappings)
        {
            var result = QdrImporter.Parse(
                $"Q-Dir Version=12.79\nViewMode=4\ntree_mode={mapping.Native}\nDir1=C:\\left\n",
                (value, _) => value);
            Require(result.TreeDisplayMode == mapping.App,
                $"Q-Dir tree_mode={mapping.Native} maps to {mapping.App}");
        }
        var invalidTreeMode = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\ntree_mode=3\nDir1=C:\\left\n", (value, _) => value);
        Require(invalidTreeMode.TreeDisplayMode is null && invalidTreeMode.Warnings.Any(w => w.Contains("tree_mode")),
            "unknown Q-Dir tree_mode values preserve the current application tree mode and warn");
        var documentedTreeExample = QdrImporter.Parse(
            "Q-Dir Version=7.84\nViewMode=0\ntree_mode=0\nTree1=0\nTree2=0\nTree3=0\nTree4=0\n" +
            "Tree1split=2500\nTree2split=2500\nTree3split=2500\nTree4split=2500\n" +
            "SPT=2000\nVorschauPos=7500\nDir1=C:\\left\n",
            (value, _) => value);
        Require(documentedTreeExample.TreeDisplayMode == TreeDisplayMode.None &&
                !documentedTreeExample.Warnings.Any(warning => warning.Contains("Tree1split")) &&
                documentedTreeExample.Warnings.Any(warning => warning.Contains("Tree1～Tree4")) &&
                documentedTreeExample.Warnings.Any(warning => warning.Contains("SPT")) &&
                documentedTreeExample.Warnings.Any(warning => warning.Contains("VorschauPos")) &&
                !documentedTreeExample.Warnings.Any(warning => warning.Contains("tree_mode")),
            "the official Q-Dir example does not warn about ignored tree widths when tree mode is explicitly disabled");
        var activeTreeSplit = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\ntree_mode=2\nTree1split=2500\nDir1=C:\\left\n",
            (value, _) => value);
        Require(activeTreeSplit.Warnings.Any(warning => warning.Contains("Tree1split")),
            "Q-Dir tree widths remain warned when an enabled tree mode may consume them");
        var unrecognizedTreeModeSplit = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=4\ntree_mode=bad\nTree1split=2500\nDir1=C:\\left\n",
            (value, _) => value);
        Require(unrecognizedTreeModeSplit.Warnings.Any(warning => warning.Contains("Tree1split")),
            "Q-Dir tree widths remain warned when the tree mode is invalid");
        var malformedTree = QdrImporter.Parse(
            "Q-Dir Version=12.79\nViewMode=0\nTree1=1\nTree2=bad\nDir1=C:\\left\n", (value, _) => value);
        Require(malformedTree.PaneTreeVisibility is null && malformedTree.Warnings.Any(w => w.Contains("目录树")),
            "partial or invalid Q-Dir tree flags preserve the current pane state and warn");
        foreach (var unconfirmed in new[] { 12, 99 })
            Require(QdrImporter.Parse(plain.Replace("ViewMode=0", "ViewMode=" + unconfirmed), (v, _) => v).Layout is null,
                "layouts without an application geometry are not silently approximated");
        foreach (var bad in new[] { "%00%00", "%FF%FF%00%00", "%02%00", "%03%00%00%00", "%GG%00%00", "%02%00%00%00%00" })
        {
            try { QdrImporter.DecodePidl(bad); throw new Exception("invalid PIDL accepted"); }
            catch (FormatException) { }
        }
        try { QdrImporter.Parse("Dir1=C:\\", (v, _) => v); throw new Exception("foreign file accepted"); }
        catch (InvalidDataException) { }
        var qdrPath = Path.Combine(root, "import.qdr");
        var loadable = $"Q-Dir Version=12.79\nViewMode=0\nDir1={root}\n";
        File.WriteAllText(qdrPath, loadable, Encoding.Unicode);
        Require(QdrImporter.Load(qdrPath).TabCount == 1, "UTF16 QDR import of an existing directory");
        File.WriteAllText(qdrPath, loadable, new UTF8Encoding(true));
        Require(QdrImporter.Load(qdrPath).TabCount == 1, "UTF8 BOM QDR import of an existing directory");
        var literalVariable = "ExplorerDirsQdrLiteral_" + Guid.NewGuid().ToString("N");
        var literalFolder = Path.Combine(root, "%" + literalVariable + "%");
        var replacementFolder = Path.Combine(root, "expanded-target");
        Directory.CreateDirectory(literalFolder);
        Directory.CreateDirectory(replacementFolder);
        Environment.SetEnvironmentVariable(literalVariable, "expanded-target");
        try
        {
            var expandedResult = QdrImporter.Parse($"Q-Dir Version=12.79\nDir1={literalFolder}\n",
                QdrImporter.ResolveLocation);
            Require(expandedResult.Panes[0][0].Path.Equals(replacementFolder, StringComparison.OrdinalIgnoreCase),
                "plain QDR text paths intentionally expand environment variables");
            nint literalPidl = 0;
            try
            {
                // Encoded PIDLs carry a filesystem identity; percent signs here are not variable syntax.
                Marshal.ThrowExceptionForHR(SHParseDisplayName(literalFolder, 0, out literalPidl, 0, out _));
                var literalBytes = new byte[ILGetSize(literalPidl)];
                Marshal.Copy(literalPidl, literalBytes, 0, literalBytes.Length);
                var literalEncoded = string.Concat(literalBytes.Select(value => "%" + value.ToString("X2")));
                var literalResult = QdrImporter.Parse($"Q-Dir Version=12.79\nFolder1={literalEncoded}\n",
                    QdrImporter.ResolveLocation);
                Require(literalResult.Panes[0][0].Path.Equals(literalFolder, StringComparison.OrdinalIgnoreCase),
                    "QDR PIDLs preserve percent-named folder identity despite a matching environment variable");
                var literalState = new SessionState(4, 4,
                    Enumerable.Range(0, 4).Select(_ => new List<TabSettings>
                        { new(literalFolder, ViewState.Default()) }).ToList(), 0, [0, 0, 0, 0]);
                var encodedExport = QdrExporter.Render(literalState, [], encodedLocations:
                    new Dictionary<string, string> { [literalFolder] = literalEncoded });
                var baseExport = string.Join("\n", encodedExport.Split('\n').Where(line =>
                    !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
                var baseRoundTrip = QdrImporter.Parse(baseExport, QdrImporter.ResolveLocation);
                Require(baseRoundTrip.TabCount == 4 && baseRoundTrip.Panes.All(pane =>
                        pane[0].Path.Equals(literalFolder, StringComparison.OrdinalIgnoreCase)),
                    "encoded export preserves literal paths in all four base panes without the app extension");
                var exactRoundTrip = QdrImporter.Parse(encodedExport, QdrImporter.ResolveLocation);
                Require(exactRoundTrip.ExtendedState is not null,
                    "encoded base paths participate in the extension hash and retain exact app state");
                var prepared = Task.Run(() => QdrExporter.Prepare(literalState, [])).GetAwaiter().GetResult();
                var preparedBase = string.Join("\n", prepared.Text.Split('\n').Where(line =>
                    !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
                Require(prepared.UnencodedLocationCount == 0 &&
                    QdrImporter.Parse(preparedBase, QdrImporter.ResolveLocation).Panes.All(pane =>
                        pane[0].Path.Equals(literalFolder, StringComparison.OrdinalIgnoreCase)),
                    "background export preparation captures literal PIDLs without environment expansion");
                var offlinePath = Path.Combine(root, "%MissingQdrLocation_" + Guid.NewGuid().ToString("N") + "%");
                var offlineState = literalState with { Panes = Enumerable.Range(0, 4)
                    .Select(_ => new List<TabSettings> { new(offlinePath, ViewState.Default()) }).ToList() };
                var offlinePrepared = Task.Run(() => QdrExporter.Prepare(offlineState, [])).GetAwaiter().GetResult();
                var offlineRoundTrip = QdrImporter.Parse(offlinePrepared.Text, (value, _) => value);
                Require(offlinePrepared.UnencodedLocationCount == 1 && offlineRoundTrip.ExtendedState is not null &&
                    offlineRoundTrip.TabCount == 4 && offlineRoundTrip.Panes.All(pane => pane[0].Path == offlinePath),
                    "unavailable percent paths retain every tab and report one distinct unresolved identity");                var literalBackup = Path.Combine(root, "literal-backup.bak");
                var literalBackupWarnings = Task.Run(() => QdrExporter.SaveBackup(literalBackup,
                    literalState, [], new Dictionary<int, LayoutProportions> { [4] = new(0.5, 0.5) },
                    NavigationTreeWidths.Defaults)).GetAwaiter().GetResult();
                var literalBackupText = QdrImporter.ReadText(literalBackup + ".qdr");
                var literalBackupBase = string.Join("\n", literalBackupText.Split('\n').Where(line =>
                    !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
                Require(literalBackupWarnings == 0 && File.ReadAllText(literalBackup) == SessionStore.Render(literalState) &&
                    QdrImporter.Parse(literalBackupBase, QdrImporter.ResolveLocation).Panes.All(pane =>
                        pane[0].Path.Equals(literalFolder, StringComparison.OrdinalIgnoreCase)),
                    "background backup retains literal percent identities in its interoperable base fields");                var malformedRejected = false;
                try { QdrExporter.Render(literalState, [], encodedLocations:
                    new Dictionary<string, string> { [literalFolder] = "%01" }); }
                catch (FormatException) { malformedRejected = true; }
                Require(malformedRejected, "invalid pre-resolved PIDLs cannot be emitted as valid export fields");
            }
            finally { if (literalPidl != 0) Marshal.FreeCoTaskMem(literalPidl); }
        }
        finally { Environment.SetEnvironmentVariable(literalVariable, null); }
        var pidl = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(root, IntPtr.Zero, out pidl, 0, out _));
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            var encoded = string.Concat(bytes.Select(value => "%" + value.ToString("X2")));
            var pidlQdr = $"Q-Dir Version=12.79\nViewMode=0\nFolder1={encoded}&ViewMode1=4&UNICODE=1";
            var resolved = QdrImporter.Parse(pidlQdr, QdrImporter.ResolveLocation);
            Require(resolved.TabCount == 1 && resolved.Panes[0][0].Path.Equals(root, StringComparison.OrdinalIgnoreCase),
                "native Shell PIDL resolves back to its filesystem directory");
        }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }

    private static void Require(bool valid, string message)
    { if (!valid) throw new Exception("Failed: " + message); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
}
