# This opt-in smoke test launches a disposable app copy on an idle desktop.
# It never sends keyboard input, moves the pointer, or changes the clipboard.
# Run only after the user reserves the desktop for testing:
# pwsh -NoProfile -File .\src\ExplorerDirs.GuiSmoke\run.ps1 -AllowInteractiveDesktopControl
# Add -CaptureFocusTrace to retain keyboard/focus routing diagnostics in the run directory.
param(
    [switch]$AllowInteractiveDesktopControl,
    [switch]$PrepareOnly,
    [switch]$CaptureFocusTrace
)

$ErrorActionPreference = 'Stop'
if ($PrepareOnly -and $AllowInteractiveDesktopControl) {
    throw 'Choose preparation only or interactive execution, not both.'
}
if (-not $PrepareOnly -and -not $AllowInteractiveDesktopControl) {
    throw 'GUI smoke test needs -AllowInteractiveDesktopControl and an idle desktop; no window was launched.'
}
if (-not $IsWindows) { throw 'This GUI smoke test requires Windows.' }

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sourceCommit = & git -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot record the acceptance source revision.' }
$sourceChanges = & git -C $projectRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot record acceptance source cleanliness.' }
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state\gui-smoke'))
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$buildVersion = "0.6.0-preview.$runName"
$buildIdentity = "$buildVersion+$($sourceCommit.Trim())"
$runRoot = [IO.Path]::GetFullPath((Join-Path $stateRoot $runName))
if (-not $runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test path escaped src/.state/gui-smoke.'
}
$appDirectory = Join-Path $runRoot 'app'
$acceptanceDirectory = Join-Path $runRoot 'acceptance-files'
$testDirectory = Join-Path $acceptanceDirectory 'pane-1'
$parentDirectoryItem = [IO.Path]::GetFileName($testDirectory)
$recursiveSizeDirectory = Join-Path $acceptanceDirectory 'recursive-size-progress'
$focusTracePath = Join-Path $runRoot 'focus-trace.log'
New-Item -ItemType Directory -Path $appDirectory, $testDirectory, $recursiveSizeDirectory -Force | Out-Null
# Choose the literal folder through the Shell tree/list during later acceptance.
# Set this variable only in the launched test process, never in the user's global environment.
$percentVariable = 'ExplorerDirsQdrAcceptance'
$percentFixtureParent = Join-Path $acceptanceDirectory 'percent-paths'
$percentLiteralFixture = Join-Path $percentFixtureParent ('%' + $percentVariable + '%')
$percentExpandedFixture = Join-Path $percentFixtureParent 'expanded-target'
New-Item -ItemType Directory -Path $percentLiteralFixture, $percentExpandedFixture -Force | Out-Null
$searchToken = 'ExplorerDirsSmoke' + [Guid]::NewGuid().ToString('N')
$searchName = "$searchToken.txt"
[IO.File]::WriteAllText((Join-Path $testDirectory $searchName), 'isolated search proof')

# Keep manual file-operation acceptance inside four independent disposable folders.
# Pane 1 also contains the search fixture used by the opt-in UI smoke steps.
foreach ($pane in 1..4) {
    $paneDirectory = Join-Path $acceptanceDirectory "pane-$pane"
    New-Item -ItemType Directory -Path $paneDirectory -Force | Out-Null
    foreach ($index in 1..5) {
        $fixture = Join-Path $paneDirectory "sample-$index.txt"
        if (-not (Test-Path -LiteralPath $fixture)) {
            [IO.File]::WriteAllText($fixture, "Disposable pane $pane sample $index.", [Text.UTF8Encoding]::new($false))
        }
    }
    New-Item -ItemType Directory -Path (Join-Path $paneDirectory 'empty-folder') -Force | Out-Null
}
$cloneHistoryDirectory = Join-Path $testDirectory 'clone-history'
foreach ($stop in 'history-stop-1', 'history-stop-2') {
    $stopDirectory = Join-Path $cloneHistoryDirectory $stop
    New-Item -ItemType Directory -Path $stopDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $stopDirectory "$stop.txt"), "Disposable $stop selection fixture.",
        [Text.UTF8Encoding]::new($false))
}
# Keep the recursive-size stress fixture separate from normal pane operations so
# it can exercise status progress without making startup or other tests heavy.
foreach ($index in 1..10000) {
    $fixture = Join-Path $recursiveSizeDirectory ("size-{0:D5}.bin" -f $index)
    if (-not (Test-Path -LiteralPath $fixture)) {
        [IO.File]::WriteAllBytes($fixture, [byte[]]::new(0))
    }
}
$dropCopyFixture = Join-Path $acceptanceDirectory 'pane-1\cross-pane-copy-source.txt'
$dropMoveFixture = Join-Path $acceptanceDirectory 'pane-1\cross-pane-move-source.txt'
[IO.File]::WriteAllText($dropCopyFixture, 'Disposable copy drag-and-drop fixture.', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($dropMoveFixture, 'Disposable move drag-and-drop fixture.', [Text.UTF8Encoding]::new($false))
$aliasCopyFixture = Join-Path $acceptanceDirectory 'pane-1\alt-k-copy-source.txt'
$aliasCutFixture = Join-Path $acceptanceDirectory 'pane-1\alt-v-cut-source.txt'
[IO.File]::WriteAllText($aliasCopyFixture, 'Disposable Alt+K copy fixture.', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($aliasCutFixture, 'Disposable Alt+V cut fixture.', [Text.UTF8Encoding]::new($false))
$programStarterFixturePath = Join-Path $runRoot 'qdir-program-items.ini'
[IO.File]::WriteAllLines($programStarterFixturePath, [string[]]@(
    '[Program]',
    'Preview Tool="C:\Program Files\Explorer Dirs Test\viewer.exe"=--preview="C:\Program Files\Explorer Dirs Test"',
    'No Args Tool="C:\Program Files\Explorer Dirs Test\no-args.exe"'
), [Text.UTF8Encoding]::new($false))
$qdrColorBaseline = Join-Path $runRoot 'qdr-color-baseline.qdr'
[IO.File]::WriteAllLines($qdrColorBaseline, [string[]]@(
    'Q-Dir Version=12.79',
    'ViewMode=0',
    'ViewMode1=4',
    "Dir1=$testDirectory"
), [Text.UTF8Encoding]::new($false))
$qdrColorMetadataFixture = Join-Path $runRoot 'qdr-color-metadata.qdr'
[IO.File]::WriteAllLines($qdrColorMetadataFixture, [string[]]@(
    'Q-Dir Version=12.79',
    'ViewMode=0',
    'ViewMode1=4',
    'useColor=1',
    'useTreeColor=0',
    'colorthema=0',
    "Dir1=$testDirectory",
    '[Filter2]',
    '0=#Hidden=1=-1=1=1=-1=0',
    '1=*.zip;*.rar;=1=00AA00=-1=1=-1=0',
    '2=#BG=1=-1=-1=1=-1=0',
    '3=#BG-A=1=-1=-1=1=-1=0'
), [Text.UTF8Encoding]::new($false))

# Import into the initially single-tab pane to reach the supported 100-tab
# boundary without creating 99 tabs by hand. All paths are disposable fixtures.
$tabCapacityFixture = Join-Path $runRoot 'qdr-tab-capacity-99.qdr'
$tabCapacityLines = [Collections.Generic.List[string]]::new()
$tabCapacityLines.Add('Q-Dir Version=12.79')
$tabCapacityLines.Add("Dir1=$testDirectory")
$tabCapacityLines.Add('CurPos1=0')
$tabCapacityLines.Add('ViewMode1=4')
foreach ($tabIndex in 1..98) {
    $tabCapacityLines.Add("Dir1_$tabIndex=$testDirectory")
    $tabCapacityLines.Add("CurPos1_$tabIndex=$tabIndex")
    $tabCapacityLines.Add("ViewMode1_$tabIndex=4")
}
[IO.File]::WriteAllLines($tabCapacityFixture, $tabCapacityLines, [Text.UTF8Encoding]::new($false))

dotnet publish (Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj') `
    --configuration Release --self-contained false --output $appDirectory `
    "-p:Version=$buildVersion" -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $appDirectory 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing test executable: $exe" }
$assemblyPath = Join-Path $appDirectory 'ExplorerDirs.App.dll'
if ((Get-Item -LiteralPath $assemblyPath).VersionInfo.ProductVersion -ne $buildIdentity) {
    throw 'Acceptance assembly does not contain its unique build identity.'
}

# Keep the isolated release package shape intact so release-only management
# tools can be accepted without ever registering them during PrepareOnly.
$executableHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
[IO.File]::WriteAllLines((Join-Path $appDirectory 'FolderRoutingCapability.ini'), [string[]]@(
    'FormatVersion=1',
    'DirectoryArgument=true',
    "ExecutableSha256=$executableHash"
), [Text.UTF8Encoding]::new($false))
$toolsDirectory = Join-Path $runRoot 'tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
foreach ($name in @('folder-verb.ps1', 'FolderVerb.psm1')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "scripts\$name") -Destination (Join-Path $toolsDirectory $name)
}

# A Release app copied outside releases/ uses the profile beside its executable.
$profileDirectory = Join-Path $appDirectory 'profile'
New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
$iniPath = Join-Path $profileDirectory 'ExplorerDirs.ini'
$otherPaneSettings = [Text.StringBuilder]::new()
foreach ($pane in 1..3) {
    $paneDirectory = Join-Path $acceptanceDirectory "pane-$($pane + 1)"
    [void]$otherPaneSettings.AppendLine("[Pane$pane]")
    [void]$otherPaneSettings.AppendLine('TabCount=1')
    [void]$otherPaneSettings.AppendLine("[Pane$pane.Tab0]")
    [void]$otherPaneSettings.AppendLine("Path=$paneDirectory")
    [void]$otherPaneSettings.AppendLine('ViewMode=4')
    [void]$otherPaneSettings.AppendLine('IconSize=-1')
    [void]$otherPaneSettings.AppendLine('SortCount=0')
    [void]$otherPaneSettings.AppendLine('GroupKey=')
    [void]$otherPaneSettings.AppendLine('GroupAscending=true')
    [void]$otherPaneSettings.AppendLine()
}
$ini = @"
[General]
FormatVersion=1
Layout=4
DefaultViewMode=4

[Pane0]
TabCount=1
[Pane0.Tab0]
Path=$testDirectory
ViewMode=4
IconSize=-1
SortCount=0
GroupKey=
GroupAscending=true

$($otherPaneSettings.ToString())
"@
[IO.File]::WriteAllText($iniPath, $ini, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $profileDirectory 'NavigationTreeDragHoverDelay.ini'),
    "; Explorer Dirs portable preference.`r`n[NavigationTree]`r`nFormatVersion=1`r`nDragHoverExpandDelayMilliseconds=1500`r`n",
    [Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    SchemaVersion = 1
    Status = 'prepared-not-run'
    PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
    SourceCommit = $sourceCommit.Trim()
    SourceWorkingTreeDirty = [bool]$sourceChanges
    BuildIdentity = $buildIdentity
    Executable = 'app/ExplorerDirs.App.exe'
    Profile = 'app/profile/ExplorerDirs.ini'
    NavigationTreeDragHoverDelay = 'app/profile/NavigationTreeDragHoverDelay.ini'
    ManualFixtures = 'acceptance-files'
    ParentDirectoryItem = $parentDirectoryItem
    CloneHistoryFixture = 'acceptance-files/pane-1/clone-history'
    RecursiveSizeFixture = 'acceptance-files/recursive-size-progress'
    QdrColorBaseline = 'qdr-color-baseline.qdr'
    QdrColorMetadataFixture = 'qdr-color-metadata.qdr'
    QdrColorMetadataFixtureSha256 = (Get-FileHash -LiteralPath $qdrColorMetadataFixture -Algorithm SHA256).Hash
    TabCapacityFixture = 'qdr-tab-capacity-99.qdr'
    PercentPathFixture = 'acceptance-files/percent-paths'
    PercentPathProcessVariable = $percentVariable
    PercentPathProcessValue = 'expanded-target'
    TabCapacityFixtureSha256 = (Get-FileHash -LiteralPath $tabCapacityFixture -Algorithm SHA256).Hash
    QdrProgramFixture = 'qdir-program-items.ini'
    QdrProgramFixtureSha256 = (Get-FileHash -LiteralPath $programStarterFixturePath -Algorithm SHA256).Hash
    InitialLayout = 4
    VisiblePaneCount = 4
    ExecutableSha256 = $executableHash
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    FocusTrace = if ($CaptureFocusTrace) { 'focus-trace.log' } else { $null }
    # Keep the current release scope separate from older parity regression cases.
    # Preparation records pending checks, never implies that any visible test ran.
    Temp86Acceptance = @(
        'F5: compare temp.80 and this candidate on the same disposable directory, source and view mode; record key-to-visible-update latency, no blank interval, preserved selection, arrow-key focus and responsiveness during size updates. View-mode switching must retain the already accepted fast behavior',
        'Address menu: repeated dropdown opens remain visible, ancestors and cross-window recent addresses navigate correctly; the clock retains per-tab back/forward history. Verify Modern F4 tree/edit remains independent',
        'Locations and favorites: home/desktop/downloads/documents/drives, configurable ordering, grouped two-level add/rename/move/reorder/delete, empty menu, legacy backup, external-change conflict and restart persistence; use isolated profile only',
        'Tab strip: no redundant address Edit button, new/close on tab row, arrows hidden when fitting and at least 28 pixels when overflowing; first/last disabled states, resize, multiline, hidden/single-tab modes, DPI and keyboard focus',
        'Folder sizes: default asynchronous standard Size column, exact known zero versus unknown, View disable/re-enable, Auto/Everything-only/BuiltIn source changes; large directories, nested Unicode paths, source changes/cancellation/F5 while pending, physical address/session identity and native back/forward/parent history without duplicate entries',
        'Native provider compatibility: real Size ascending/descending and saved per-pane sort after restart, selection/focus, rename, native item/background menus, copy/paste, drag/drop and filesystem notifications; preserve ordinary file sizes and non-size properties. Do not count old wrong-property-key sort experiments as evidence',
        'Space analysis: explicit start, rankings/percentages, list-map selection, drill/up, locate tab and tab-capacity error, cancel during query/scan, close/reopen release and stable memory, unavailable/partial data and light/dark themes',
        'Release gate: all six docs/temp86-goals.md targets require evidence; prepared artifact or passing background checks alone are not publish authorization. Only after the full gate replace the same temp.86 payload with recoverable backup and unchanged daily profile'
    )
    PendingAcceptance = @(
        'In View → Title Bar Display, check application name only, current folder full path, current folder name, selected item full path, and recent favorite. In two windows from this isolated profile, add and open a different disposable favorite in each, restart both window sessions, and verify each recent-favorite title restores its own path while ordinary navigation leaves it unchanged',
        'For percent-paths, set PercentPathProcessVariable=PercentPathProcessValue only in the isolated launched process. Navigate through the file list/tree into the literal percent-named folder; export ordinary QDR and a desktop workspace shortcut. Open their base Folder fields in Q-Dir and verify the literal directory rather than expanded-target. Cancel an unresolved-path compatibility prompt and verify no target was written',
        'During import backup preparation, change a tab/view/layout or switch to a sibling window: keep the backup but do not append. Repeat with a stable session and verify the exact session plus QDR backup, then append normally. Force a disposable backup output failure and verify no imported tabs appear',
        'Close one isolated window during QDR preparation or desktop workspace publication; no late UI changes appear. While a sibling window keeps the process alive, inspect confirmed output and ensure shortcut/QDR match or failed publication cleans its new QDR',
        'For the updated window registry, restart multiple isolated windows and verify per-window placement/session restoration; the background unknown-version protection checks do not replace normal restart acceptance',
        'In each pane, paste a file and immediately press Up/Down without clicking first; then use Shift+Delete, confirm, and immediately press Up/Down. The same pane must move its file focus/selection after both operations. If either fails, Alt+Tab away and back, then repeat the arrow key and record whether window activation restores keyboard navigation. When FocusTrace is set, retain focus-trace.log and compare the key, IShellView accelerator result, HWND focus, Shell item focus, and focus-restore-poll content-focused/attempt values around the operation',
        'In every pane and view style, F2 then Left/Up/Right/Down/Home/End/PageUp/PageDown keeps the native rename editor focused; Shift/Ctrl+Left/Right/Home/End edits text; Escape cancels and Enter commits without changing another pane selection. In file content verify Home/End/PageUp/PageDown and their Ctrl/Shift variants keep selection and physical focus in that pane',
        'With the file view focused, F10 opens the File menu; Escape closes it without running a command; F10 can open it again',
        'Select one fixture file and use Edit → Copy selected item full path; paste into a scratch text field and verify the exact unquoted absolute path. Repeat with the file view focused using Ctrl+Shift+C, then focus address/search editing and verify the shortcut remains available to text input. Select multiple files and verify one full path per line. Confirm the menu item is disabled with no selection, a virtual Shell selection is rejected as a whole, and focus returns to the same file view so an arrow key still changes selection',
        'In pane 1, verify Tab/Shift+Tab includes Address, path directory tree, This PC and address history in that order. Click the path dropdown: the ancestor/recent-address menu must remain open and navigate only pane 1; the clock button shows independent current-tab history. F4 Modern still opens the tree then edits the full path; F7 still opens the current path hierarchy and F8 / This PC opens configured common locations, drives and grouped favorites. Use Alt+K on alt-k-copy-source.txt, switch to pane 2 and Ctrl+V; verify the source remains and copied content matches. Use Alt+V on alt-v-cut-source.txt, paste to pane 2 and verify the source is removed and content matches. In address/search editing and F2 rename text, verify Alt+V/Alt+K are not intercepted',
        'In four-pane and two-pane layouts, verify Ctrl+Alt+1…4 activates the corresponding visible pane and focuses its file view; requesting a hidden pane leaves the current pane active. Confirm Ctrl+1…9 still selects tabs, Ctrl+Shift+1…8 still changes view style, and address/search editing keeps Ctrl+Alt+number input',
        'Use Ctrl+Q to add fixture folders plus one file/program quick link. A folder link opens in the active pane on ordinary click and in a copied window session on Ctrl+click; confirm the original window and its session remain unchanged. Ctrl+click file/program links must keep their normal Shell launch behavior',
        'Select one item, right-click unused viewport below the last row, then separately right-click empty trailing space inside a visible row. Verify the file-area menu remains native-only and targets the confirmed item or background; do not assume row-tail whitespace is background. Retain focus-trace.log coordinates/control types and record which Shell menu appears at each point',
        'In Details view, click the filter arrow—not the resize edge or header body—on Type. Select TXT, confirm only matching rows remain, then clear it and verify all rows return; combine two values in one column and filters across columns, duplicate the tab, restart, and confirm per-tab filter state and other panes stay independent. Verify ordinary header clicks still sort and the filter menu remains legible in both themes. Navigate to RecursiveSizeFixture, open the Name filter with 10,000 distinct values, use Next to reach a later page, select one value, page away and back, and verify its checked state and selected count persist before clearing the filter and confirming all 10,000 rows return',
        'Right-click a tab and verify “Use Explorer to browse” is the first custom tab command and opens the confirmed folder in Windows Explorer without adding an Explorer Dirs tab. Right-click blank tab-strip space and verify it uses the selected tab menu; double-click blank space still creates a tab, while double-clicking a tab closes it',
        'Select multiple children in a regular folder, duplicate the tab and verify the same items remain selected. Navigate another tab to This PC, select a drive, duplicate it and verify the drive remains selected in the clone; deleting a selected item before clone navigation completes must not prevent surviving selections from restoring',
        'In a fixture folder, select multiple files and use the tab header Clone View command. Navigate to a second folder, then use Back/Forward and verify each location returns with its own saved selection. Repeat with a scoped search result selection; wait for all selected result names to appear before checking the clone. Navigate to a new folder after going Back and verify the old forward branch is removed. Also verify Clone View refuses a snapshot if navigation or selection changes during capture, without creating a mixed-state tab',
        'Ctrl-drag cross-pane-copy-source.txt from pane-1 to pane-2; confirm source and destination remain and destination content matches',
        'Drag cross-pane-move-source.txt from pane-1 to pane-2; confirm the source disappears and destination content matches',
        'Tab and Shift+Tab traverse every enabled stop of each visible pane in reversible order, including the Details column header. From a file use Shift+Tab to enter Name, Right to move to the next column, then Tab to return to the same selected file; Up/Down must work. F6 and Shift+F6 traverse major regions, including the last-pane to first-pane wrap',
        'The address-history button shows current-tab recent paths and selecting one navigates only that tab; Tab/Shift+Tab include the button and address-edit right-click retains its text context menu. In Modern style F4 opens the directory tree and a second F4 edits the full path; clicking breadcrumb blank space also edits, while clicking an ancestor navigates. Classic style keeps directly editable paths',
        'Visit two fixture folders, close and reopen the tab, and verify its address dropdown keeps those entries. Duplicate it, visit another folder in the copy, and verify the original dropdown is unchanged; this does not assert restoration of the disposed Shell Travel Log',
        'In pane 1, navigate through clone-history/history-stop-1, select its text file, then navigate through clone-history/history-stop-2 and select its file. Use the tab context menu → Clone Current View. Verify the clone opens at stop 2 with the same selection; move Back and Forward in the clone and verify each stop restores its own selection. Navigate the clone to a different folder and verify the source tab path, history and selections remain unchanged; then independently navigate the source and confirm the clone is unchanged',
        'Immediately after directory navigation, duplicate the tab, create a current-folder tab, close/reopen it, open its address editor and close a pending search; verify every operation uses the confirmed directory. Save/restart and check both the path and pinned metadata, without replacing a newer address/search draft',
        'Move and resize each window on a secondary monitor, including a negative-coordinate monitor where available, restart the isolated app, and verify normal bounds and maximized state. Minimize a maximized window and relaunch the same isolated executable; verify it restores maximized. Ctrl+N and a new QDR workspace must appear on the source monitor; also cover F11 and mixed DPI without altering the daily-use profile',
        'Per-tab history survives tab and layout switches',
        'In a disposable Q-Dir workspace copy, inspect the Tab Group, Move to new Tab Group, Copy to new Tab Group, and Use Tab Groups commands. Record the exact Q-Dir version, whether groups are per window or pane, how switching works, whether tabs can move/copy across panes or layouts, and what group state survives saving/reopening a copied .qdr workspace. Do not overwrite the original Q-Dir settings or workspace files; leave implementation semantics unresolved until this observation is recorded',
        'Ctrl+wheel, toolbar and menu view changes preserve per-tab sorting/grouping and save independently. While switching styles or applying a column preset, navigate or switch tabs; no retired operation may change the replacement tab, publish an old error or forward old wheel input. Retain evidence separately from the controlled backend callback tests',
        'In a populated filesystem fixture, select and focus a file, press F5, and verify an externally added item appears through the current Shell directory notification. The candidate holds redraw for the current Shell view until three stable row-count samples or a 1.5-second cap. Record blank duration, selection, focus, scroll position, whether Shell paints rows during enumeration, and whether mouse/keyboard targets still match the visible rows; any stale hit target rejects this candidate',
        'In a populated filesystem fixture, press F5 again while the previous refresh is still pending. Verify both refreshes converge to the newest external files; record when the redraw hold releases, any blank interval, and whether mouse/keyboard input targets match the visible rows throughout. Check selection, focus and scroll position',
        'In a populated search or virtual Shell location, press F5 to exercise the current-view IShellView.Refresh fallback under the same bounded redraw hold. Record blank duration, selection, focus and scroll position; confirm mouse/keyboard targets match visible rows during the hold and after release',
        'View → Soft-Refresh is off by default. Enable it only in the disposable profile, then compare F5, an external fixture-file addition, and a copy/rename in the same folder: record whether old rows remain visible, whether new/renamed rows appear, and whether sorting changes. Disable Soft-Refresh, press F5, and verify the directory converges to the real contents; create a new tab and restart once to check the setting is global and portable. Do not infer Q-Dir parity from the label alone',
        'Switch Details/List/Icon/Tile/Content modes and confirm the path, tabs, pane chrome and status remain stable while only file rows reflow. The mode setter temporarily suppresses redraw only for its synchronous call; record blank time and scroll/selection/focus results, checking whether the native reflow itself visibly clears the rows',
        'Close address editing or search, or clear a filter, then switch tabs before delayed focus restoration executes. The old request must not move focus in the replacement tab; the same-tab normal path must still return to files. This timing-sensitive case is pending if it cannot be observed reliably',
        'Set a group and direction, apply and clear a quick filter, then duplicate and restart; temporary ungrouping must not become saved no-group. While enumeration/restoration is pending, explicitly choose a different native group and direction; the new choice must persist. Test explicit native no-group separately and record it as unresolved if old grouping returns',
        'With several tabs in a pane, start file-drag hover activation over an inactive tab, then hide that pane by changing layout; a delayed callback must not switch the hidden tab or make its pane active. Repeat normal visible hover to confirm switching still works, and cancel the drag without moving fixture files',
        'In one isolated window start QDR/workspace or current-view CSV/TXT/HTML/XLS IO on a deliberately slow disposable target, then close only that window; no late status/dialog may appear and a sibling window stays usable. A confirmed export may finish while the process remains alive; inspect its target contents before cleanup. Do not assume the last process exit waits for pending writes',
        'Open Tools > Options > Size display format; switch between Auto and fixed units, change decimal places and thousands separator, and verify selected-size, F9 total, and disk-capacity text refresh consistently. Toggle F9 file/subdirectory counts off and on; verify the summary changes while unreadable-item warnings remain. Restart the isolated app and confirm the saved unit/precision/count preferences load from SizeDisplay.ini',
        'Navigate one pane to RecursiveSizeFixture, press F9 and verify the status counter advances before the final 10,000-file summary; start a second scan, press F9 to cancel, then navigate away and confirm stale progress does not return',
        'Open Tools > Program Starter; add and save one inert test entry, then restart and verify it returns from the isolated profile ProgramStarter.ini. Import qdir-program-items.ini and verify both rows and all fields in preview; cancel and confirm ProgramStarter.ini is unchanged, then import again and explicitly apply. Verify the fixture SHA-256 is unchanged, the Q-Dir source was not modified, and imported programs were not launched',
        'Test Tools > Restart Windows Explorer in a disposable desktop session only: first cancel the confirmation and verify the Shell process is unchanged; before confirming a restart, save work and close any Explorer windows that must remain open. Then verify the desktop, taskbar, and Start menu return, and record whether Explorer folder windows shared the Shell process. Do not execute this acceptance against an active user session without explicit confirmation',
        'Open Tools > Folder Entry; verify Directory/Drive status and preview the isolated EXE path, SHA-256, and directory-argument capability. The isolated package includes tools/folder-verb.ps1 and its exact-hash capability manifest. Keep the real user registry unchanged; enabling or disabling the actual folder context entry requires a separate explicit user choice',
        'In the enabled tree mode, verify the isolated profile opens with a 1.5-second drag-hover expand delay. Compare the six menu options, hover a fixture folder through its configured delay and confirm only that folder expands, then drop a disposable copy into the intended tree folder and verify its destination/content. Change the delay, restart the isolated app, and confirm the selection persists; do not drop or move real user files',
        'Import qdr-color-metadata.qdr and confirm the preview says Q-Dir colors are retained but not applied. Export to a new .qdr and compare useColor, useTreeColor, colorthema and all [Filter2] rows in the same order. Keep the fixture SHA-256 unchanged. If checking Q-Dir GUI compatibility, open only the exported copy and close without saving over the fixture; do not claim visible file coloring from metadata preservation',
        'QDR import/export, cancellation, and native Q-Dir compatibility',
        'Theme regression: in both Light and Dark, verify cold start, duplicated tabs, pane switching, scrollbar endpoints, directory trees and native context menus retain the accepted temp.76 appearance without white flashes, delayed/double borders or transparent gutters; compare after import cancel, minimal qdr-color-baseline.qdr import and restart',
        'Run capacity checks last on a fresh isolated profile with one tab in pane-1: import TabCapacityFixture (99 incoming tabs) to reach 100, verify New/Duplicate/Middle-click/drag-copy or drag-move into that pane cannot add a tab and keep the source/selection. Reimport is rejected before partial append. At capacity Reopen Closed must preserve its history; freeing a slot permits recovery. Do not use the resulting 100-tab workload as the normal memory baseline'
    )
}
[IO.File]::WriteAllText((Join-Path $runRoot 'preparation.json'),
    ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))

# This branch must remain before UI Automation loading and all window operations.
if ($PrepareOnly) {
    Write-Output "Prepared isolated GUI acceptance copy; no window launched: $runRoot"
    return
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ExplorerDirsSmokeForeground {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@

function Wait-Until([scriptblock]$condition, [string]$description, [int]$milliseconds = 7000) {
    $limit = [DateTime]::UtcNow.AddMilliseconds($milliseconds)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Timed out waiting for $description."
}

function Assert-TestForeground([Diagnostics.Process]$app, [IntPtr]$mainWindow) {
    $foreground = [ExplorerDirsSmokeForeground]::GetForegroundWindow()
    [uint32]$owner = 0
    [ExplorerDirsSmokeForeground]::GetWindowThreadProcessId($foreground, [ref]$owner) | Out-Null
    if ($foreground -ne $mainWindow -or $owner -ne $app.Id) {
        throw "Desktop focus left the test window; stopping interactions (appPid=$($app.Id), foregroundPid=$owner)."
    }
}

function Find-Control([IntPtr]$mainWindow, [string]$automationId,
    [Windows.Automation.ControlType]$controlType) {
    $window = [Windows.Automation.AutomationElement]::FromHandle($mainWindow)
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId),
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType))
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ShellItem([IntPtr]$mainWindow, [string]$displayName) {
    $window = [Windows.Automation.AutomationElement]::FromHandle($mainWindow)
    $itemType = [Windows.Automation.OrCondition]::new(
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::ListItem),
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::DataItem))
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty, $displayName),
        $itemType)
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-TestButton([Diagnostics.Process]$app, [IntPtr]$mainWindow,
    [string]$automationId) {
    Assert-TestForeground $app $mainWindow
    $button = Find-Control $mainWindow $automationId ([Windows.Automation.ControlType]::Button)
    if ($null -eq $button) { throw "Test button $automationId was not found." }
    Assert-TestForeground $app $mainWindow
    $pattern = [Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Assert-TestForeground $app $mainWindow
}

function Invoke-Layout([Diagnostics.Process]$app, [IntPtr]$mainWindow, [int]$layout) {
    Invoke-TestButton $app $mainWindow "Layout$layout"
    Wait-Until { (Get-Content -LiteralPath $iniPath -Raw) -match "(?m)^Layout=$layout\s*$" } "saved layout $layout"
}

function Test-Search([Diagnostics.Process]$app, [IntPtr]$mainWindow,
    [Collections.IDictionary]$measurements) {
    Invoke-TestButton $app $mainWindow 'OpenCurrentDirectorySearch'
    Assert-TestForeground $app $mainWindow
    $queryBox = Find-Control $mainWindow 'PaneSearchQuery' ([Windows.Automation.ControlType]::Edit)
    if ($null -eq $queryBox) { throw 'Pane search input was not found.' }
    if (-not $queryBox.Current.HasKeyboardFocus) {
        throw 'Opening pane search did not focus its input.'
    }
    Assert-TestForeground $app $mainWindow
    $value = [Windows.Automation.ValuePattern]$queryBox.GetCurrentPattern(
        [Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($searchToken)
    Assert-TestForeground $app $mainWindow
    $searchWatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-TestButton $app $mainWindow 'PaneSearchSubmit'

    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $status = Find-Control $mainWindow 'PaneStatus' ([Windows.Automation.ControlType]::Text)
        $null -ne $status -and $status.Current.Name -eq "搜索结果：$searchToken"
    } 'Shell search navigation' 20000
    $measurements.SearchSubmitToStatusMs = [Math]::Round($searchWatch.Elapsed.TotalMilliseconds, 1)

    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $window = [Windows.Automation.AutomationElement]::FromHandle($mainWindow)
        foreach ($displayName in @($searchName, $searchToken)) {
            $condition = [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::NameProperty, $displayName)
            if ($null -ne $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)) {
                return $true
            }
        }
        return $false
    } 'Shell search result item' 20000
    $measurements.SearchSubmitToResultMs = [Math]::Round($searchWatch.Elapsed.TotalMilliseconds, 1)

    Invoke-TestButton $app $mainWindow 'PaneSearchClose'
    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $status = Find-Control $mainWindow 'PaneStatus' ([Windows.Automation.ControlType]::Text)
        $null -eq (Find-Control $mainWindow 'PaneSearchQuery' ([Windows.Automation.ControlType]::Edit)) -and
            $null -ne $status -and $status.Current.Name -eq 'Shell 视图'
    } 'search to return to the directory'
}

$app = $null
$measurements = [ordered]@{
    SchemaVersion = 1
    Status = 'failed'
    ExecutableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    StartToWindowMs = $null
    StartToFirstFileItemMs = $null
    DirectoryUpToItemMs = $null
    DirectoryBackToItemMs = $null
    SearchSubmitToStatusMs = $null
    SearchSubmitToResultMs = $null
}
try {
    $startupWatch = [Diagnostics.Stopwatch]::StartNew()
    $startParameters = @{ FilePath = $exe; PassThru = $true }
    if ($CaptureFocusTrace) {
        $startParameters.Environment = @{ EXPLORER_DIRS_FOCUS_TRACE = $focusTracePath }
    }
    $app = Start-Process @startParameters
    Wait-Until {
        $app.Refresh()
        -not $app.HasExited -and $app.MainWindowHandle -ne [IntPtr]::Zero
    } 'isolated Explorer Dirs window'
    $measurements.StartToWindowMs = [Math]::Round($startupWatch.Elapsed.TotalMilliseconds, 1)
    $mainWindow = $app.MainWindowHandle
    [uint32]$owner = 0
    [ExplorerDirsSmokeForeground]::GetWindowThreadProcessId($mainWindow, [ref]$owner) | Out-Null
    if ($owner -ne $app.Id -or $app.MainWindowTitle -notlike 'Explorer Dirs*') {
        throw 'Unexpected test window ownership or title.'
    }
    Assert-TestForeground $app $mainWindow
    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $null -ne (Find-ShellItem $mainWindow $searchName)
    } 'first file item in the initial directory' 20000
    $measurements.StartToFirstFileItemMs = [Math]::Round($startupWatch.Elapsed.TotalMilliseconds, 1)
    $directoryWatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-TestButton $app $mainWindow 'PaneGoParent'
    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $null -ne (Find-ShellItem $mainWindow $parentDirectoryItem)
    } 'parent directory item' 20000
    $measurements.DirectoryUpToItemMs = [Math]::Round($directoryWatch.Elapsed.TotalMilliseconds, 1)
    $directoryWatch.Restart()
    Invoke-TestButton $app $mainWindow 'PaneGoBack'
    Wait-Until {
        Assert-TestForeground $app $mainWindow
        $null -ne (Find-ShellItem $mainWindow $searchName)
    } 'returned directory item' 20000
    $measurements.DirectoryBackToItemMs = [Math]::Round($directoryWatch.Elapsed.TotalMilliseconds, 1)
    Invoke-Layout $app $mainWindow 4
    Invoke-Layout $app $mainWindow 1
    Test-Search $app $mainWindow $measurements
    $measurements.Status = 'passed'
    Write-Output "Layout toolbar, isolated INI save, and Shell search result: PASS ($runRoot)"
}
finally {
    try {
        $performanceJson = $measurements | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText((Join-Path $runRoot 'performance.json'), $performanceJson,
            [Text.UTF8Encoding]::new($false))
    }
    catch { Write-Warning 'Could not save the isolated timing record.' }
    if ($null -ne $app) {
        $app.Refresh()
        if (-not $app.HasExited) {
            $app.CloseMainWindow() | Out-Null
            if (-not $app.WaitForExit(5000) -and $app.Path -eq $exe) {
                Stop-Process -Id $app.Id -Force
            }
        }
    }
}
