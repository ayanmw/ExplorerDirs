using ExplorerDirs.App;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

if (args.Length >= 2 && args[0] == "--program-starter-argv-probe")
{
    var resultPath = args[1];
    var temporaryPath = resultPath + ".tmp";
    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(args.Skip(2)));
    File.Move(temporaryPath, resultPath, overwrite: true);
    return;
}

if (args.Length == 1 && args[0] == "--recursive-directory-scanner-smoke")
{
    ExplorerDirs.SettingsSmoke.RecursiveDirectoryScannerSmoke.Run();
    Console.WriteLine("Recursive directory scanner smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-watcher-recovery-smoke")
{
    ShellDirectoryWatcherRecoverySmoke.Run();
    Console.WriteLine("Shell directory watcher recovery smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-selection-restore-gate-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellSelectionRestoreGateSmoke.Run();
    Console.WriteLine("Shell selection restore gate smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-rename-window-gate-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellRenameWindowGateSmoke.Run();
    Console.WriteLine("Shell rename window gate smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--full-row-selection-smoke")
{
    var smokeRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.FullRowSelectionSmoke.{Guid.NewGuid():N}");
    Directory.CreateDirectory(smokeRoot);
    try
    {
        ExplorerDirs.SettingsSmoke.FolderHeaderFlagsSmoke.Run();
        ExplorerDirs.SettingsSmoke.StatusBarVisibilitySmoke.Run(smokeRoot);
        ExplorerDirs.SettingsSmoke.FullRowSelectionRoutingSmoke.Run();
    }
    finally { Directory.Delete(smokeRoot, recursive: true); }
    Console.WriteLine("Full-row selection smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--pure-state-smoke")
{
    DirectoryMonitorStartupSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellLocationIdentitySmoke.RunShellNameSyntaxOnly();
    Console.WriteLine("Pure state smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--theme-palette-smoke")
{
    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.CheckResolution();
    return;
}

if (args.Length == 1 && args[0] == "--tab-strip-visibility-smoke")
{
    var smokeRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.TabStripSmoke.{Guid.NewGuid():N}");
    Directory.CreateDirectory(smokeRoot);
    try { ExplorerDirs.SettingsSmoke.TabStripVisibilitySmoke.Run(smokeRoot); }
    finally { Directory.Delete(smokeRoot, recursive: true); }
    Console.WriteLine("Tab strip visibility smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--active-pane-theme-smoke")
{
    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.CheckActivePaneAccentWiring();
    return;
}

if (args.Length == 1 && args[0] == "--semantic-theme-brush-smoke")
{
    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.CheckSemanticBrushRoles();
    return;
}

if (args.Length == 1 && args[0] == "--view-style-icon-smoke")
{
    ExplorerDirs.SettingsSmoke.ViewStyleIconSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--explorer-chrome-icon-smoke")
{
    ExplorerDirs.SettingsSmoke.ExplorerChromeIconSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--native-shell-tree-icon-fallback-smoke")
{
    ExplorerDirs.SettingsSmoke.NativeShellTreeIconFallbackSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--shell-icon-provider-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellIconProviderSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--address-path-menu-smoke")
{
    AddressPathMenuSmoke.Run();
    Console.WriteLine("Address path menu smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--address-path-history-smoke")
{
    AddressPathHistorySmoke.Run();
    Console.WriteLine("Address path history smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--file-view-export-smoke")
{
    FileViewExportSmoke.Run();
    Console.WriteLine("File view export smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--focus-traversal-smoke")
{
    FocusTraversalSmoke.Run();
    Console.WriteLine("Focus traversal smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--mouse-wheel-focus-smoke")
{
    ExplorerDirs.SettingsSmoke.MouseWheelFocusSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--directory-watcher-startup-smoke")
{
    ExplorerDirs.SettingsSmoke.DirectoryWatcherStartupSmoke.RunAsync().GetAwaiter().GetResult();
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-store-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterStoreSmoke.Run(
        Path.Combine(AppContext.BaseDirectory, "program-starter-store-smoke"));
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-arguments-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterArgumentsSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--qdr-program-import-smoke")
{
    ExplorerDirs.SettingsSmoke.QdrProgramImporterSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-launcher-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterLauncherSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-shortcut-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterShortcutSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-command-state-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterCommandStateSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--program-starter-entry-commit-smoke")
{
    ExplorerDirs.SettingsSmoke.ProgramStarterEntryCommitSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--photo-print-window-markup-smoke")
{
    ExplorerDirs.SettingsSmoke.PhotoPrintWindowMarkupSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--pane-theme-markup-smoke")
{
    PaneThemeMarkupSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--window-frame-theme-smoke")
{
    WindowFrameThemeSmoke.Run();
    return;
}

if (args.Length == 2 && args[0] == "--roundtrip-qdr")
{
    QdrRoundTripProbe.Run(args[1]);
    return;
}

if (args.Length == 1 && args[0] == "--shell-menu-target-smoke")
{
    ShellMenuTargetTrackerSmoke.Run();
    ShellHeaderPresetShortcutSmoke.Run();
    Console.WriteLine("Shell menu target smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-header-preset-shortcut-smoke")
{
    ShellHeaderPresetShortcutSmoke.Run();
    Console.WriteLine("Shell header preset shortcut smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-status-count-refresh-smoke")
{
    ShellStatusCountRefreshGateSmoke.Run();
    Console.WriteLine("Shell status count refresh smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-changed-item-batch-smoke")
{
    ShellChangedItemNotificationBatchSmoke.Run();
    Console.WriteLine("Shell changed-item notification batch smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-directory-change-batch-smoke")
{
    ShellDirectoryChangeBatchSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellSelectionRecoverySuppressionSmoke.Run();
    Console.WriteLine("Shell directory-change batch smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-refresh-request-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellRefreshRequestSmoke.Run();
    Console.WriteLine("Shell refresh request smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-refresh-completion-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellRefreshCompletionSmoke.Run();
    Console.WriteLine("Shell refresh completion smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--qdr-layout-smoke")
{
    QdrLayoutSmoke.Run();
    Console.WriteLine("Q-Dir layout smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--folder-creation-observation-smoke")
{
    ExplorerDirs.SettingsSmoke.FolderCreationObservationSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--folder-header-flags-smoke")
{
    ExplorerDirs.SettingsSmoke.FolderHeaderFlagsSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--shell-tab-drop-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellTabDropSmoke.Run();
    Console.WriteLine("Shell tab drop smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--tree-width-smoke")
{
    ExplorerDirs.SettingsSmoke.NavigationTreeWidthsSmoke.Run();
    Console.WriteLine("Navigation tree widths smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--tree-selection-auto-expand-smoke")
{
    ExplorerDirs.SettingsSmoke.TreeSelectionAutoExpandSmoke.Run();
    Console.WriteLine("Tree selection auto-expand smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--address-tree-shortcut-smoke")
{
    ExplorerDirs.SettingsSmoke.AddressTreeShortcutSmoke.Run();
    Console.WriteLine("Address tree shortcut smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-tree-location-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellTreeFolderLocationSmoke.Run();
    Console.WriteLine("Shell tree location smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--shell-folder-filter-layout-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellFolderFilterLayoutSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--hidden-items-preference-smoke")
{
    HiddenItemsPreferenceSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--shell-file-commands-smoke")
{
    ShellFileCommandsSmoke.Run();
    ShellCommandRefreshGateSmoke.Run();
    ShellRenameKeyRoutingSmoke.Run();
    Console.WriteLine("Shell file command smoke: PASS");
    return;
}

if (args.Length == 1 && args[0] == "--paste-special-output-smoke")
{
    ExplorerDirs.SettingsSmoke.PasteSpecialOutputSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--created-file-selection-request-smoke")
{
    ExplorerDirs.SettingsSmoke.CreatedFileSelectionRequestSmoke.Run();
    return;
}

if (args.Length == 1 && args[0] == "--environment-change-message-smoke")
{
    ExplorerDirs.SettingsSmoke.EnvironmentChangeMessageSmoke.Run();
    Console.WriteLine("Environment setting-change message smoke: PASS");
    return;
}

ExplorerDirs.SettingsSmoke.EnvironmentChangeMessageSmoke.Run();
ExplorerDirs.SettingsSmoke.ShellTabDropSmoke.Run();
ExplorerDirs.SettingsSmoke.ShellFolderFilterLayoutSmoke.Run();
ExplorerDirs.SettingsSmoke.ViewStyleWheelRoutingSmoke.Run();
ExplorerDirs.SettingsSmoke.FullRowSelectionRoutingSmoke.Run();
ExplorerDirs.SettingsSmoke.ShellRenameWindowGateSmoke.Run();
HiddenItemsPreferenceSmoke.Run();
ExplorerDirs.SettingsSmoke.FolderHeaderFlagsSmoke.Run();
ExplorerDirs.SettingsSmoke.QuickLinkStoreSmoke.Run();
ExplorerDirs.SettingsSmoke.FolderCreationObservationSmoke.Run();
ExplorerDirs.SettingsSmoke.ProgramStarterStoreSmoke.Run(
    Path.Combine(AppContext.BaseDirectory, "program-starter-store-smoke"));
ExplorerDirs.SettingsSmoke.ProgramStarterArgumentsSmoke.Run();
ExplorerDirs.SettingsSmoke.QdrProgramImporterSmoke.Run();
ExplorerDirs.SettingsSmoke.ProgramStarterLauncherSmoke.Run();
ExplorerDirs.SettingsSmoke.ProgramStarterShortcutSmoke.Run();
ExplorerDirs.SettingsSmoke.ProgramStarterCommandStateSmoke.Run();
ExplorerDirs.SettingsSmoke.ProgramStarterEntryCommitSmoke.Run();
var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.SettingsSmoke.{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
ExplorerDirs.SettingsSmoke.TabStripVisibilitySmoke.Run(root);
ExplorerDirs.SettingsSmoke.StatusBarVisibilitySmoke.Run(root);
ExplorerDirs.SettingsSmoke.ViewPresetStoreSmoke.Run(root);
if (args.Length == 1 && args[0] == "--view-preset-store-smoke")
{
    ExplorerDirs.SettingsSmoke.ViewPresetStoreSmoke.Run(root);
    Console.WriteLine("Named view preset store smoke: PASS");
    return;
}
if (args.Length == 1 && args[0] == "--shell-location-qdr-smoke")
{
    ExplorerDirs.SettingsSmoke.ShellLocationIdentitySmoke.Run();
    QdrImportSmoke.Run(root);
    Console.WriteLine("Shell location and Q-Dir import smoke: PASS");
    return;
}
if (args.Length == 1 && args[0] == "--qdr-location-roundtrip-only")
{
    QdrExportSmoke.Run(root);
    Console.WriteLine("Q-Dir Shell location export/import smoke: PASS");
    return;
}
ExplorerDirs.SettingsSmoke.ShellSelectionRestoreGateSmoke.Run();
ExplorerDirs.SettingsSmoke.ShellSelectionBatchRestoreSmoke.Run();
ExplorerDirs.SettingsSmoke.ShellFocusRestoreGateSmoke.Run();
ShellDirectoryRefreshRecoverySmoke.Run();
ExplorerDirs.SettingsSmoke.ShellLocationIdentitySmoke.Run();
ExplorerDirs.SettingsSmoke.ShellSelectionSizeRefreshGateSmoke.Run();
PaneTreeSessionSmoke.Run(root);
FocusTraversalSmoke.Run();
var ini = Path.Combine(root, "ExplorerDirs.ini");
var legacy = Path.Combine(root, "session.json");
var store = new SessionStore(ini, legacy);
var sortKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
var groupKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
var mutableSorts = new List<SortColumnState> { new(sortKey, 1) };
var cachedView = new ViewState(4, 16, mutableSorts, groupKey, true).Copy();
mutableSorts.Clear();
var first = new TabSettings(@"C:\项目=测试", new ViewState(4, 16,
    [new SortColumnState(sortKey, -1)], groupKey, false));
var second = new TabSettings(@"C:\项目=测试", ViewState.Default(6));
var state = new SessionState(2, 3, [[first, second], [], [], []]);
var shellSessionState = state with
{
    Panes = [[new TabSettings("shell:RecycleBinFolder", ViewState.Default())], [], [], []],
    ActiveTabIndices = [0, 0, 0, 0]
};
var shellSessionText = SessionStore.Render(shellSessionState);
var shellSessionRoundTrip = SessionStore.Parse(shellSessionText);
Check(shellSessionRoundTrip?.Panes[0][0].Path == "shell:RecycleBinFolder" &&
      shellSessionText.Contains("FormatVersion=10", StringComparison.Ordinal),
    "supported Shell parsing identities round-trip in the existing session format");

try
{
    ShellItemCountsSmoke.Run();
    ShellDirectoryChangeBatchSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellRefreshRequestSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellRefreshCompletionSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellSelectionRecoverySuppressionSmoke.Run();
    ShellChangedItemNotificationBatchSmoke.Run();
    ShellDirectoryChangeQueueSmoke.Run();
    ShellDirectoryChangeMonitorOptionsSmoke.Run();
    ShellDirectoryWatcherRecoverySmoke.Run();
    ExplorerDirs.SettingsSmoke.DirectoryWatcherStartupSmoke.RunAsync().GetAwaiter().GetResult();
    DirectoryMonitorStartupSmoke.Run();
    ShellCreatedItemNotificationSmoke.Run();
    ShellNewFolderRefreshGateSmoke.Run();
    ShellNewFolderEditGateSmoke.Run();
    ExplorerDirs.SettingsSmoke.CreatedFileSelectionRequestSmoke.Run();
    ExplorerPaneVisibilitySmoke.Run();
    ShellTreeMiddleClickSmoke.Run();
    ShellTreeLocationRouteSmoke.Run();
    ShellMiddleClickTargetSmoke.Run();
    PreviewPaneSessionSmoke.Run(root);
    ShellCreateNotificationBatchSmoke.Run();
    ShellDeleteNotificationBatchSmoke.Run();
    ShellViewReadinessSmoke.Run();
    ShellHistorySmoke.Run();
    AddressPathHistorySmoke.Run();
    AddressPathMenuSmoke.Run();
    DiskSpaceSmoke.Run(root).GetAwaiter().GetResult();
    ShellFileCommandsSmoke.Run();
    ShellCommandRefreshGateSmoke.Run();
    ShellRenameKeyRoutingSmoke.Run();
    NativeShellCommandSmoke.Run();
    NativeColumnSmoke.Run();
    QdrLayoutSmoke.Run();
    QdrImportSmoke.Run(root);
    QdrExportSmoke.Run(root);
    QdrProportionsSmoke.Run();
    DefaultViewSmoke.Run(root);
    if (args.Length == 2 && args[0] == "--verify-older-profile-guard")
        DefaultViewSmoke.VerifyOlderReader(args[1], root);
    if (args.Length == 2 && args[0] == "--verify-older-proportions-guard")
        DefaultViewSmoke.VerifyOlderReader(args[1], root, proportionsOnly: true);
    WorkspaceCatalogSmoke.Run(root);
    ExplorerDirs.SettingsSmoke.WindowSessionRegistrySmoke.Run(root);
    ExplorerDirs.SettingsSmoke.DesktopWorkspaceShortcutSmoke.Run(root);
    QdrViewSmoke.Run();
    ViewStyleWheelSmoke.Run();
    ShellQuickFilterSmoke.Run();
    QuickFilterEnumerationGateSmoke.Run();
    FileViewExportSmoke.Run();
    FileViewPrintSmoke.Run();
    ExplorerDirs.SettingsSmoke.PhotoPrintSelectionSmoke.Run();
    ExplorerDirs.SettingsSmoke.PhotoPrintLayoutSmoke.Run();
    ExplorerDirs.SettingsSmoke.PhotoPrintImageLoaderSmoke.Run();
    ExplorerDirs.SettingsSmoke.PhotoPrintPreviewGateSmoke.Run();
    ExplorerDirs.SettingsSmoke.PhotoPrintWindowMarkupSmoke.Run();
    PaneThemeMarkupSmoke.Run();
    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.CheckSemanticBrushRoles();
    ExplorerDirs.SettingsSmoke.ViewStyleIconSmoke.Run();
    ExplorerDirs.SettingsSmoke.ExplorerChromeIconSmoke.Run();
    ExplorerDirs.SettingsSmoke.NativeShellTreeIconFallbackSmoke.Run();
    ExplorerDirs.SettingsSmoke.ShellIconProviderSmoke.Run();
    QuickFilterSessionSmoke.Run(root);
    QuickFilterFavoritesSmoke.Run(root);
    QuickFilterHistoryStoreSmoke.Run(root);
    TabHoverActivationSmoke.Run(root);
    ExplorerDirs.SettingsSmoke.NewTabLocationSmoke.Run();
    ExplorerDirs.SettingsSmoke.CrossPaneTabTransferSmoke.Run();
    QDirBlankTabDoubleClickSmoke.Run();
    if (args.Length == 2 && args[0] == "--inspect-qdr")
    {
        var inspectedQdr = QdrImporter.Load(args[1]);
        Console.WriteLine($"QDR import: {inspectedQdr.TabCount} tabs; pane counts {string.Join('/', inspectedQdr.Panes.Select(pane => pane.Count))}; warnings {inspectedQdr.Warnings.Count}");
        Console.WriteLine($"QDR view snapshots: {inspectedQdr.Panes.SelectMany(pane => pane).Count(tab => tab.View.DisplayColumns is not null)}; sorted tabs: {inspectedQdr.Panes.SelectMany(pane => pane).Count(tab => tab.View.SortColumns.Count > 0)}");
        Console.WriteLine($"QDR layout: {(inspectedQdr.Layout?.ToString() ?? "unsupported")}; active pane: {(inspectedQdr.ActivePane?.ToString() ?? "unchanged")}");
        foreach (var warning in inspectedQdr.Warnings) Console.WriteLine($"QDR warning: {warning}");
    }
    var columnsProfile = SessionStore.Render(state).Replace("DisplayColumnCount=0",
        $"DisplayColumnCount=2\nDisplayColumn0={groupKey},180\nDisplayColumn1={sortKey},320",
        StringComparison.Ordinal);
    var columnsRoundTrip = SessionStore.Render(SessionStore.Parse(columnsProfile)!);
    Check(columnsRoundTrip.Contains($"DisplayColumn0={groupKey},180", StringComparison.Ordinal) &&
          columnsRoundTrip.Contains($"DisplayColumn1={sortKey},320", StringComparison.Ordinal),
          "visible column identity, order and widths survive session round-trip");
    var savedColumns = SessionStore.Parse(columnsProfile)!.Panes[0][0].View.DisplayColumns!;
    var mutableColumns = savedColumns.ToList();
    var independentColumns = (first.View with { DisplayColumns = mutableColumns }).Copy();
    mutableColumns.Clear();
    Check(independentColumns.DisplayColumns!.SequenceEqual(savedColumns),
        "duplicated view has an independent column layout snapshot");
    var columnIni = Path.Combine(root, "columns.ini");
    var columnStore = new SessionStore(columnIni, Path.Combine(root, "missing-columns.json"));
    var legacyColumns = SessionStore.Render(state).Replace("FormatVersion=10", "FormatVersion=1")
        .Replace("DefaultIconSize=-1" + Environment.NewLine, "")
        .Replace("DisplayColumnCount=0" + Environment.NewLine, "");
    File.WriteAllText(columnIni, legacyColumns);
    var oldColumnsState = columnStore.Load()!;
    Check(columnStore.LoadWarning is null && oldColumnsState.Panes[0][0].View.DisplayColumns is null,
        "v1 loads without damage warning and leaves the Shell column defaults alone");
    columnStore.Save(oldColumnsState);
    columnStore.Save(oldColumnsState);
    var upgradeBackups = Directory.GetFiles(root, "columns.ini.before-v10-*.bak");
    Check(upgradeBackups.Length == 1 && File.ReadAllText(upgradeBackups[0]) == legacyColumns &&
        File.ReadAllText(columnIni).Contains("FormatVersion=10"), "v1 migration keeps exactly one original snapshot");
    foreach (var damaged in new[]
    {
        columnsProfile.Replace("DisplayColumnCount=2", "DisplayColumnCount=1"),
        columnsProfile.Replace($"DisplayColumn1={sortKey},320", $"DisplayColumn1={groupKey},320"),
        columnsProfile.Replace($"DisplayColumn1={sortKey},320", $"DisplayColumn1={sortKey},-3"),
        columnsProfile.Replace($"DisplayColumn1={sortKey},320", ""),
        columnsProfile.Replace("DisplayColumnCount=2", "DisplayColumnCount=99999999")
    })
    {
        File.WriteAllText(columnIni, damaged);
        var recoveredColumns = columnStore.Load()!;
        Check(columnStore.LoadWarning is not null && recoveredColumns.Panes[0][0].Path == first.Path &&
            recoveredColumns.Panes[0][0].View.DisplayColumns is null &&
            recoveredColumns.Panes[0][0].View.SortColumns.SequenceEqual(first.View.SortColumns),
            "bad count, duplicate, width or missing column drops only the damaged layout");
        Check(Directory.GetFiles(root, "columns.ini.invalid-*.bak").Any(path => File.ReadAllText(path) == damaged),
            "damaged column profile is backed up before any future save");
    }
    Check(Marshal.SizeOf<ShellColumnManager.NativeKey>() == 20 &&
        Marshal.SizeOf<ShellColumnManager.ColumnInfo>() == 184 &&
        Marshal.OffsetOf<ShellColumnManager.ColumnInfo>("Width").ToInt32() == 12,
        "column COM structures match Windows SDK layout");
    Check(cachedView.SortColumns.Count == 1 && cachedView.SortColumns[0].Key == sortKey,
          "cached view state keeps independent sort columns after its source changes");
    var shellView = new FakeShellViewStateSource
    {
        Mode = 3,
        IconSize = 24,
        Sorts = [new SortColumnState(sortKey, 1)],
        Group = groupKey,
        GroupAscending = false
    };
    var captured = ShellViewStateCapture.Capture(cachedView, shellView, out var captureComplete);
    Check(captureComplete && captured.Mode == 3 && captured.IconSize == 24 &&
          captured.SortColumns.SequenceEqual(shellView.Sorts) && captured.GroupKey == groupKey &&
          !captured.GroupAscending, "complete Shell read replaces every cached view field");
    foreach (var failedStep in new[] { "mode", "count", "columns", "group", "invalid-count", "short-columns" })
    {
        shellView.FailedStep = failedStep;
        captured = ShellViewStateCapture.Capture(cachedView, shellView, out captureComplete);
        Check(!captureComplete && captured.Mode == cachedView.Mode &&
              captured.IconSize == cachedView.IconSize &&
              captured.SortColumns.SequenceEqual(cachedView.SortColumns) &&
              captured.GroupKey == cachedView.GroupKey &&
              captured.GroupAscending == cachedView.GroupAscending,
              $"{failedStep} leaves the last complete Shell view state intact");
    }
    shellView.FailedStep = null;
    captured = ShellViewStateCapture.Capture(cachedView, shellView, out captureComplete);
    Check(captureComplete && captured.Mode == 3,
          "a later complete Shell read clears the capture failure");
    shellView.DisplayColumns = savedColumns;
    captured = ShellViewStateCapture.Capture(cachedView, shellView, out captureComplete);
    Check(captureComplete && captured.DisplayColumns!.SequenceEqual(savedColumns),
        "column capture joins the complete view snapshot");
    foreach (var invalidColumns in new IReadOnlyList<DisplayColumnState>?[]
    {
        savedColumns, [], [savedColumns[0], savedColumns[0]],
        [new DisplayColumnState(sortKey, -3)]
    })
    {
        shellView.DisplayColumns = invalidColumns;
        shellView.FailedStep = ReferenceEquals(invalidColumns, savedColumns) ? "display-columns" : null;
        var failedCapture = ShellViewStateCapture.Capture(captured, shellView, out captureComplete);
        Check(!captureComplete && failedCapture.DisplayColumns!.SequenceEqual(savedColumns),
            "failed or invalid column capture preserves the entire previous snapshot");
    }
    shellView.FailedStep = null;
    shellView.DisplayColumns = null;
    var noColumnSupport = ShellViewStateCapture.Capture(captured, shellView, out captureComplete);
    Check(captureComplete && noColumnSupport.DisplayColumns!.SequenceEqual(savedColumns),
        "a view without IColumnManager retains the saved columns for the next supported view");
    var noSortView = new FakeShellViewStateSource
    {
        Mode = 4,
        IconSize = 16,
        FailedStep = "columns"
    };
    captured = ShellViewStateCapture.Capture(cachedView, noSortView, out captureComplete);
    Check(captureComplete && captured.SortColumns.Count == 0 && captured.GroupKey is null,
          "zero sort columns clear the cache without querying a column array");
    var mutableShellSorts = new List<SortColumnState> { new(sortKey, -1) };
    captured = ShellViewStateCapture.Capture(cachedView, new FakeShellViewStateSource
    {
        Mode = 4,
        IconSize = 16,
        Sorts = mutableShellSorts
    }, out captureComplete);
    mutableShellSorts.Clear();
    Check(captureComplete && captured.SortColumns.Count == 1,
          "captured sort columns do not alias the Shell reader's list");
    Check(StartupDirectory.Parse([]) == new StartupDirectoryRequest(null, null, null),
          "startup without a directory restores the normal session");
    Check(StartupDirectory.Parse([@"C:\Users\marvin"]).Directory == @"C:\Users\marvin" &&
          StartupDirectory.Parse([@"\\server\share\docs"]).Directory == @"\\server\share\docs",
          "local and UNC directory arguments keep their normalized paths");
    Check(StartupDirectory.Parse(["."]).Directory == Path.GetFullPath("."),
          "relative startup directory resolves against the caller's current directory");
    Check(StartupDirectory.Parse([@"C:\work\saved.qdr"]).QdrFile == Path.GetFullPath(@"C:\work\saved.qdr") &&
          StartupDirectory.Parse([@"C:\work\folder"]).Directory == Path.GetFullPath(@"C:\work\folder") &&
          StartupDirectory.Parse([@"C:\work\saved.QDR"]).QdrFile == Path.GetFullPath(@"C:\work\saved.QDR"),
          "Q-Dir status arguments are distinguished from directory arguments by extension");
    Check(StartupDirectory.Parse(["", "extra"]).Directory is null &&
          StartupDirectory.Parse(["\0"]).Warning is not null,
          "unsupported or invalid arguments do not become a navigation target");
    var routerProfile = Path.Combine(root, "router-smoke", "ExplorerDirs.ini");
    var routerOtherProfile = Path.Combine(root, "router-other", "ExplorerDirs.ini");
    Check(FolderOpenRouter.PipeName(routerProfile) != FolderOpenRouter.PipeName(routerOtherProfile),
          "folder routing is scoped to its portable profile");
    var routedPath = Path.Combine(root, "routed-folder");
    Check(FolderOpenRouter.TryForwardAsync(routerProfile, routedPath,
              TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult() == FolderForwardResult.Unavailable,
          "no running window leaves the directory request with the launcher");
    Check(FolderOpenRouter.TryActivateAsync(routerProfile, TimeSpan.FromMilliseconds(150))
              .GetAwaiter().GetResult() == FolderForwardResult.Unavailable,
          "no running process leaves the activation request with the launcher");
    Check(FolderOpenRouter.TryForwardQdrAsync(routerProfile, Path.Combine(root, "saved.qdr"),
              TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult() == FolderForwardResult.Unavailable &&
          FolderOpenRouter.TryForwardQdrAsync(routerProfile, Path.Combine(root, "not-qdr.txt"),
              TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult() == FolderForwardResult.Rejected,
          "Q-Dir state requests use the route when available and reject non-qdr files");
    var routedPaths = new List<string>();
    using (var router = FolderOpenRouter.TryStart(routerProfile, path =>
           {
               lock (routedPaths) routedPaths.Add(path);
               return Task.FromResult(!path.EndsWith("deny", StringComparison.Ordinal));
           }) ?? throw new Exception("folder router did not start"))
    {
        Check(FolderOpenRouter.TryStart(routerProfile, _ => Task.FromResult(true)) is null,
              "only one window owns the folder route for a profile");
        Check(FolderOpenRouter.TryActivateAsync(routerProfile, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "an owner without an activation target does not falsely acknowledge activation");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "a running window acknowledges a newly opened directory tab");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, Path.Combine(root, "deny"), TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "a rejected folder request is not reported as opened");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, "relative", TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "the pipe does not accept relative paths from another process");
        var parallelPaths = Enumerable.Range(0, 4)
            .Select(index => Path.Combine(root, "parallel-" + index)).ToArray();
        var parallelForwards = parallelPaths.Select(path => FolderOpenRouter.TryForwardAsync(
            routerProfile, path, TimeSpan.FromSeconds(3))).ToArray();
        Task.WaitAll(parallelForwards);
        Check(parallelForwards.All(task => task.Result == FolderForwardResult.Accepted),
              "simultaneous directory requests all reach the existing process");
    }
    Check(routedPaths.Count == 6 && routedPaths[0] == routedPath,
          "only valid absolute folder requests reach the window callback");
    var activationProfile = Path.Combine(root, "router-activate", "ExplorerDirs.ini");
    var activationCount = 0;
    using (var activationRouter = FolderOpenRouter.TryStart(activationProfile,
               _ => Task.FromResult(false), () =>
               {
                   Interlocked.Increment(ref activationCount);
                   return Task.FromResult(true);
               }) ?? throw new Exception("activation router did not start"))
    {
        Check(FolderOpenRouter.TryActivateAsync(activationProfile, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted && activationCount == 1,
              "a second launch activates the existing window without reopening its session roster");
        Check(FolderOpenRouter.TryForwardAsync(activationProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected && activationCount == 1,
              "directory routing remains distinct from window activation");
    }
    var qdrProfile = Path.Combine(root, "router-qdr", "ExplorerDirs.ini");
    string? routedQdrPath = null;
    using (var qdrRouter = FolderOpenRouter.TryStart(qdrProfile, _ => Task.FromResult(false),
               openQdr: path =>
               {
                   routedQdrPath = path;
                   return Task.FromResult(true);
               }) ?? throw new Exception("Q-Dir route did not start"))
    {
        var qdrPath = Path.GetFullPath(Path.Combine(root, "Saved Workspace.qdr"));
        Check(FolderOpenRouter.TryForwardQdrAsync(qdrProfile, qdrPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted && routedQdrPath == qdrPath,
              "a Q-Dir startup request reaches its dedicated existing-process callback");
        Check(FolderOpenRouter.TryForwardAsync(qdrProfile, Path.Combine(root, "folder"), TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "Q-Dir startup routing remains distinct from directory navigation");
    }
    var unexpectedProfile = Path.Combine(root, "router-unexpected", "ExplorerDirs.ini");
    using (var unexpectedServer = new NamedPipeServerStream(FolderOpenRouter.PipeName(unexpectedProfile),
           PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
           PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance))
    {
        var unexpectedResponse = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await unexpectedServer.WaitForConnectionAsync(deadline.Token);
            var header = new byte[4];
            await unexpectedServer.ReadExactlyAsync(header, deadline.Token);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await unexpectedServer.ReadExactlyAsync(payload, deadline.Token);
            await unexpectedServer.WriteAsync(new byte[] { 2 }, deadline.Token);
        });
        var result = FolderOpenRouter.TryForwardAsync(unexpectedProfile, routedPath,
            TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        unexpectedResponse.GetAwaiter().GetResult();
        Check(result == FolderForwardResult.Uncertain,
              "an unknown pipe response does not pretend the directory was rejected");
    }
    var stalledProfile = Path.Combine(root, "router-stalled", "ExplorerDirs.ini");
    using (var stalledRouter = FolderOpenRouter.TryStart(stalledProfile,
               _ => Task.FromResult(true)) ?? throw new Exception("stalled router did not start"))
    using (var stalledClient = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(stalledProfile),
               PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        stalledClient.Connect(2000); // Connect, then never send the request header.
        Check(FolderOpenRouter.TryForwardAsync(stalledProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "a connected client that sends no request cannot block later folder launches indefinitely");
        using var partialClient = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(stalledProfile),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        partialClient.Connect(2000);
        partialClient.Write(new byte[] { 8, 0, 0, 0 }); // Claim a path but send no payload.
        partialClient.Flush();
        Check(FolderOpenRouter.TryForwardAsync(stalledProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "an incomplete directory payload cannot block later folder launches indefinitely");
    }
    var startingProfile = Path.Combine(root, "router-starting", "ExplorerDirs.ini");
    var windowReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using (var startingRouter = FolderOpenRouter.TryStart(startingProfile, async _ =>
           {
               await windowReady.Task;
               return true;
           }) ?? throw new Exception("starting folder router did not start"))
    {
        var pendingForward = FolderOpenRouter.TryForwardAsync(startingProfile, routedPath,
            TimeSpan.FromSeconds(2));
        Check(!pendingForward.Wait(TimeSpan.FromMilliseconds(100)),
              "a folder request waits while the receiving window initializes");
        windowReady.SetResult(true);
        Check(pendingForward.GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "the startup race forwards after the first window becomes ready");
    }
    var stoppingProfile = Path.Combine(root, "router-stopping", "ExplorerDirs.ini");
    var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var neverReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var stoppingRouter = FolderOpenRouter.TryStart(stoppingProfile, _ =>
    {
        callbackEntered.TrySetResult();
        return neverReady.Task;
    }) ?? throw new Exception("stopping router did not start");
    var waitingForward = FolderOpenRouter.TryForwardAsync(stoppingProfile, routedPath,
        TimeSpan.FromSeconds(5));
    Check(callbackEntered.Task.Wait(TimeSpan.FromSeconds(2)),
          "folder router reached the pending window callback");
    stoppingRouter.Dispose();
    FolderOpenRouter? replacementRouter = null;
    var releasedOnStop = SpinWait.SpinUntil(() =>
    {
        replacementRouter = FolderOpenRouter.TryStart(stoppingProfile, _ => Task.FromResult(true));
        return replacementRouter is not null;
    }, TimeSpan.FromSeconds(2));
    neverReady.TrySetResult(false); // Let the old callback finish even when the assertion fails.
    using (replacementRouter)
        Check(releasedOnStop, "disposing a router releases ownership despite a pending window callback");
    _ = waitingForward.GetAwaiter().GetResult();
    Check(ShellItemKind.IsFileSystemFolder(0x60000000) &&
          !ShellItemKind.IsFileSystemFolder(0x40000000) &&
          !ShellItemKind.IsFileSystemFolder(0x60400000) &&
          !ShellItemKind.IsFileSystemFolder(0x20000000),
          "only ordinary filesystem folders get the new-tab directory command");
    Check(ShellMenuCommand.TryGetMenuId((nint)0xE000, IntPtr.Zero, out var menuId) &&
          menuId == 0xE000 &&
          !ShellMenuCommand.TryGetMenuId((nint)0x1E000, IntPtr.Zero, out _) &&
          !ShellMenuCommand.TryGetMenuId((nint)0xE000, (nint)123, out _) &&
          !ShellMenuCommand.TryGetMenuId(IntPtr.Zero, IntPtr.Zero, out _),
          "custom menu commands ignore accelerators, control notifications, and separator IDs");
    ShellMenuTargetTrackerSmoke.Run();
    ExplorerDirs.SettingsSmoke.RestartShortcutSmoke.Run();
    ShellStatusCountRefreshGateSmoke.Run();
    ExplorerDirs.SettingsSmoke.FullScreenPresentationSmoke.Run();
    Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent, "Backspace goes to parent");
    Check(KeyboardShortcuts.Resolve(0x26, 1) == ExplorerShortcut.Parent, "Alt+Up goes to parent");
    Check(KeyboardShortcuts.Resolve(0x25, 1) == ExplorerShortcut.Back, "Alt+Left goes back");
    Check(KeyboardShortcuts.Resolve(0x27, 1) == ExplorerShortcut.Forward, "Alt+Right goes forward");
    Check(KeyboardShortcuts.Resolve(0x7a, 1) == ExplorerShortcut.RestartApplication &&
          KeyboardShortcuts.Resolve(0x7a, 0) == ExplorerShortcut.ToggleFullScreen,
          "Alt+F11 restarts the app while F11 toggles full-screen mode");
    Check(KeyboardShortcuts.ResolveTabHeaderArrow(0x25, 2, tabHeaderFocused: true) == ExplorerShortcut.PreviousTab &&
          KeyboardShortcuts.ResolveTabHeaderArrow(0x27, 2, tabHeaderFocused: true) == ExplorerShortcut.NextTab,
          "Ctrl+Left/Right switches tabs while a tab header has focus");
    Check(KeyboardShortcuts.ResolveTabHeaderArrow(0x25, 2, tabHeaderFocused: false) == ExplorerShortcut.None &&
          KeyboardShortcuts.ResolveTabHeaderArrow(0x27, 2, tabHeaderFocused: false) == ExplorerShortcut.None &&
          KeyboardShortcuts.Resolve(0x25, 2) == ExplorerShortcut.None &&
          KeyboardShortcuts.Resolve(0x27, 2) == ExplorerShortcut.None,
          "Ctrl+Left/Right remains available to the native file view outside tab headers");
    Check(KeyboardShortcuts.Resolve(0x79, 0) == ExplorerShortcut.ActivateMenu,
          "F10 activates the main menu bar");
    Check(KeyboardShortcuts.Resolve(0x79, 1) == ExplorerShortcut.None,
          "Alt+F10 is not consumed as menu activation");
    Check(KeyboardShortcuts.Resolve(0x4e, 2) == ExplorerShortcut.NewWindow,
          "Ctrl+N requests an independent window session");
    Check(KeyboardShortcuts.Resolve(0x51, 2) == ExplorerShortcut.QuickFolderMenu,
          "Ctrl+Q opens the Q-Dir-style quick-folder menu");
    var reopenedTabCount = 0;
    var selectedFolderOpenCalled = false;
    TabRecoveryShortcut.Dispatch(true,
        () => { selectedFolderOpenCalled = true; return true; },
        () => reopenedTabCount++);
    Check(selectedFolderOpenCalled && reopenedTabCount == 0,
          "Ctrl+Shift+T opens a selected folder instead of restoring a closed tab");
    var wrongFocusFolderOpenCalled = false;
    TabRecoveryShortcut.Dispatch(false,
        () => { wrongFocusFolderOpenCalled = true; return true; },
        () => reopenedTabCount++);
    Check(!wrongFocusFolderOpenCalled && reopenedTabCount == 1,
          "Ctrl+Shift+T does not open a folder from a non-file-view focus");
    TabRecoveryShortcut.Dispatch(true, () => false, () => reopenedTabCount++);
    Check(reopenedTabCount == 2,
          "Ctrl+Shift+T restores a closed tab when no single folder can be opened");
    Check(KeyboardShortcuts.Resolve(0x50, 1) == ExplorerShortcut.TogglePreviewPane,
          "Alt+P toggles the active tab's preview pane");
    Check(KeyboardShortcuts.Resolve(0x50, 5) == ExplorerShortcut.ToggleDetailsPane,
          "Alt+Shift+P toggles the active tab's details pane");
    Check(KeyboardShortcuts.Resolve(0x73, 0) == ExplorerShortcut.AddressTree,
          "F4 routes to the address tree transition");
    Check(KeyboardShortcuts.Resolve(0x78, 0) == ExplorerShortcut.CalculateDirectorySize,
          "F9 requests the active directory's recursive size");
    Check(KeyboardShortcuts.Resolve(0x09, 0) == ExplorerShortcut.NextFocusStop &&
          KeyboardShortcuts.Resolve(0x09, 4) == ExplorerShortcut.PreviousFocusStop,
          "Tab and Shift+Tab traverse pane focus stops");
    Check(KeyboardShortcuts.Resolve(0x75, 0) == ExplorerShortcut.NextFocusRegion &&
          KeyboardShortcuts.Resolve(0x75, 4) == ExplorerShortcut.PreviousFocusRegion,
          "F6 and Shift+F6 traverse major regions in opposite directions");
    Check(KeyboardShortcuts.NextPane(3, 4, false) == 0 &&
          KeyboardShortcuts.NextPane(0, 4, true) == 3 &&
          KeyboardShortcuts.NextPane(0, 1, false) == 0,
          "visible pane focus wraps");
    var focusStops = new[] { 13, 12, 13, 12 };
    Check(FocusTraversal.Move(0, 9, focusStops, false) == (0, 10) &&
          FocusTraversal.Move(0, 10, focusStops, false) == (0, 11) &&
          FocusTraversal.Move(0, 11, focusStops, false) == (0, 12) &&
          FocusTraversal.Move(0, 12, focusStops, false) == (1, 0) &&
          FocusTraversal.Move(1, 0, focusStops, true) == (0, 12) &&
          FocusTraversal.Move(0, 12, focusStops, true) == (0, 11) &&
          FocusTraversal.Move(0, 0, focusStops, true) == (3, 11) &&
          FocusTraversal.Move(2, -1, focusStops, true) == (2, 12),
          "forward and reverse tab order includes Details header, status tools and wraps panes");
    var detailedPaneStops = PaneFocusStopMap.Create(false, false, false, hasColumnHeader: true,
        addressHistoryButtonVisible: true);
    var iconPaneStops = PaneFocusStopMap.Create(false, false, false, hasColumnHeader: false,
        addressHistoryButtonVisible: true);
    Check(detailedPaneStops.PaneViewStyle == 4 && detailedPaneStops.Address == 5 &&
          detailedPaneStops.FileView == 12 && detailedPaneStops.StatusTools == 13 && detailedPaneStops.Count == 14 &&
          iconPaneStops.PaneViewStyle == 4 && iconPaneStops.Address == 5 &&
          iconPaneStops.FileView == 11 && iconPaneStops.StatusTools == 12 && iconPaneStops.Count == 13,
          "the pane view-style button and address controls precede file-view stops in every view mode");
    Check(FocusTraversal.Move(0, detailedPaneStops.FileView, [detailedPaneStops.Count, iconPaneStops.Count], false) == (0, detailedPaneStops.StatusTools) &&
          FocusTraversal.Move(0, detailedPaneStops.StatusTools, [detailedPaneStops.Count, iconPaneStops.Count], false) == (1, 0) &&
          FocusTraversal.Move(1, 0, [detailedPaneStops.Count, iconPaneStops.Count], true) == (0, detailedPaneStops.StatusTools),
          "Tab and reverse Tab traverse the status tools between file views");
    IReadOnlyList<int>[] majorStops = [[4, 8, 9, 10, 11], [4, 8, 9, 10], [4, 8, 9, 10, 11], [4, 8, 9, 10]];
    Check(FocusTraversal.MoveMajor(0, 4, majorStops, false) == (0, 8), "F6 advances from pane navigation controls");
    Check(FocusTraversal.MoveMajor(0, 8, majorStops, false) == (0, 9), "F6 advances to the visible tab header");
    Check(FocusTraversal.MoveMajor(0, 9, majorStops, false) == (0, 10), "F6 advances to the Details header");
    Check(FocusTraversal.MoveMajor(0, 10, majorStops, false) == (0, 11), "F6 advances from Details header to file view");
    Check(FocusTraversal.MoveMajor(0, 11, majorStops, false) == (1, 4), "F6 advances from file view to status tools, then pane");
    Check(FocusTraversal.MoveMajor(1, 4, majorStops, true) == (0, 11), "reverse F6 returns to the previous pane status tools");
    Check(FocusTraversal.MoveMajor(0, 4, majorStops, true) == (3, 10), "reverse F6 wraps to the previous pane's last stop");
    Check(FocusTraversal.MoveMajor(2, 6, majorStops, false) == (2, 8), "F6 skips fine controls");
    Check(FocusTraversal.MoveMajor(2, -1, majorStops, true) == (2, 11),
          "reverse F6 starts at the final region when current focus is outside the pane");
    Check(KeyboardShortcuts.Resolve(0x08, 2) == ExplorerShortcut.None,
          "Ctrl+Backspace remains available to text controls");
    Check(KeyboardShortcuts.Resolve(0x44, 2) == ExplorerShortcut.None,
          "Ctrl+D is left to the native Shell view for Explorer's delete-to-Recycle-Bin command");
    Check(KeyboardShortcuts.Resolve(0x26, 2) == ExplorerShortcut.None &&
          KeyboardShortcuts.Resolve(0x28, 2) == ExplorerShortcut.None &&
          KeyboardShortcuts.Resolve(0x20, 2) == ExplorerShortcut.None,
          "Ctrl+Up/Down and Ctrl+Space remain available to Shell for individual multi-selection");
    Check(KeyboardShortcuts.Resolve(0x4e, 6) == ExplorerShortcut.NewFolder,
          "Ctrl+Shift+N routes to the Shell background New Folder command");
    Check(KeyboardShortcuts.Resolve(0x56, 6) == ExplorerShortcut.PasteSpecial &&
          KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.PasteSpecial),
          "Ctrl+Shift+V opens Paste Special only from the file view");
    Check(KeyboardShortcuts.Resolve(0x0d, 4) == ExplorerShortcut.OpenSelectionInNewTab &&
          KeyboardShortcuts.Resolve(0x0d, 0) == ExplorerShortcut.None,
          "Shift+Enter opens the selected folder in a new tab without changing plain Enter");
    FileViewShortcutDispatchSmoke.Run();
    Check(KeyboardShortcuts.Resolve(0x41, 6) == ExplorerShortcut.InvertSelection,
          "Ctrl+Shift+A routes to the active Shell view's invert-selection command");
    Check(KeyboardShortcuts.Resolve(0x72, 1) == ExplorerShortcut.SearchSelectedFolder &&
          KeyboardShortcuts.Resolve(0x72, 0) == ExplorerShortcut.Search &&
          KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.SearchSelectedFolder) &&
          !KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.Search),
          "Alt+F3 uses a file-view folder selection while F3 keeps the current search scope");
    Check(KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.OpenSelectionInNewTab) &&
          KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.InvertSelection) &&
          KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.AutoFitColumns) &&
          KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.NewFolder) &&
          !KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.NewTab),
          "file-view commands stay out of text-input focus while general tab commands remain global");
    Check(KeyboardShortcuts.Resolve(0x45, 6) == ExplorerShortcut.ExpandTree,
          "Ctrl+Shift+E expands the visible navigation tree to the current folder");
    Check(Enumerable.Range(1, 9).All(number =>
              KeyboardShortcuts.Resolve(0x30 + number, 2) == ExplorerShortcut.Tab1 + (number - 1)) &&
          KeyboardShortcuts.Resolve(0x30, 2) == ExplorerShortcut.None &&
          Enumerable.Range(1, 8).All(number =>
              KeyboardShortcuts.Resolve(0x30 + number, 6) == ExplorerShortcut.View1 + (number - 1)) &&
          KeyboardShortcuts.Resolve(0x39, 6) == ExplorerShortcut.None,
          "Ctrl+1-9 selects tabs; Ctrl+Shift+1 changes view without selecting a tab; unspecified 9 stays native");
    var stylesStore = new SessionStore(Path.Combine(root, "view-styles.ini"), Path.Combine(root, "no-styles.json"));
    var stylesState = new SessionState(1, 4,
        [ViewStyle.All.Select(style => first with
        {
            View = first.View with { Mode = style.Mode, IconSize = style.IconSize }
        }).ToList()]);
    stylesStore.Save(stylesState);
    var restoredStyles = stylesStore.Load()!;
    Check(restoredStyles.DefaultViewMode == 4 && restoredStyles.Panes[0].Count == 8 &&
        restoredStyles.Panes[0].Zip(stylesState.Panes[0]).All(pair =>
            pair.First.View.Mode == pair.Second.View.Mode &&
            pair.First.View.IconSize == pair.Second.View.IconSize &&
            pair.First.View.SortColumns.SequenceEqual(first.View.SortColumns) &&
            pair.First.View.GroupKey == groupKey && !pair.First.View.GroupAscending),
        "all view presets survive INI round-trip with independent mode/size and unchanged defaults, sort and grouping");
    Check(KeyboardShortcuts.Resolve(0x72, 0) == ExplorerShortcut.Search &&
          KeyboardShortcuts.Resolve(0x45, 2) == ExplorerShortcut.Search &&
          KeyboardShortcuts.Resolve(0x46, 2) == ExplorerShortcut.Search,
          "F3 and Ctrl+E/F focus current-directory search");
    Check(KeyboardShortcuts.Resolve(0x4d, 2) == ExplorerShortcut.QuickFilter,
          "Ctrl+M opens the pane-local quick filter");
    Check(KeyboardShortcuts.Resolve(0x52, 2) == ExplorerShortcut.Refresh &&
          KeyboardShortcuts.Resolve(0x74, 0) == ExplorerShortcut.Refresh,
          "Ctrl+R and F5 refresh the same active Explorer view action");
    Check(KeyboardShortcuts.Resolve(0x6b, 2) == ExplorerShortcut.AutoFitColumns,
          "Ctrl+Numpad Plus routes to the native Shell view to auto-fit Details columns");
    Check(KeyboardShortcuts.Resolve(0x6b, 0) != ExplorerShortcut.AutoFitColumns &&
          KeyboardShortcuts.Resolve(0x6b, 4) != ExplorerShortcut.AutoFitColumns &&
          KeyboardShortcuts.Resolve(0x64, 2) != ExplorerShortcut.AutoFitColumns,
          "auto-fit columns requires Ctrl+VK_ADD and does not claim other numpad keys");
    ExplorerDirs.SettingsSmoke.ShellAutoColumnWidthRoutingSmoke.Run();
    var searchFocusStops = new[] { 16, 15, 13, 12 };
    Check(FocusTraversal.Move(0, 7, searchFocusStops, false) == (0, 8) &&
          FocusTraversal.Move(0, 8, searchFocusStops, false) == (0, 9) &&
          FocusTraversal.Move(0, 9, searchFocusStops, false) == (0, 10) &&
          FocusTraversal.Move(0, 10, searchFocusStops, false) == (0, 11) &&
          FocusTraversal.Move(0, 11, searchFocusStops, true) == (0, 10) &&
          FocusTraversal.Move(0, 10, searchFocusStops, true) == (0, 9) &&
          FocusTraversal.Move(0, 13, searchFocusStops, false) == (0, 14) &&
          FocusTraversal.Move(0, 14, searchFocusStops, false) == (0, 15) &&
          FocusTraversal.Move(0, 15, searchFocusStops, false) == (1, 0) &&
          FocusTraversal.Move(1, 0, searchFocusStops, true) == (0, 15),
          "visible search controls extend each pane's reversible Tab order");
    var searchScope = Path.Combine(root, "search scope & 中文");
    Directory.CreateDirectory(searchScope);
    var searchUri = ShellSearchLocation.BuildUri(searchScope, "name:报告 & #1");
    Check(searchUri.StartsWith("search-ms:query=", StringComparison.Ordinal) &&
          searchUri.Contains("%26", StringComparison.Ordinal) &&
          searchUri.Contains("%23", StringComparison.Ordinal) &&
          searchUri.Contains("crumb=location:", StringComparison.Ordinal) &&
          ShellSearchLocation.TryReadScopeAndQuery(searchUri, out var parsedScope, out var parsedQuery) &&
          parsedScope == searchScope && parsedQuery == "name:报告 & #1",
          "search query and scope round-trip without confusing URI separators");
    var selectedFolderScope = Path.Combine(searchScope, "selected folder");
    Directory.CreateDirectory(selectedFolderScope);
    var selectedFolderUri = ShellSearchLocation.BuildUri(selectedFolderScope, "needle");
    Check(ShellSearchLocation.TryReadScopeAndQuery(selectedFolderUri, out var selectedParsedScope, out var selectedParsedQuery) &&
          selectedParsedScope == selectedFolderScope && selectedParsedQuery == "needle",
          "selected-folder search URI keeps its folder scope distinct from the containing tab directory");
    var searchPidl = ShellSearchLocation.ParsePidl(searchUri);
    try
    {
        SearchFolderSmoke.RequireFolder(searchPidl);
        var searchHistory = new ShellSearchHistory();
        searchHistory.Remember(searchPidl, searchUri);
        Check(searchPidl != IntPtr.Zero && searchHistory.TryResolve(searchPidl, out var restoredSearchUri) &&
              ShellSearchLocation.TryReadScopeAndQuery(restoredSearchUri, out var shellScope, out var shellQuery) &&
              shellScope == searchScope && shellQuery == "name:报告 & #1",
              "native search location retains its query separately from its caption");
        var otherUri = ShellSearchLocation.BuildUri(searchScope, "another-query");
        var otherPidl = ShellSearchLocation.ParsePidl(otherUri);
        try
        {
            SearchFolderSmoke.RequireFolder(otherPidl);
            Check(!searchHistory.TryResolve(otherPidl, out _), "different queries must not alias by folder caption");
            searchHistory.Remember(otherPidl, otherUri);
            Check(searchHistory.TryResolve(searchPidl, out var firstSearchUri) && firstSearchUri == searchUri &&
                  searchHistory.TryResolve(otherPidl, out var secondSearchUri) && secondSearchUri == otherUri &&
                  !new ShellSearchHistory().TryResolve(searchPidl, out _),
                "search query history remains distinct and browser-local");
            searchHistory.Clear();
            Check(!searchHistory.TryResolve(searchPidl, out _) && !searchHistory.TryResolve(otherPidl, out _),
                "disposing a Shell browser releases its search identity mappings");
            searchHistory.Remember(searchPidl, searchUri);
            Check(searchHistory.TryResolve(searchPidl, out var reopenedSearchUri) && reopenedSearchUri == searchUri,
                "a recreated Shell browser can record fresh search identities");
        }
        finally { Marshal.FreeCoTaskMem(otherPidl); }
        var collisionHistory = new ShellSearchHistory();
        var collisionUriA = searchUri[..searchUri.IndexOf("&displayname=", StringComparison.Ordinal)] + "&displayname=identical-test-caption";
        var collisionUriB = otherUri[..otherUri.IndexOf("&displayname=", StringComparison.Ordinal)] + "&displayname=identical-test-caption";
        var collisionPidlA = ShellSearchLocation.ParsePidl(collisionUriA);
        var collisionPidlB = ShellSearchLocation.ParsePidl(collisionUriB);
        try
        {
            collisionHistory.Remember(collisionPidlA, collisionUriA);
            var rejectedCollision = false;
            try { collisionHistory.Remember(collisionPidlB, collisionUriB); }
            catch (COMException) { rejectedCollision = true; }
            Check(rejectedCollision && collisionHistory.TryResolve(collisionPidlA, out var keptUri) && keptUri == collisionUriA,
                "native caption collision must not replace an existing query association");
        }
        finally { Marshal.FreeCoTaskMem(collisionPidlB); Marshal.FreeCoTaskMem(collisionPidlA); }
    }
    finally { Marshal.FreeCoTaskMem(searchPidl); }
    var searchNavigation = new ShellNavigationState(searchScope);
    Check(searchNavigation.CanSearchCurrentLocation,
        "filesystem locations can start the app's scoped Search Folder flow");
    Check(searchNavigation.TryCompleteSearch(searchUri) &&
          searchNavigation.DirectoryPath == searchScope &&
          searchNavigation.SearchQuery == "name:报告 & #1" &&
          searchNavigation.IsSearchActive,
          "search results keep the scope as the tab's file-system directory");
    searchNavigation.CompleteDirectory(Path.Combine(searchScope, "child"));
    Check(!searchNavigation.IsSearchActive && searchNavigation.SearchQuery is null &&
          searchNavigation.DirectoryPath == Path.Combine(searchScope, "child") &&
          searchNavigation.TryCompleteSearch(searchUri) &&
          searchNavigation.DirectoryPath == searchScope && searchNavigation.SearchQuery == "name:报告 & #1" &&
          !searchNavigation.TryCompleteSearch("shell:unknown") && searchNavigation.IsSearchActive,
          "folder navigation clears search while travel-log search restores its query");
    const string virtualScope = "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    var virtualSearchUri = ShellSearchLocation.BuildUri(virtualScope, "probe");
    var virtualNavigation = new ShellNavigationState(virtualScope);
    Check(virtualNavigation.CanSearchCurrentLocation &&
          ShellSearchLocation.TryReadScopeAndQuery(virtualSearchUri, out var virtualParsedScope, out var virtualQuery) &&
          virtualParsedScope == virtualScope && virtualQuery == "probe" &&
          virtualNavigation.TryCompleteSearch(virtualSearchUri) && !virtualNavigation.IsFileSystemLocation,
          "confirmed absolute Shell folder identities can retain search scope without being mislabeled as filesystem paths");
    searchNavigation.CompleteDirectory("shell:RecycleBinFolder", isFileSystemLocation: false);
    Check(searchNavigation.CanSearchCurrentLocation &&
          searchNavigation.DirectoryPath == "shell:RecycleBinFolder" &&
          !searchNavigation.IsSearchActive,
          "known virtual Shell folders are eligible for Shell search scopes");
    var browserSite = new ShellBrowserSite();
    browserSite.OnStateChange(IntPtr.Zero, 0);
    browserSite.OnStateChange(IntPtr.Zero, 1);
    Check(browserSite.SelectionVersion == 0, "focus transitions do not invalidate a command selection");
    browserSite.OnStateChange(IntPtr.Zero, 2);
    browserSite.OnStateChange(IntPtr.Zero, 3);
    browserSite.OnStateChange(IntPtr.Zero, 4);
    Check(browserSite.SelectionVersion == 3, "selection, rename and checked-state changes invalidate command snapshots");
    var previousCallbackTrace = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE");
    var previousCallbackTiming = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING");
    try
    {
        Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE", "1");
        Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING", "1");
        var timedBrowserSite = new ShellBrowserSite();
        timedBrowserSite.OnStateChange(IntPtr.Zero, 2);
        Check(timedBrowserSite.StateChangeTraceSnapshot.Contains("change=2,start-ticks="),
            "opt-in Shell state-change trace records a common monotonic start timestamp");
        Check(timedBrowserSite.StateChangeTraceSnapshot.Contains(",elapsed-ms="),
            "opt-in Shell state-change trace records each callback duration");
        Check(timedBrowserSite.CallbackTimingSnapshot.Contains("state=calls=1,samples=1,"),
            "opt-in Shell state-change callback timing remains aggregated");
    }
    finally
    {
        Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE", previousCallbackTrace);
        Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING", previousCallbackTiming);
    }
    var serviceId = ShellBrowserSite.BrowserFrameServiceId;
    var browser2Id = ShellBrowserSite.DialogBrowser2Id;
    var serviceResult = browserSite.QueryService(ref serviceId, ref browser2Id, out var browser2Pointer);
    var flagResult = browserSite.GetViewFlags(out var searchFlags);
    try
    {
        var nativeFlags = 0u;
        var nativeFlagResult = -1;
        if (browser2Pointer != IntPtr.Zero)
        {
            var vtable = Marshal.ReadIntPtr(browser2Pointer);
            var getFlagsPointer = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
            nativeFlagResult = Marshal.GetDelegateForFunctionPointer<NativeGetViewFlags>(getFlagsPointer)(
                browser2Pointer, out nativeFlags);
        }
        Check(serviceResult == 0 && browser2Pointer != IntPtr.Zero &&
              flagResult == 0 &&
              searchFlags == ShellBrowserSite.NoIncludeItemFlag &&
              nativeFlagResult == 0 && nativeFlags == ShellBrowserSite.NoIncludeItemFlag,
              $"browser site exposes background search view flags through the Shell service " +
              $"(service=0x{serviceResult:X8}, pointer={browser2Pointer != IntPtr.Zero}, flags=0x{searchFlags:X8})");
    }
    finally { if (browser2Pointer != IntPtr.Zero) Marshal.Release(browser2Pointer); }
    var browserId = typeof(IComDialogBrowser).GUID;
    var browser3Id = new Guid("c8ad25a1-3294-41ee-8165-71174bd01c57");
    var browser3Result = browserSite.QueryService(ref serviceId, ref browser3Id, out var browser3Pointer);
    try
    {
        Check(browser3Result == 0 && browser3Pointer != IntPtr.Zero,
            "browser site must expose the complete ICommDlgBrowser3 contract");
        if (browser3Pointer != IntPtr.Zero)
        {
            var table = Marshal.ReadIntPtr(browser3Pointer);
            var getFlags = Marshal.GetDelegateForFunctionPointer<NativeGetViewFlags>(Marshal.ReadIntPtr(table, 8 * IntPtr.Size));
            var column = Marshal.GetDelegateForFunctionPointer<NativeColumnClicked>(Marshal.ReadIntPtr(table, 9 * IntPtr.Size));
            var filter = Marshal.GetDelegateForFunctionPointer<NativeCurrentFilter>(Marshal.ReadIntPtr(table, 10 * IntPtr.Size));
            var preView = Marshal.GetDelegateForFunctionPointer<NativePreViewCreated>(Marshal.ReadIntPtr(table, 11 * IntPtr.Size));
            var buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt16(buffer, 0, 123);
                Marshal.WriteInt16(buffer, 2, 456);
                Check(getFlags(browser3Pointer, out var flags3) == 0 && flags3 == 0x10 &&
                    column(browser3Pointer, IntPtr.Zero, 0) == 0 && preView(browser3Pointer, IntPtr.Zero) == 0,
                    "ICommDlgBrowser3 native slots preserve asynchronous search flags and callback ABI");
                Check(filter(browser3Pointer, buffer, 1) == 0 && Marshal.ReadInt16(buffer) == 0 &&
                    Marshal.ReadInt16(buffer, 2) == 456, "native current filter is empty and bounded to its buffer");
                Check(filter(browser3Pointer, IntPtr.Zero, 1) < 0 && filter(browser3Pointer, buffer, 0) < 0 &&
                    filter(browser3Pointer, buffer, -1) < 0, "native filter rejects invalid output buffers");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    finally { if (browser3Pointer != IntPtr.Zero) Marshal.Release(browser3Pointer); }
    var baseServiceResult = browserSite.QueryService(ref serviceId, ref browserId, out var browserPointer);
    try { Check(baseServiceResult == 0 && browserPointer != IntPtr.Zero,
                "browser site also exposes the base dialog-browser interface"); }
    finally { if (browserPointer != IntPtr.Zero) Marshal.Release(browserPointer); }
    Exception? siteFailure = null;
    var siteThread = new Thread(() =>
    {
        object? shellBrowser = null;
        try
        {
            shellBrowser = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB"), true)!);
            ShellBrowserSite.SetSite(shellBrowser!, new ShellBrowserSite());
            var providerId = typeof(IShellServiceProvider).GUID;
            var getSiteResult = ((ITestObjectWithSite)shellBrowser!).GetSite(ref providerId, out var provider);
            try { Check(getSiteResult == 0 && provider != IntPtr.Zero,
                        "headless ExplorerBrowser retains the background-search service site"); }
            finally { if (provider != IntPtr.Zero) Marshal.Release(provider); }
        }
        catch (Exception ex) { siteFailure = ex; }
        finally
        {
            if (shellBrowser is not null)
            {
                ShellBrowserSite.SetSite(shellBrowser, null);
                Marshal.ReleaseComObject(shellBrowser);
            }
        }
    });
    siteThread.SetApartmentState(ApartmentState.STA);
    siteThread.Start();
    siteThread.Join();
    if (siteFailure is not null) throw siteFailure;
    var observedDirectory = Path.Combine(root, "new-folder-observation");
    Directory.CreateDirectory(observedDirectory);
    using (var observation = FolderCreationObservation.TryStart(observedDirectory)
           ?? throw new Exception("local directory observation could not start"))
    {
        var createdOne = Path.Combine(observedDirectory, "one-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(createdOne);
        Check(SpinWait.SpinUntil(() => observation.Snapshot().Directories.Contains(createdOne),
                  TimeSpan.FromSeconds(3)),
              "new directory is observed without enumerating the parent");
        var createdFile = Path.Combine(observedDirectory, "file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(createdFile, "sample");
        Check(!SpinWait.SpinUntil(() => observation.Snapshot().Directories.Count > 1,
                  TimeSpan.FromMilliseconds(400)),
              "a new file is not mistaken for the folder to rename");
        File.Delete(createdFile);
        var createdTwo = Path.Combine(observedDirectory, "two-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(createdTwo);
        Check(SpinWait.SpinUntil(() => observation.Snapshot().Directories.Count >= 2,
                  TimeSpan.FromSeconds(3)),
              "concurrent creations remain ambiguous rather than choosing the wrong folder");
        Directory.Delete(createdOne);
        Directory.Delete(createdTwo);
    }
    Check(FolderCreationObservation.TryStart(@"\\offline\share\folder") is null,
          "UNC path skips the extra directory watcher");
    store.Save(state);
    var loaded = store.Load() ?? throw new Exception("INI did not load");
    Check(store.LoadWarning is null, "complete INI needs no recovery");
    Check(loaded.Layout == 2 && loaded.DefaultViewMode == 3, "global settings");
    Check(loaded.Panes[0].Count == 2, "two tabs with same path");
    Check(loaded.Panes[0][0].Path == first.Path, "Unicode path with equals sign");
    Check(loaded.Panes[0][0].View.SortColumns[0] == first.View.SortColumns[0], "sort key and direction");
    Check(loaded.Panes[0][0].View.GroupKey == groupKey && !loaded.Panes[0][0].View.GroupAscending, "group key and direction");
    Check(loaded.Panes[0][1].View.Mode == 6 && loaded.Panes[0][1].View.GroupKey is null, "independent tab state");
    var activeSelection = state with { ActivePane = 1, ActiveTabIndices = [1, 0, 0, 0] };
    var selectedProfile = SessionStore.Render(activeSelection);
    var selectedSession = SessionStore.Parse(selectedProfile) ?? throw new Exception("active tabs did not parse");
    Check(selectedSession.ActivePane == 1 && selectedSession.ActiveTabIndices?.SequenceEqual([1, 0, 0, 0]) == true,
        "active pane and displayed tab positions round-trip in the portable session");
    var oldSelectionProfile = selectedProfile.Replace("ActivePane=1" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal).Replace("ActiveTab=1" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal).Replace("ActiveTab=0" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal);
    var oldSelectionIni = Path.Combine(root, "old-selection.ini");
    File.WriteAllText(oldSelectionIni, oldSelectionProfile);
    var oldSelectionStore = new SessionStore(oldSelectionIni, Path.Combine(root, "missing-legacy.json"));
    var oldSelection = oldSelectionStore.Load() ?? throw new Exception("older session format did not load");
    Check(oldSelectionStore.LoadWarning is null && oldSelection.ActivePane == 0 &&
          oldSelection.ActiveTabIndices?.All(index => index == 0) == true,
        "older session without selection fields remains complete and selects first tabs");
    var badSelectionIni = Path.Combine(root, "bad-selection.ini");
    File.WriteAllText(badSelectionIni,
        selectedProfile.Replace("ActiveTab=1", "ActiveTab=99", StringComparison.Ordinal));
    var badSelectionStore = new SessionStore(badSelectionIni, Path.Combine(root, "missing-legacy.json"));
    var selectionRecovered = badSelectionStore.Load() ?? throw new Exception("damaged active tab did not recover");
    Check(selectionRecovered.Panes[0].Count == 2 && selectionRecovered.ActiveTabIndices?[0] == 0 &&
          badSelectionStore.LoadWarning is not null &&
          Directory.GetFiles(root, "bad-selection.ini.invalid-*.bak").Length == 1,
        "invalid active tab falls back without losing tabs and backs up the source INI");
    var lastWrite = File.GetLastWriteTimeUtc(ini);
    store.Save(state);
    Check(File.GetLastWriteTimeUtc(ini) == lastWrite, "unchanged settings avoid write");

    var conflictIni = Path.Combine(root, "conflict.ini");
    var conflictStore = new SessionStore(conflictIni, Path.Combine(root, "missing-legacy.json"));
    conflictStore.Save(state);
    conflictStore.Load();
    var externalEdit = File.ReadAllText(conflictIni) + "; changed by another instance" + Environment.NewLine;
    File.WriteAllText(conflictIni, externalEdit);
    try
    {
        conflictStore.Save(state with { Layout = 3 });
        throw new Exception("external session edit was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(conflictIni) == externalEdit,
        "external session edits remain untouched after a stale save");

    var raceIni = Path.Combine(root, "race.ini");
    using var raceStart = new Barrier(2);
    Task<bool> RaceSave(int layout) => Task.Run(() =>
    {
        var instance = new SessionStore(raceIni, Path.Combine(root, "missing-legacy.json"));
        Check(instance.Load() is null && raceStart.SignalAndWait(TimeSpan.FromSeconds(5)),
            "both instances load the same absent session before writing");
        try { instance.Save(state with { Layout = layout }); return true; }
        catch (IOException) { return false; }
    });
    var firstRace = RaceSave(1);
    var secondRace = RaceSave(2);
    Task.WaitAll(firstRace, secondRace);
    var raceState = SessionStore.Parse(File.ReadAllText(raceIni));
    Check(firstRace.Result != secondRace.Result && raceState is not null &&
          raceState.Layout == (firstRace.Result ? 1 : 2),
          "simultaneous instances produce one intact session without stale overwrite");

    var recoveryIni = Path.Combine(root, "recovery.ini");
    var recoveryStore = new SessionStore(recoveryIni, Path.Combine(root, "missing-legacy.json"));
    var completeProfile = SessionStore.Render(state);
    var truncatedProfile = completeProfile[..completeProfile.IndexOf("[Pane0.Tab1]", StringComparison.Ordinal)];
    File.WriteAllText(recoveryIni, truncatedProfile);
    var recovered = recoveryStore.Load() ?? throw new Exception("truncated INI did not recover");
    Check(recovered.Panes[0].Count == 1 && recovered.Panes[0][0].Path == first.Path,
        "truncated INI preserves readable tab");
    Check(recoveryStore.LoadWarning is not null &&
          Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 1,
        "truncated INI is reported and backed up");
    recoveryStore.Save(recovered);
    Check(SessionStore.Parse(File.ReadAllText(recoveryIni))?.Panes[0].Count == 1,
        "recovered INI can be saved cleanly");

    File.WriteAllText(recoveryIni, completeProfile.Replace("TabCount=2", "TabCount=0", StringComparison.Ordinal));
    var countRecovered = recoveryStore.Load() ?? throw new Exception("wrong tab count did not recover");
    Check(countRecovered.Panes[0].Count == 2, "orphaned tab sections survive damaged tab count");
    Check(Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 2,
        "damaged tab count is backed up");

    var headerlessProfile = completeProfile[completeProfile.IndexOf("[Pane0]", StringComparison.Ordinal)..];
    File.WriteAllText(recoveryIni, headerlessProfile);
    var headerlessRecovered = recoveryStore.Load() ?? throw new Exception("missing General section did not recover");
    Check(headerlessRecovered.Layout == 4 && headerlessRecovered.Panes[0].Count == 2,
        "missing General section falls back to layout default and preserves tabs");
    Check(Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 3,
        "missing General section is backed up");

    var sortRecoveryIni = Path.Combine(root, "sort-recovery.ini");
    var staleSortCountProfile = completeProfile.Replace("SortCount=1", "SortCount=0", StringComparison.Ordinal);
    File.WriteAllText(sortRecoveryIni, staleSortCountProfile);
    var sortRecoveryStore = new SessionStore(sortRecoveryIni, Path.Combine(root, "missing-legacy.json"));
    var sortRecovered = sortRecoveryStore.Load() ?? throw new Exception("stale sort count did not recover");
    Check(sortRecovered.Panes[0][0].View.SortColumns.SequenceEqual(first.View.SortColumns),
        "orphaned sort column survives damaged sort count");
    Check(sortRecoveryStore.LoadWarning is not null &&
          Directory.GetFiles(root, "sort-recovery.ini.invalid-*.bak").Length == 1,
        "damaged sort count is reported and backed up");
    sortRecoveryStore.Save(sortRecovered);
    Check(SessionStore.Parse(File.ReadAllText(sortRecoveryIni))?.Panes[0][0].View.SortColumns
        .SequenceEqual(first.View.SortColumns) == true, "recovered sort remains after saving");

    File.Delete(ini);
    File.WriteAllText(legacy, JsonSerializer.Serialize(new { Layout = 4, Panes = new[]
    {
        new[] { @"C:\old" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()
    } }));
    var migrated = store.Load() ?? throw new Exception("legacy session did not migrate");
    Check(migrated.Panes[0][0].Path == @"C:\old" && migrated.Panes[0][0].View.Mode == 4, "legacy paths and default view");
    Check(File.Exists(ini) && File.Exists(legacy), "migration preserves legacy file");

    File.WriteAllText(ini, "[invalid]\nVersion=broken\n");
    Check(store.Load() is null, "invalid INI falls back");
    store.Save(state);
    var backups = Directory.GetFiles(root, "ExplorerDirs.ini.invalid-*.bak");
    Check(backups.Length == 1 && File.ReadAllText(backups[0]).Contains("Version=broken"),
        "invalid INI is preserved before overwrite");

    var futureIni = Path.Combine(root, "future.ini");
    var futureProfile = completeProfile.Replace("FormatVersion=10", "FormatVersion=11", StringComparison.Ordinal);
    File.WriteAllText(futureIni, futureProfile);
    var futureStore = new SessionStore(futureIni, Path.Combine(root, "missing-legacy.json"));
    Check(futureStore.Load() is null && futureStore.LoadWarning is not null,
        "newer format is not interpreted as this version");
    try
    {
        futureStore.Save(state);
        throw new Exception("newer format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(futureIni) == futureProfile, "newer format stays untouched");

    var duplicateFutureIni = Path.Combine(root, "duplicate-future.ini");
    var duplicateFutureProfile = completeProfile.Replace("FormatVersion=10",
        "FormatVersion=11" + Environment.NewLine + "FormatVersion=10", StringComparison.Ordinal);
    File.WriteAllText(duplicateFutureIni, duplicateFutureProfile);
    var duplicateFutureStore = new SessionStore(duplicateFutureIni, Path.Combine(root, "missing-legacy.json"));
    Check(duplicateFutureStore.Load() is null && duplicateFutureStore.LoadWarning is not null,
        "a newer duplicate format version must not be interpreted as version one");
    try
    {
        duplicateFutureStore.Save(state);
        throw new Exception("duplicated newer format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(duplicateFutureIni) == duplicateFutureProfile,
        "duplicated newer format stays untouched");

    var crashLogs = new CrashLog(Path.Combine(root, "crash-logs"));
    Exception simulatedCrash;
    try { throw new InvalidOperationException(@"PRIVATE_PATH_C:\Users\marvin\secret.txt"); }
    catch (Exception ex) { simulatedCrash = ex; }
    var crashFile = crashLogs.TryWrite(CrashEventKind.DispatcherUnhandled, simulatedCrash);
    Check(crashFile is not null && File.Exists(crashFile), "managed crash report is written");
    var report = File.ReadAllText(crashFile!);
    Check(report.Contains("InvalidOperationException", StringComparison.Ordinal) &&
          report.Contains("DispatcherUnhandled", StringComparison.Ordinal),
          "crash report retains diagnostic event and exception type");
    Check(!report.Contains("PRIVATE_PATH", StringComparison.Ordinal) &&
          !report.Contains(@"C:\Users\marvin", StringComparison.Ordinal),
          "crash report omits exception messages and private paths");
    var aggregateCrashFile = crashLogs.TryWrite(CrashEventKind.UnobservedTask,
        new AggregateException(new InvalidOperationException("first private error"),
            new ArgumentException("second private error")));
    var aggregateReport = aggregateCrashFile is null ? string.Empty : File.ReadAllText(aggregateCrashFile);
    Check(aggregateCrashFile is not null &&
          aggregateReport.Contains("ArgumentException", StringComparison.Ordinal) &&
          !aggregateReport.Contains("first private error", StringComparison.Ordinal) &&
          !aggregateReport.Contains("second private error", StringComparison.Ordinal),
          "crash report retains sibling exceptions from an unobserved task aggregate");
    var boundedCrashFile = crashLogs.TryWrite(CrashEventKind.UnobservedTask,
        new AggregateException(Enumerable.Range(0, 8)
            .Select(_ => new InvalidOperationException()).Cast<Exception>()));
    Check(boundedCrashFile is not null &&
          JsonDocument.Parse(File.ReadAllText(boundedCrashFile)).RootElement
              .GetProperty("Exceptions").GetArrayLength() == 4,
          "crash report bounds aggregate exception details to four entries");
    Check(crashLogs.TryWrite(CrashEventKind.AppDomainUnhandled, simulatedCrash) is null &&
          Directory.GetFiles(Path.Combine(root, "crash-logs"), "*.json").Length == 3,
          "the same managed exception is logged once");
    var interruptedCrashDirectory = Path.Combine(root, "interrupted-crash-logs");
    var interruptedCrashLog = new CrashLog(interruptedCrashDirectory, (path, _) =>
    {
        File.WriteAllText(path, "{partial");
        throw new IOException("simulated termination during report write");
    });
    Check(interruptedCrashLog.TryWrite(CrashEventKind.DispatcherUnhandled, new InvalidOperationException()) is null &&
          Directory.GetFiles(interruptedCrashDirectory, "*.json").Length == 0 &&
          Directory.GetFiles(interruptedCrashDirectory, "*.tmp").Length == 0,
          "an interrupted crash report write leaves neither a partial report nor a temporary file");
    var staleCrashDirectory = Path.Combine(root, "stale-crash-logs");
    Directory.CreateDirectory(staleCrashDirectory);
    var staleTemporaryReport = Path.Combine(staleCrashDirectory, "managed-stale.json.tmp");
    var activeTemporaryReport = Path.Combine(staleCrashDirectory, "managed-active.json.tmp");
    File.WriteAllText(staleTemporaryReport, "{stale");
    File.WriteAllText(activeTemporaryReport, "{active");
    File.SetLastWriteTimeUtc(staleTemporaryReport, DateTime.UtcNow.AddDays(-2));
    _ = new CrashLog(staleCrashDirectory);
    Check(!File.Exists(staleTemporaryReport) && File.Exists(activeTemporaryReport),
          "startup reclaims stale crash-report temporaries without deleting a recent report in progress");

    var runMarkerDirectory = Path.Combine(root, "run-marker-smoke");
    Directory.CreateDirectory(runMarkerDirectory);
    foreach (var old in Directory.GetFiles(runMarkerDirectory, "*.json")) File.Delete(old);
    File.WriteAllText(Path.Combine(runMarkerDirectory, "run-dead.json"),
        "{\"ProcessId\":2147483647,\"StartedUtcTicks\":1,\"AppVersion\":\"test\"}");
    using (var firstRun = RunMarkerTracker.TryStart(runMarkerDirectory)
           ?? throw new Exception("run marker could not start"))
    {
        Check(firstRun.PriorUncleanExit && File.Exists(Path.Combine(runMarkerDirectory, "abrupt-dead.json")),
              "terminated process marker becomes an abrupt-exit report");
        using var secondRun = RunMarkerTracker.TryStart(runMarkerDirectory)
            ?? throw new Exception("second run marker could not start");
        Check(!secondRun.PriorUncleanExit &&
              Directory.GetFiles(runMarkerDirectory, "run-*.json").Length == 2,
              "another live instance is not mistaken for a crash");
    }
    Check(Directory.GetFiles(runMarkerDirectory, "run-*.json").Length == 0 &&
          Directory.GetFiles(runMarkerDirectory, "abrupt-*.json").Length == 1,
          "clean exits remove only their own live markers");
    var abruptReport = File.ReadAllText(Path.Combine(runMarkerDirectory, "abrupt-dead.json"));
    Check(!abruptReport.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase),
          "abrupt-exit report contains no browsing path");
    var reusedPidDirectory = Path.Combine(root, "run-marker-reused-pid");
    if (Directory.Exists(reusedPidDirectory)) Directory.Delete(reusedPidDirectory, recursive: true);
    Directory.CreateDirectory(reusedPidDirectory);
    using (var currentProcess = Process.GetCurrentProcess())
    {
        var previousStartTicks = currentProcess.StartTime.ToUniversalTime().Ticks + TimeSpan.TicksPerDay;
        File.WriteAllText(Path.Combine(reusedPidDirectory, "run-reused.json"), JsonSerializer.Serialize(new
        {
            ProcessId = currentProcess.Id,
            StartedUtcTicks = previousStartTicks,
            AppVersion = "older-process"
        }));
        using var reusedPid = RunMarkerTracker.TryStart(reusedPidDirectory)
            ?? throw new Exception("reused-PID run marker could not start");
        Check(reusedPid.PriorUncleanExit && File.Exists(Path.Combine(reusedPidDirectory, "abrupt-reused.json")),
              "a reused process ID with a different start time is recognized as an ended run");
    }
    var corruptMarker = Path.Combine(runMarkerDirectory, "run-corrupt.json");
    File.WriteAllText(corruptMarker, "{incomplete");
    using (var recoveredRun = RunMarkerTracker.TryStart(runMarkerDirectory)
           ?? throw new Exception("run marker recovery could not start"))
    {
        Check(!recoveredRun.PriorUncleanExit && File.ReadAllText(corruptMarker) == "{incomplete",
              "unreadable run evidence remains untouched without false crash attribution");
    }
    var collisionDirectory = Path.Combine(root, "run-marker-collision");
    Directory.CreateDirectory(collisionDirectory);
    var previousAbrupt = Path.Combine(collisionDirectory, "abrupt-dead.json");
    File.WriteAllText(previousAbrupt, "older report");
    File.WriteAllText(Path.Combine(collisionDirectory, "run-dead.json"),
        "{\"ProcessId\":2147483647,\"StartedUtcTicks\":1,\"AppVersion\":\"test\"}");
    using (var recoveredCollision = RunMarkerTracker.TryStart(collisionDirectory)
           ?? throw new Exception("run marker collision recovery could not start"))
    {
        Check(recoveredCollision.PriorUncleanExit && File.ReadAllText(previousAbrupt) == "older report" &&
              !File.Exists(Path.Combine(collisionDirectory, "run-dead.json")) &&
              Directory.GetFiles(collisionDirectory, "abrupt-dead-*.json").Length == 1,
              "an existing abrupt report is preserved while the new ended run is archived");
    }
    using (var nextRun = RunMarkerTracker.TryStart(collisionDirectory)
           ?? throw new Exception("next run marker could not start"))
    {
        Check(!nextRun.PriorUncleanExit,
              "an archived marker does not cause another unclean-exit warning");
    }

    var machineVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["TOOLS"] = new(@"C:\new-tools", false),
        ["Path"] = new(@"C:\Windows\System32;%TOOLS%", true),
        ["LINK"] = new(@"%TOOLS%\bin", true),
        ["PLAIN"] = new(@"%TOOLS%\literal", false),
        ["MISSING_LINK"] = new(@"%DELETED%\bin", true)
    };
    var userVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = new(@"%PATH%;C:\user-bin", true),
        ["USER_TOOL"] = new(@"%TOOLS%\user", true)
    };
    var oldProcess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TOOLS"] = @"C:\old-tools",
        ["DELETED"] = @"C:\stale",
        ["USERPROFILE"] = @"C:\Users\example"
    };
    var refreshPlan = EnvironmentRefresh.BuildPlan(machineVariables, userVariables, oldProcess,
        ["TOOLS", "Path", "DELETED"]);
    Check(refreshPlan.Values["LINK"] == @"C:\new-tools\bin" &&
          refreshPlan.Values["USER_TOOL"] == @"C:\new-tools\user",
          "expandable values use the fresh persisted snapshot");
    Check(refreshPlan.Values["Path"] == @"C:\Windows\System32;C:\new-tools;C:\user-bin",
          "machine and user PATH combine without stale or duplicate machine segments");
    Check(refreshPlan.Values["PLAIN"] == @"%TOOLS%\literal" &&
          refreshPlan.Values["MISSING_LINK"] == @"%DELETED%\bin",
          "plain registry strings stay literal and deleted names do not use stale process values");
    Check(refreshPlan.Removed.Contains("DELETED", StringComparer.OrdinalIgnoreCase),
          "deleted persisted variable is removed from the process");
    var normalUserVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = new(@"C:\user-bin", false),
        ["FROM_PROCESS"] = new(@"%USERPROFILE%\Documents", true)
    };
    var normalPathPlan = EnvironmentRefresh.BuildPlan(machineVariables, normalUserVariables,
        oldProcess, ["TOOLS", "Path", "DELETED"]);
    Check(normalPathPlan.Values["Path"] == @"C:\Windows\System32;C:\new-tools;C:\user-bin" &&
          normalPathPlan.Values["FROM_PROCESS"] == @"C:\Users\example\Documents",
          "ordinary user PATH appends and process-only references remain available");

    var fakeEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["STALE"] = "old-delete", ["UPDATED"] = "old-value", ["FAIL"] = null
    };
    var failingPlan = new EnvironmentRefreshPlan(
        new Dictionary<string, string> { ["UPDATED"] = "new-value", ["FAIL"] = "rejected" },
        ["STALE"]);
    try
    {
        EnvironmentRefresh.ApplyPlan(failingPlan,
            name => fakeEnvironment.GetValueOrDefault(name),
            (name, value) =>
            {
                if (name == "FAIL") throw new IOException("simulated process environment write failure");
                fakeEnvironment[name] = value;
            });
        throw new Exception("partial environment refresh was reported as successful");
    }
    catch (IOException) { }
    Check(fakeEnvironment["STALE"] == "old-delete" && fakeEnvironment["UPDATED"] == "old-value" &&
          fakeEnvironment["FAIL"] is null,
          "failed environment update restores earlier deletions and assignments");
    var rollbackFailureEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["UPDATED"] = "old-value"
    };
    EnvironmentRefreshRollbackException? rollbackFailure = null;
    var updatedWrites = 0;
    try
    {
        EnvironmentRefresh.ApplyPlan(failingPlan,
            name => rollbackFailureEnvironment.GetValueOrDefault(name),
            (name, value) =>
            {
                if (name == "FAIL" || name == "UPDATED" && ++updatedWrites > 1)
                    throw new IOException("simulated process environment write failure");
                rollbackFailureEnvironment[name] = value;
            });
        throw new Exception("rollback failure was reported as successful");
    }
    catch (EnvironmentRefreshRollbackException error) { rollbackFailure = error; }
    Check(rollbackFailure?.UnrestoredNames.Contains("UPDATED", StringComparer.OrdinalIgnoreCase) == true,
          "a failed rollback reports the process variable that may remain stale");
    var retryPreviousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    EnvironmentRefresh.RememberUnrestoredNames(retryPreviousNames, rollbackFailure!.UnrestoredNames);
    var retryPlan = EnvironmentRefresh.BuildPlan(
        new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["UPDATED"] = "new-value" },
        retryPreviousNames);
    Check(retryPlan.Removed.Contains("UPDATED", StringComparer.OrdinalIgnoreCase),
          "the next refresh removes a variable whose rollback failed after its registry definition vanished");
    var rejectedOversize = new EnvironmentRefreshPlan(
        new Dictionary<string, string> { ["UPDATED"] = "new-value", ["FAIL"] = new string('x', 32767) },
        ["STALE"]);
    try
    {
        EnvironmentRefresh.ApplyPlan(rejectedOversize,
            name => fakeEnvironment.GetValueOrDefault(name),
            (name, value) => fakeEnvironment[name] = value);
        throw new Exception("oversized environment value was accepted");
    }
    catch (ArgumentException) { }
    Check(fakeEnvironment["STALE"] == "old-delete" && fakeEnvironment["UPDATED"] == "old-value",
          "invalid environment plan is rejected before changing any value");

    // This affects only the short-lived console test process; no persistent registry values are changed.
    Check(new EnvironmentRefresh().Reload() > 0, "live Windows environment snapshot can be refreshed");

    ProcessStartInfo? commandInfo = null;
    using var commandStarted = new ManualResetEventSlim();
    using var releaseCommand = new ManualResetEventSlim();
    var commandLaunch = TerminalLauncher.OpenAsync(root, CancellationToken.None, info =>
    {
        commandInfo = info;
        commandStarted.Set();
        releaseCommand.Wait();
    });
    try
    {
        Check(commandStarted.Wait(TimeSpan.FromSeconds(3)) && !commandLaunch.IsCompleted,
              "slow command launch does not hold the calling thread");
    }
    finally { releaseCommand.Set(); }
    commandLaunch.GetAwaiter().GetResult();
    Check(commandInfo?.FileName == Path.Combine(Environment.SystemDirectory, "cmd.exe") &&
          Path.IsPathFullyQualified(commandInfo.FileName) && File.Exists(commandInfo.FileName) &&
          commandInfo.WorkingDirectory == root && !commandInfo.UseShellExecute,
          "terminal launch uses the system command interpreter and requested directory");
    const string childVariable = "EXPLORER_DIRS_CHILD_SMOKE";
    const string removedChildVariable = "EXPLORER_DIRS_REMOVED_SMOKE";
    var oldChildValue = Environment.GetEnvironmentVariable(childVariable, EnvironmentVariableTarget.Process);
    var oldRemovedValue = Environment.GetEnvironmentVariable(removedChildVariable,
        EnvironmentVariableTarget.Process);
    var oldProcessPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process);
    var expectedChildValue = "updated-" + Guid.NewGuid().ToString("N");
    var expectedPathEntry = Path.Combine(root, "refreshed-path-marker");
    var refreshedPath = string.IsNullOrEmpty(oldProcessPath)
        ? expectedPathEntry : oldProcessPath + Path.PathSeparator + expectedPathEntry;
    try
    {
        Environment.SetEnvironmentVariable(removedChildVariable, "stale",
            EnvironmentVariableTarget.Process);
        var childPlan = EnvironmentRefresh.BuildPlan(
            new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
            {
                [childVariable] = new(expectedChildValue, false),
                ["Path"] = new(refreshedPath, false)
            },
            new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [removedChildVariable]);
        EnvironmentRefresh.ApplyPlan(childPlan);
        Check(Environment.GetEnvironmentVariable(removedChildVariable,
                  EnvironmentVariableTarget.Process) is null,
              "applying the environment plan removes a deleted persistent variable");
        string? childOutput = null;
        TerminalLauncher.OpenAsync(root, CancellationToken.None, info =>
        {
            info.Arguments = "/d /c echo %EXPLORER_DIRS_CHILD_SMOKE%";
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            using var child = Process.Start(info) ?? throw new Exception("hidden command did not start");
            if (!child.WaitForExit(5000))
            {
                child.Kill();
                throw new Exception("hidden command did not exit in time");
            }
            if (child.ExitCode != 0) throw new Exception("hidden command failed");
            childOutput = child.StandardOutput.ReadToEnd().Trim();
        }).GetAwaiter().GetResult();
        Check(childOutput == expectedChildValue,
              "a new command process inherits a freshly applied environment plan");
        TerminalLauncher.OpenAsync(root, CancellationToken.None, info =>
        {
            info.Arguments = "/d /c echo %PATH%";
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            using var child = Process.Start(info) ?? throw new Exception("hidden PATH command did not start");
            if (!child.WaitForExit(5000))
            {
                child.Kill();
                throw new Exception("hidden PATH command did not exit in time");
            }
            if (child.ExitCode != 0) throw new Exception("hidden PATH command failed");
            childOutput = child.StandardOutput.ReadToEnd().Trim();
        }).GetAwaiter().GetResult();
        Check(childOutput?.Split(Path.PathSeparator).Contains(expectedPathEntry, StringComparer.OrdinalIgnoreCase) == true,
              "a new command process inherits the refreshed PATH");
    }
    finally
    {
        Environment.SetEnvironmentVariable(childVariable, oldChildValue,
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(removedChildVariable, oldRemovedValue,
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("Path", oldProcessPath, EnvironmentVariableTarget.Process);
    }
    using var canceledCommand = new CancellationTokenSource();
    canceledCommand.Cancel();
    var launchAfterCancel = false;
    try
    {
        TerminalLauncher.OpenAsync(root, canceledCommand.Token,
            _ => launchAfterCancel = true).GetAwaiter().GetResult();
        throw new Exception("canceled terminal request unexpectedly started");
    }
    catch (OperationCanceledException) { }
    Check(!launchAfterCancel, "a canceled window cannot start a delayed terminal");

    Check(DirectoryProbe.Check(@"C:\allowed", _ => FileAttributes.Directory, _ => false) ==
          DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(@"C:\ordinary-file", _ => FileAttributes.Archive, _ => false) ==
          DirectoryProbeStatus.Unavailable,
          "directory preflight distinguishes a directory from an ordinary file");
    var probeFile = Path.Combine(root, "navigation-probe.txt");
    File.WriteAllText(probeFile, "probe");
    Check(DirectoryProbe.Check(root) == DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(probeFile) == DirectoryProbeStatus.Unavailable &&
          DirectoryProbe.Check(Path.Combine(root, "missing-directory")) == DirectoryProbeStatus.Unavailable,
          "real filesystem directory, file, and missing path classification");
    Check(DirectoryProbe.Check(@"C:\protected", _ => throw new UnauthorizedAccessException(), _ => false) ==
          DirectoryProbeStatus.AccessDenied &&
          DirectoryProbe.Check(@"C:\protected", _ => throw new System.Security.SecurityException(), _ => false) ==
          DirectoryProbeStatus.AccessDenied,
          "access denial is deferred to the Shell rather than misreported as missing");
    Check(DirectoryProbe.Check(@"\\server\share\", _ => throw new IOException(), _ => true) ==
          DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(@"\\server\offline\", _ => throw new IOException(), _ => false) ==
          DirectoryProbeStatus.Unavailable &&
          DirectoryProbe.Check(@"C:\missing", _ => throw new DirectoryNotFoundException(),
              _ => throw new Exception("local paths must not get a second filesystem probe")) ==
          DirectoryProbeStatus.Unavailable,
          "UNC share roots keep the existing Directory.Exists fallback");
    var navigationGate = new DirectoryNavigationGate();
    using var oldProbeStarted = new ManualResetEventSlim();
    using var releaseOldProbe = new ManualResetEventSlim();
    var oldProbe = navigationGate.ProbeAsync(@"\\offline\share", _ =>
    {
        oldProbeStarted.Set();
        releaseOldProbe.Wait();
        return DirectoryProbeStatus.Unavailable;
    });
    try
    {
        Check(oldProbeStarted.Wait(TimeSpan.FromSeconds(3)), "first directory probe starts in background");
        var newProbe = navigationGate.ProbeAsync(@"C:\ready", _ => DirectoryProbeStatus.Directory).GetAwaiter().GetResult();
        Check(navigationGate.IsCurrent(newProbe) && newProbe.CanNavigate,
              "new navigation can finish while older probe waits");
        var deniedProbe = navigationGate.ProbeAsync(@"C:\protected", _ => DirectoryProbeStatus.AccessDenied)
            .GetAwaiter().GetResult();
        Check(navigationGate.IsCurrent(deniedProbe) && deniedProbe.CanNavigate,
              "access denied reaches Shell navigation for its own error handling");
    }
    finally { releaseOldProbe.Set(); }
    Check(!navigationGate.IsCurrent(oldProbe.GetAwaiter().GetResult()),
        "late result from an older directory probe cannot replace the newer navigation");

    Check(TabReorder.Move(["A", "B", "C", "D"], 0, 2, false).SequenceEqual(["B", "A", "C", "D"]),
        "dragging a first tab before a later tab uses the correct insertion index");
    Check(TabReorder.Move(["A", "B", "C", "D"], 0, 2, true).SequenceEqual(["B", "C", "A", "D"]),
        "dragging a first tab after a later tab uses the correct insertion index");
    Check(TabReorder.Move(["A", "B", "C", "D"], 3, 1, false).SequenceEqual(["A", "D", "B", "C"]),
        "dragging a last tab to the left preserves other tab order");
    Check(TabReorder.Move(["A", "B", "C", "D"], 2, 2, true).SequenceEqual(["A", "B", "C", "D"]),
        "dropping on the same tab leaves order unchanged");
    Exception? layoutFailure = null;
    var layoutThread = new Thread(() =>
    {
        try
        {
            var panel = new TabHeaderPanel();
            var firstHeader = new Border { Width = 20, Height = 10 };
            var secondHeader = new Border { Width = 30, Height = 10 };
            panel.Children.Add(firstHeader);
            panel.Children.Add(secondHeader);
            TabHeaderPanel.SetOrder(firstHeader, 1);
            TabHeaderPanel.SetOrder(secondHeader, 0);
            panel.Measure(new Size(100, 10));
            panel.Arrange(new Rect(0, 0, 100, 10));
            Check(VisualTreeHelper.GetOffset(secondHeader).X == 0 &&
                  VisualTreeHelper.GetOffset(firstHeader).X == 30,
                  "tab headers arrange by display order");
            TabHeaderPanel.SetOrder(firstHeader, 0);
            TabHeaderPanel.SetOrder(secondHeader, 1);
            panel.Arrange(new Rect(0, 0, 100, 10));
            Check(VisualTreeHelper.GetOffset(firstHeader).X == 0 &&
                  VisualTreeHelper.GetOffset(secondHeader).X == 20 &&
                  ReferenceEquals(panel.Children[0], firstHeader),
                  "reordering headers leaves child controls attached");
            var contentHost = new PersistentTabHost();
            var firstContent = new Border();
            var secondContent = new Border();
            contentHost.Show(firstContent);
            contentHost.Show(secondContent);
            Check(contentHost.Children.Count == 2 &&
                  ReferenceEquals(VisualTreeHelper.GetParent(firstContent), contentHost) &&
                  firstContent.Visibility == Visibility.Collapsed &&
                  secondContent.Visibility == Visibility.Visible,
                  "switching tabs keeps the previous view in the visual tree");
            contentHost.Show(firstContent);
            Check(contentHost.Children.Count == 2 && firstContent.Visibility == Visibility.Visible &&
                  secondContent.Visibility == Visibility.Collapsed,
                  "returning to a tab reuses its existing view object");
            contentHost.Remove(secondContent);
            Check(contentHost.Children.Count == 1 &&
                  VisualTreeHelper.GetParent(secondContent) is null,
                  "closing a tab removes only its own view");
            var paneGrid = new PaneLayoutGrid();
            var panes = Enumerable.Range(0, 4).Select(_ => new Border()).ToArray();
            PaneLayoutSplitSmoke.Run();
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(4));
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(1));
            Check(paneGrid.Children.Count == 4 && panes[0].Visibility == Visibility.Visible &&
                  panes.Skip(1).All(pane => pane.Visibility == Visibility.Collapsed) &&
                  panes.All(pane => ReferenceEquals(VisualTreeHelper.GetParent(pane), paneGrid)),
                  "hiding panes keeps every pane in the visual tree");
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(4));
            Check(panes.All(pane => pane.Visibility == Visibility.Visible) &&
                  Grid.GetRow(panes[3]) == 2 && Grid.GetColumn(panes[3]) == 2,
                  "restoring four panes reuses their original controls");
        }
        catch (Exception ex) { layoutFailure = ex; }
    });
    layoutThread.SetApartmentState(ApartmentState.STA);
    layoutThread.Start();
    layoutThread.Join();
    if (layoutFailure is not null) throw layoutFailure;

    var driveCrumbs = BreadcrumbPath.Build(@"C:\Users\marvin");
    Check(driveCrumbs.Count == 3 && driveCrumbs[0] == new BreadcrumbSegment("C:", @"C:\") &&
          driveCrumbs[1] == new BreadcrumbSegment("Users", @"C:\Users") &&
          driveCrumbs[2] == new BreadcrumbSegment("marvin", @"C:\Users\marvin"),
          "drive breadcrumbs navigate to each directory level");
    var networkCrumbs = BreadcrumbPath.Build(@"\\server\share\docs\2026");
    Check(networkCrumbs.Count == 3 &&
          Path.TrimEndingDirectorySeparator(networkCrumbs[0].Path) == @"\\server\share" &&
          networkCrumbs[0].Label == @"\\server\share" &&
          networkCrumbs[2].Path == @"\\server\share\docs\2026",
          "UNC breadcrumbs keep the server and share as one root");
    Check(BreadcrumbPath.Build(@"C:\").Count == 1 &&
          BreadcrumbPath.Build(@"folder\child").Count == 1,
          "root and relative paths do not gain misleading extra segments");
    Check(BreadcrumbPath.NormalizeAddressInput("C:/Windows/System32") == @"C:\Windows\System32" &&
          BreadcrumbPath.NormalizeAddressInput(@"C:\\Windows\\System32\") == @"C:\Windows\System32\",
          "absolute drive paths accept forward and repeated backslashes");
    Check(BreadcrumbPath.NormalizeAddressInput("//server//share/folder") == @"\\server\share\folder" &&
          BreadcrumbPath.NormalizeAddressInput(@"\\server\\share\folder") == @"\\server\share\folder",
          "absolute UNC paths normalize separators without losing the share root");
    Check(BreadcrumbPath.NormalizeAddressInput("shell:RecycleBinFolder") == "shell:RecycleBinFolder" &&
          BreadcrumbPath.NormalizeAddressInput("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}") ==
              "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}" &&
          BreadcrumbPath.NormalizeAddressInput("https://example.invalid/a/b") == "https://example.invalid/a/b" &&
          BreadcrumbPath.NormalizeAddressInput("folder//child") == "folder//child",
          "Shell names, protocols and relative inputs stay outside filesystem path normalization");

    var addressStyleIni = Path.Combine(root, "AddressBar.ini");
    var addressStyleStore = new AddressBarStyleStore(addressStyleIni);
    Check(!addressStyleStore.Load() && addressStyleStore.LoadWarning is null,
          "a missing address-bar style keeps the current modern breadcrumb presentation");
    addressStyleStore.Save(true);
    Check(new AddressBarStyleStore(addressStyleIni).Load(),
          "the classic address-bar choice round-trips in its own portable preference");
    File.AppendAllText(addressStyleIni, "; edited elsewhere" + Environment.NewLine);
    try { addressStyleStore.Save(false); throw new Exception("external address-bar preference edit was overwritten"); }
    catch (IOException) { Check(File.ReadAllText(addressStyleIni).Contains("edited elsewhere", StringComparison.Ordinal),
          "an external address-bar preference edit is preserved"); }

    var uiIni = Path.Combine(root, "UiPreferences.ini");
    var uiStore = new UiPreferencesStore(uiIni);
    Check(!uiStore.Load() && uiStore.LoadWarning is null, "missing appearance profile uses normal spacing");
    uiStore.Save(true);
    Check(uiStore.Load() && uiStore.LoadWarning is null, "compact spacing round-trips in portable INI");
    var uiContent = File.ReadAllText(uiIni);
    File.AppendAllText(uiIni, "; edited elsewhere" + Environment.NewLine);
    try { uiStore.Save(false); throw new Exception("external appearance edit was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni).Contains("edited elsewhere", StringComparison.Ordinal),
        "external appearance changes remain untouched");
    File.WriteAllText(uiIni, uiContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
    Check(!uiStore.Load() && uiStore.LoadWarning is not null,
        "future appearance format is protected");
    try { uiStore.Save(false); throw new Exception("future appearance format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni).Contains("FormatVersion=2", StringComparison.Ordinal),
        "future appearance file stays untouched");
    const string hugeVersion = "999999999999999999999999999999999999";
    var hugeUiContent = uiContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(uiIni, hugeUiContent);
    Check(!uiStore.Load() && uiStore.LoadWarning is not null,
        "oversized future appearance version is protected even with a later version one");
    try { uiStore.Save(false); throw new Exception("oversized future appearance format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni) == hugeUiContent, "oversized future appearance file stays untouched");
    File.WriteAllText(uiIni, uiContent.Replace("Compact=true", "Compact=broken", StringComparison.Ordinal));
    Check(!uiStore.Load() && uiStore.LoadWarning is not null &&
          Directory.GetFiles(root, "UiPreferences.ini.invalid-*.bak").Length == 1,
          "invalid appearance file is backed up before recovery");
    uiStore.Save(false);
    Check(!uiStore.Load() && uiStore.LoadWarning is null,
          "recovered appearance settings can be saved cleanly");

    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.Run(root);
    ExplorerDirs.SettingsSmoke.NetworkDriveDialogsSmoke.Run();
    ExplorerDirs.SettingsSmoke.ThemePreferenceSmoke.CheckResolution();
    ExplorerDirs.SettingsSmoke.SizeDisplaySmoke.Run(root);

    var treeIni = Path.Combine(root, "NavigationTree.ini");
    var treeStore = new PortableBooleanPreference(treeIni, "NavigationTree", "Visible", "目录树");
    Check(!treeStore.Load(), "missing tree preference does not instantiate a tree by default");
    treeStore.Save(true);
    Check(treeStore.Load() && treeStore.LoadWarning is null &&
        File.ReadAllText(treeIni).Contains("Visible=true", StringComparison.Ordinal),
        "tree visibility round-trips in its independent portable schema");
    var treeContent = File.ReadAllText(treeIni);
    uiStore.Save(true);
    Check(File.ReadAllText(treeIni) == treeContent, "appearance save preserves independent tree preference");
    File.AppendAllText(treeIni, "; external tree edit");
    try { treeStore.Save(false); throw new Exception("external tree preference was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(treeIni).EndsWith("; external tree edit", StringComparison.Ordinal),
        "external tree preference changes are preserved");
    File.WriteAllText(treeIni, treeContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
    Check(!treeStore.Load() && treeStore.LoadWarning is not null, "future tree preference stays protected");
    try { treeStore.Save(false); throw new Exception("future tree preference was overwritten"); }
    catch (IOException) { }

    var pinsIni = Path.Combine(root, "PinnedTabs.ini");
    if (File.Exists(pinsIni)) File.Delete(pinsIni);
    foreach (var backup in Directory.GetFiles(root, "PinnedTabs.ini.invalid-*.bak")) File.Delete(backup);
    var pinsStore = new PinnedTabsStore(pinsIni);
    Check(pinsStore.Load().Count == 0, "missing pinned tabs profile starts empty");
    var pinned = new[] { new PinnedTabEntry(0, 1, second.Path) };
    pinsStore.Save(pinned);
    var loadedPins = pinsStore.Load();
    Check(loadedPins.SequenceEqual(pinned) &&
          PinnedTabsStore.MatchPane([first, second], loadedPins, 0).SequenceEqual([1]) &&
          !PinnedTabsStore.MatchPane([first, new TabSettings(@"C:\other", ViewState.Default())], loadedPins, 0).Any(),
          "pinned duplicate path matches its saved position and ignores a changed session slot");
    var pinsContent = File.ReadAllText(pinsIni);
    File.AppendAllText(pinsIni, "; changed by another instance" + Environment.NewLine);
    try { pinsStore.Save([]); throw new Exception("external pin edit was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(pinsIni).Contains("changed by another instance", StringComparison.Ordinal),
          "external pinned tab edits remain untouched");
    File.WriteAllText(pinsIni, pinsContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
    Check(pinsStore.Load().Count == 0 && pinsStore.LoadWarning is not null,
          "future pinned tab format is protected");
    try { pinsStore.Save([]); throw new Exception("future pinned tab format was overwritten"); }
    catch (IOException) { }
    var hugePinsContent = pinsContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(pinsIni, hugePinsContent);
    Check(pinsStore.Load().Count == 0 && pinsStore.LoadWarning is not null,
          "oversized future pinned tab version is protected even with a later version one");
    try { pinsStore.Save([]); throw new Exception("oversized future pinned tabs format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(pinsIni) == hugePinsContent, "oversized future pinned tabs file stays untouched");
    File.WriteAllText(pinsIni, pinsContent.Replace("Count=1", "Count=2", StringComparison.Ordinal));
    Check(pinsStore.Load().Count == 1 && pinsStore.LoadWarning is not null,
          "pinned tab recovery remains readable");
    Check(Directory.GetFiles(root, "PinnedTabs.ini.invalid-*.bak").Length == 1,
          "invalid pinned tab file is backed up before recovery");

    var favoritesIni = Path.Combine(root, "Favorites.ini");
    var favoritesStore = new FavoritesStore(favoritesIni);
    Check(favoritesStore.Load().Count == 0, "missing favorites profile starts empty");
    favoritesStore.Save([@"C:\项目=测试", @"\\server\share\tools", @"c:\项目=测试\"]);
    var favorites = favoritesStore.Load();
    Check(favoritesStore.LoadWarning is null && favorites.Count == 2 &&
          favorites[0] == @"C:\项目=测试" && favorites[1] == @"\\server\share\tools",
          "favorites round-trip Unicode, equals signs, UNC paths, and case-insensitive duplicates");
    var lockedFavoritesIni = Path.Combine(root, "locked-favorites.ini");
    var lockedFavorites = new FavoritesStore(lockedFavoritesIni);
    Check(lockedFavorites.Load().Count == 0, "locked favorites test starts without a file");
    using (var heldWrite = new Mutex(false, PortableIniWriter.MutexName(lockedFavoritesIni)))
    {
        Check(heldWrite.WaitOne(TimeSpan.FromSeconds(1)), "test holds favorites write gate");
        try
        {
            var blocked = Task.Run(() =>
            {
                try { lockedFavorites.Save([@"C:\blocked"]); return false; }
                catch (IOException) { return true; }
            }).GetAwaiter().GetResult();
            Check(blocked && !File.Exists(lockedFavoritesIni),
                "favorites writer does not bypass another instance's write gate");
        }
        finally { heldWrite.ReleaseMutex(); }
    }
    lockedFavorites.Save([@"C:\allowed"]);
    Check(lockedFavorites.Load().Single() == @"C:\allowed", "favorites save succeeds after write gate releases");
    var favoriteContent = File.ReadAllText(favoritesIni);
    File.AppendAllText(favoritesIni, "; edit in another instance" + Environment.NewLine);
    try
    {
        favoritesStore.Save([@"C:\new"]);
        throw new Exception("concurrent favorites edit was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni).Contains("edit in another instance", StringComparison.Ordinal),
          "concurrent favorites edit is not overwritten");
    File.WriteAllText(favoritesIni, favoriteContent.Replace("Count=2", "Count=3", StringComparison.Ordinal));
    var recoveredFavorites = favoritesStore.Load();
    Check(recoveredFavorites.Count == 2 && favoritesStore.LoadWarning is not null &&
          Directory.GetFiles(root, "Favorites.ini.invalid-*.bak").Length == 1,
          "damaged favorites count is backed up and readable entries survive");
    favoritesStore.Save(recoveredFavorites);
    var savedFavorites = favoritesStore.Load();
    Check(savedFavorites.Count == 2 && favoritesStore.LoadWarning is null,
          "recovered favorites can be saved");
    var futureFavorites = favoriteContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal);
    File.WriteAllText(favoritesIni, futureFavorites);
    Check(favoritesStore.Load().Count == 0 && favoritesStore.LoadWarning is not null,
          "newer favorites format is not interpreted");
    try
    {
        favoritesStore.Save([@"C:\new"]);
        throw new Exception("newer favorites format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni) == futureFavorites, "newer favorites profile stays untouched");
    var hugeFavorites = favoriteContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(favoritesIni, hugeFavorites);
    Check(favoritesStore.Load().Count == 0 && favoritesStore.LoadWarning is not null,
          "oversized future favorites version is protected even with a later version one");
    try { favoritesStore.Save([@"C:\new"]); throw new Exception("oversized future favorites was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni) == hugeFavorites, "oversized future favorites file stays untouched");
    Console.WriteLine("Settings smoke: PASS");
}
finally
{
    // The only recursive cleanup target is this fixed child of the test output directory.
    var resolved = Path.GetFullPath(root);
    var expectedParent = Path.GetFullPath(AppContext.BaseDirectory);
    if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(expectedParent))
        Directory.Delete(resolved, true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
}

[ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITestObjectWithSite
{
    [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    [PreserveSig] int GetSite(ref Guid iid, out IntPtr result);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int NativeGetViewFlags(IntPtr self, out uint flags);

internal sealed class FakeShellViewStateSource : IShellViewStateSource
{
    public IReadOnlyList<DisplayColumnState>? DisplayColumns { get; set; }
    public int GetDisplayColumns(out IReadOnlyList<DisplayColumnState>? columns)
    {
        columns = DisplayColumns;
        return FailedStep == "display-columns" ? -1 : columns is null ? 1 : 0;
    }
    public uint Mode { get; init; }
    public int IconSize { get; init; }
    public IReadOnlyList<SortColumnState> Sorts { get; init; } = [];
    public PropertyKeyState? Group { get; init; }
    public bool GroupAscending { get; init; }
    public string? FailedStep { get; set; }

    public int GetMode(out uint mode, out int iconSize)
    {
        mode = Mode;
        iconSize = IconSize;
        return FailedStep == "mode" ? -1 : 0;
    }

    public int GetSortColumnCount(out int count)
    {
        count = FailedStep == "invalid-count" ? 17 : Sorts.Count;
        return FailedStep == "count" ? -1 : 0;
    }

    public int GetSortColumns(int count, out IReadOnlyList<SortColumnState> columns)
    {
        columns = FailedStep == "short-columns" ? [] : Sorts;
        return FailedStep == "columns" ? -1 : 0;
    }

    public int GetGroupBy(out PropertyKeyState? key, out bool ascending)
    {
        key = Group;
        ascending = GroupAscending;
        return FailedStep == "group" ? -1 : 0;
    }
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int NativeColumnClicked(IntPtr self, IntPtr view, int column);
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int NativeCurrentFilter(IntPtr self, IntPtr text, int capacity);
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int NativePreViewCreated(IntPtr self, IntPtr view);
