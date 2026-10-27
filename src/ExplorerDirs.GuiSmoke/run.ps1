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
$buildIdentity = "gui-acceptance.$runName+$($sourceCommit.Trim())"
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
$programStarterFixturePath = Join-Path $runRoot 'qdir-program-items.ini'
[IO.File]::WriteAllLines($programStarterFixturePath, [string[]]@(
    '[Program]',
    'Preview Tool="C:\Program Files\Explorer Dirs Test\viewer.exe"=--preview="C:\Program Files\Explorer Dirs Test"',
    'No Args Tool="C:\Program Files\Explorer Dirs Test\no-args.exe"'
), [Text.UTF8Encoding]::new($false))
$qdrColorBaseline = Join-Path $runRoot 'qdr-color-baseline.qdr'
[IO.File]::WriteAllLines($qdrColorBaseline, [string[]]@(
    'Q-Dir Version=12.79',
    'ViewMode1=4',
    "Dir1=$testDirectory"
), [Text.UTF8Encoding]::new($false))

dotnet publish (Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj') `
    --configuration Release --self-contained false --output $appDirectory `
    "-p:InformationalVersion=$buildIdentity" -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $appDirectory 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing test executable: $exe" }
$assemblyPath = Join-Path $appDirectory 'ExplorerDirs.App.dll'
if ((Get-Item -LiteralPath $assemblyPath).VersionInfo.ProductVersion -ne $buildIdentity) {
    throw 'Acceptance assembly does not contain its unique build identity.'
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

$manifest = [ordered]@{
    SchemaVersion = 1
    Status = 'prepared-not-run'
    PreparedAtUtc = [DateTime]::UtcNow.ToString('o')
    SourceCommit = $sourceCommit.Trim()
    SourceWorkingTreeDirty = [bool]$sourceChanges
    BuildIdentity = $buildIdentity
    Executable = 'app/ExplorerDirs.App.exe'
    Profile = 'app/profile/ExplorerDirs.ini'
    ManualFixtures = 'acceptance-files'
    ParentDirectoryItem = $parentDirectoryItem
    RecursiveSizeFixture = 'acceptance-files/recursive-size-progress'
    QdrColorBaseline = 'qdr-color-baseline.qdr'
    QdrProgramFixture = 'qdir-program-items.ini'
    QdrProgramFixtureSha256 = (Get-FileHash -LiteralPath $programStarterFixturePath -Algorithm SHA256).Hash
    InitialLayout = 4
    VisiblePaneCount = 4
    ExecutableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    FocusTrace = if ($CaptureFocusTrace) { 'focus-trace.log' } else { $null }
    PendingAcceptance = @(
        'In each pane, paste a file and immediately press Up/Down without clicking first; then use Shift+Delete, confirm, and immediately press Up/Down. The same pane must move its file focus/selection after both operations. If either fails, Alt+Tab away and back, then repeat the arrow key and record whether window activation restores keyboard navigation. When FocusTrace is set, retain focus-trace.log and compare the key, IShellView accelerator result, HWND focus, Shell item focus, and focus-restore-poll content-focused/attempt values around the operation',
        'In every pane and view style, F2 then Left/Up/Right/Down/Home/End/PageUp/PageDown keeps the native rename editor focused; Shift/Ctrl+Left/Right/Home/End edits text; Escape cancels and Enter commits without changing another pane selection. In file content verify Home/End/PageUp/PageDown and their Ctrl/Shift variants keep selection and physical focus in that pane',
        'With the file view focused, F10 opens the File menu; Escape closes it without running a command; F10 can open it again',
        'Select one item, right-click unused viewport below the last row, then separately right-click empty trailing space inside a visible row. Compare the Shell native menu and custom command target for both positions; do not assume row-tail whitespace is background. Retain focus-trace.log coordinates/control types and record which Shell menu appears at each point',
        'Ctrl-drag cross-pane-copy-source.txt from pane-1 to pane-2; confirm source and destination remain and destination content matches',
        'Drag cross-pane-move-source.txt from pane-1 to pane-2; confirm the source disappears and destination content matches',
        'Tab and Shift+Tab traverse all visible panes in reverse order',
        'The address-history button shows current-tab recent paths and selecting one navigates only that tab; Tab/Shift+Tab include the button and address-edit right-click retains its text context menu. In Modern style F4 opens the directory tree and a second F4 edits the full path; clicking breadcrumb blank space also edits, while clicking an ancestor navigates. Classic style keeps directly editable paths',
        'Visit two fixture folders, close and reopen the tab, and verify its address dropdown keeps those entries. Duplicate it, visit another folder in the copy, and verify the original dropdown is unchanged; this does not assert restoration of the disposed Shell Travel Log',
        'Immediately after directory navigation, duplicate the tab, create a current-folder tab, close/reopen it, open its address editor and close a pending search; verify every operation uses the confirmed directory. Save/restart and check both the path and pinned metadata, without replacing a newer address/search draft',
        'Move and resize each window on a secondary monitor, including a negative-coordinate monitor where available, restart the isolated app, and verify normal bounds and maximized state. Minimize a maximized window and relaunch the same isolated executable; verify it restores maximized. Ctrl+N and a new QDR workspace must appear on the source monitor; also cover F11 and mixed DPI without altering the daily-use profile',
        'Per-tab history survives tab and layout switches',
        'Ctrl+wheel view changes and independent per-tab view persistence',
        'Open Tools > Options > Size display format; switch between Auto and fixed units, change decimal places and thousands separator, and verify selected-size, F9 total, and disk-capacity text refresh consistently. Toggle F9 file/subdirectory counts off and on; verify the summary changes while unreadable-item warnings remain. Restart the isolated app and confirm the saved unit/precision/count preferences load from SizeDisplay.ini',
        'Navigate one pane to RecursiveSizeFixture, press F9 and verify the status counter advances before the final 10,000-file summary; start a second scan, press F9 to cancel, then navigate away and confirm stale progress does not return',
        'Open Tools > Program Starter; add and save one inert test entry, then restart and verify it returns from the isolated profile ProgramStarter.ini. Import qdir-program-items.ini and verify both rows and all fields in preview; cancel and confirm ProgramStarter.ini is unchanged, then import again and explicitly apply. Verify the fixture SHA-256 is unchanged, the Q-Dir source was not modified, and imported programs were not launched',
        'QDR import/export, cancellation, and native Q-Dir compatibility',
        'Color diagnosis: on the same directory and Details view, compare the initial fresh view, an ordinary new tab created in the running window, import-dialog cancel, minimal qdr-color-baseline.qdr import, and restart; capture the DirectUI file-area pixels at the same screen region after each step, and note exactly which transition changes the color'
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
