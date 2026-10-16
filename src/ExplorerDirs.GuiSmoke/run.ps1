# This opt-in smoke test launches a disposable app copy on an idle desktop.
# It never sends keyboard input, moves the pointer, or changes the clipboard.
# Run only after the user reserves the desktop for testing:
# pwsh -NoProfile -File .\src\ExplorerDirs.GuiSmoke\run.ps1 -AllowInteractiveDesktopControl
param([switch]$AllowInteractiveDesktopControl)

$ErrorActionPreference = 'Stop'
if (-not $AllowInteractiveDesktopControl) {
    throw 'GUI smoke test needs -AllowInteractiveDesktopControl and an idle desktop; no window was launched.'
}
if (-not $IsWindows) { throw 'This GUI smoke test requires Windows.' }

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state\gui-smoke'))
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$runRoot = [IO.Path]::GetFullPath((Join-Path $stateRoot $runName))
if (-not $runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test path escaped src/.state/gui-smoke.'
}
$appDirectory = Join-Path $runRoot 'app'
$testDirectory = Join-Path $runRoot 'files'
New-Item -ItemType Directory -Path $appDirectory, $testDirectory -Force | Out-Null
$searchToken = 'ExplorerDirsSmoke' + [Guid]::NewGuid().ToString('N')
$searchName = "$searchToken.txt"
[IO.File]::WriteAllText((Join-Path $testDirectory $searchName), 'isolated search proof')

dotnet publish (Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj') `
    --configuration Release --self-contained false --output $appDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $appDirectory 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing test executable: $exe" }

# A Release app copied outside releases/ uses the profile beside its executable.
$profileDirectory = Join-Path $appDirectory 'profile'
New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
$iniPath = Join-Path $profileDirectory 'ExplorerDirs.ini'
$ini = @"
[General]
FormatVersion=1
Layout=1
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

[Pane1]
TabCount=0
[Pane2]
TabCount=0
[Pane3]
TabCount=0
"@
[IO.File]::WriteAllText($iniPath, $ini, [Text.UTF8Encoding]::new($false))

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
    $app = Start-Process -FilePath $exe -PassThru
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
        $null -ne (Find-ShellItem $mainWindow 'files')
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
