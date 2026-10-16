# This script takes foreground focus, sends system-wide input, and changes the clipboard.
# Run only when the desktop is reserved for testing:
# pwsh -STA -NoProfile -File .\src\ExplorerDirs.GuiSmoke\run.ps1 -AllowInteractiveDesktopControl
# Operates on newly created files under src/.state/gui-smoke.
param([switch]$AllowInteractiveDesktopControl)

$ErrorActionPreference = 'Stop'
if (-not $AllowInteractiveDesktopControl) {
    throw 'GUI smoke test needs -AllowInteractiveDesktopControl and an idle desktop; no window or input was touched.'
}
if (-not $IsWindows) { throw 'This GUI smoke test requires Windows.' }
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with pwsh -STA so the clipboard can be restored.'
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state\gui-smoke'))
$runTag = [Guid]::NewGuid().ToString('N').Substring(0, 6)
$runRoot = [IO.Path]::GetFullPath((Join-Path $stateRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + $runTag)))
if (-not $runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test path escaped src/.state/gui-smoke.'
}
$appDirectory = Join-Path $runRoot 'app'
$source = Join-Path $runRoot 'source'
$destination = Join-Path $runRoot 'destination'
New-Item -ItemType Directory -Path $appDirectory, $source, $destination -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'alpha.txt'), 'Explorer Dirs GUI smoke alpha')
[IO.File]::WriteAllText((Join-Path $source 'beta.txt'), 'Explorer Dirs GUI smoke beta')

dotnet publish (Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj') `
    --configuration Release --self-contained false --output $appDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $appDirectory 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing test executable: $exe" }

$profileDirectory = Join-Path $appDirectory 'profile'
New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
$ini = @"
[General]
FormatVersion=1
Layout=2
DefaultViewMode=4

[Pane0]
TabCount=1
[Pane0.Tab0]
Path=$source
ViewMode=4
IconSize=-1
SortCount=0
GroupKey=
GroupAscending=true

[Pane1]
TabCount=1
[Pane1.Tab0]
Path=$destination
ViewMode=4
IconSize=-1
SortCount=0
GroupKey=
GroupAscending=true

[Pane2]
TabCount=0
[Pane3]
TabCount=0
"@
[IO.File]::WriteAllText((Join-Path $profileDirectory 'ExplorerDirs.ini'), $ini, [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ExplorerDirsSmokeNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo {
        public int Size; public uint Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    private delegate bool EnumWindowProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string text);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    public static List<IntPtr> Descendants(IntPtr parent) {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, (window, _) => { result.Add(window); return true; }, IntPtr.Zero);
        return result;
    }
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

function Send-Key([byte]$key) {
    Assert-TestForeground $script:app
    [ExplorerDirsSmokeNative]::keybd_event($key, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [ExplorerDirsSmokeNative]::keybd_event($key, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 140
}

function Send-Chord([byte[]]$modifiers, [byte]$key) {
    Assert-TestForeground $script:app
    foreach ($modifier in $modifiers) { [ExplorerDirsSmokeNative]::keybd_event($modifier, 0, 0, [UIntPtr]::Zero) }
    try {
        Start-Sleep -Milliseconds 40
        Send-Key $key
    }
    finally {
        foreach ($modifier in @($modifiers)[($modifiers.Length - 1)..0]) {
            [ExplorerDirsSmokeNative]::keybd_event($modifier, 0, 2, [UIntPtr]::Zero)
        }
    }
    Start-Sleep -Milliseconds 150
}

function Get-Class([IntPtr]$window) {
    $name = [Text.StringBuilder]::new(100)
    [ExplorerDirsSmokeNative]::GetClassName($window, $name, $name.Capacity) | Out-Null
    return $name.ToString()
}

function Get-FocusWindow([uint32]$threadId) {
    $info = [ExplorerDirsSmokeNative+GuiThreadInfo]::new()
    $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (-not [ExplorerDirsSmokeNative]::GetGUIThreadInfo($threadId, [ref]$info)) { throw 'GetGUIThreadInfo failed.' }
    return $info.Focus
}

function Assert-TestForeground([Diagnostics.Process]$app) {
    $foreground = [ExplorerDirsSmokeNative]::GetForegroundWindow()
    if ($foreground -eq $script:mainWindow) { return }
    [uint32]$owner = 0
    [ExplorerDirsSmokeNative]::GetWindowThreadProcessId($foreground, [ref]$owner) | Out-Null
    if ($owner -eq $app.Id -and (Get-Class $foreground) -in @('DirectUIHWND', 'Edit')) { return }
    $title = [Text.StringBuilder]::new(150)
    [ExplorerDirsSmokeNative]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
    throw "Desktop focus left the test app; stopping input (appPid=$($app.Id), foreground=$foreground, pid=$owner, class=$(Get-Class $foreground), title=$title)."
}

function Find-FileItem([IntPtr]$view, [string]$name) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($view)
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name))
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-FileViews([IntPtr]$mainWindow) {
    return @([ExplorerDirsSmokeNative]::Descendants($mainWindow) | ForEach-Object {
        if ((Get-Class $_) -ne 'DirectUIHWND') { return }
        $rect = [ExplorerDirsSmokeNative+Rect]::new()
        [ExplorerDirsSmokeNative]::GetWindowRect($_, [ref]$rect) | Out-Null
        if (($rect.Right - $rect.Left) -gt 200 -and ($rect.Bottom - $rect.Top) -gt 100) {
            [pscustomobject]@{ Handle = $_; Left = $rect.Left }
        }
    } | Sort-Object Left)
}

function Select-FileItem([Diagnostics.Process]$app, [IntPtr]$view, [string]$name) {
    Assert-TestForeground $app
    $item = Find-FileItem $view $name
    if ($null -eq $item) { throw "Shell item is not visible: $name" }
    $selection = [Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    $item.SetFocus()
    Start-Sleep -Milliseconds 180
    if ([Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne $name) {
        throw "Failed to focus Shell item: $name"
    }
}

function Focus-EmptyView([Diagnostics.Process]$app, [IntPtr]$view, [uint32]$threadId) {
    Assert-TestForeground $app
    $rect = [ExplorerDirsSmokeNative+Rect]::new()
    [ExplorerDirsSmokeNative]::GetWindowRect($view, [ref]$rect) | Out-Null
    [ExplorerDirsSmokeNative]::SetCursorPos(($rect.Left + $rect.Right) / 2, ($rect.Top + $rect.Bottom) / 2) | Out-Null
    [ExplorerDirsSmokeNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [ExplorerDirsSmokeNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 220
    if ((Get-FocusWindow $threadId) -ne $view) { throw 'Destination file view did not receive focus.' }
}

function Rename-FocusedEdit([Diagnostics.Process]$app, [uint32]$threadId, [string]$newName) {
    Wait-Until { (Get-Class (Get-FocusWindow $threadId)) -eq 'Edit' } 'Shell rename editor'
    Assert-TestForeground $app
    $editor = Get-FocusWindow $threadId
    [ExplorerDirsSmokeNative]::SendMessage($editor, 0x000C, [IntPtr]::Zero, $newName) | Out-Null
    Send-Key 0x0D
}

$originalCursor = [ExplorerDirsSmokeNative+Point]::new()
[ExplorerDirsSmokeNative]::GetCursorPos([ref]$originalCursor) | Out-Null
$originalClipboard = [Windows.Forms.Clipboard]::GetDataObject()
$clipboardSequence = $null
$app = $null
$checks = [Collections.Generic.List[string]]::new()
try {
    $app = Start-Process -FilePath $exe -Environment @{ EXPLORER_DIRS_FOCUS_TRACE = (Join-Path $runRoot 'focus.log') } -PassThru
    Wait-Until { $app.Refresh(); -not $app.HasExited -and $app.MainWindowHandle -ne [IntPtr]::Zero } 'isolated Explorer Dirs window'
    $script:mainWindow = $app.MainWindowHandle
    [uint32]$appProcessId = 0
    $threadId = [ExplorerDirsSmokeNative]::GetWindowThreadProcessId($script:mainWindow, [ref]$appProcessId)
    if ($appProcessId -ne $app.Id) { throw 'Window belongs to a different process.' }
    if ($app.MainWindowTitle -notlike 'Explorer Dirs*') { throw 'Unexpected test window.' }
    Write-Output "Isolated app PID=$($app.Id); output=$runRoot"
    [ExplorerDirsSmokeNative]::SetForegroundWindow($script:mainWindow) | Out-Null
    Assert-TestForeground $app

    Wait-Until {
        $script:views = Get-FileViews $script:mainWindow
        $views.Count -eq 2 -and $null -ne (Find-FileItem $views[0].Handle 'alpha.txt')
    } 'two isolated Shell file views'
    $left = [IntPtr]$views[0].Handle
    $right = [IntPtr]$views[1].Handle
    if ($null -ne (Find-FileItem $right 'alpha.txt')) { throw 'Destination was not empty.' }
    Write-Output 'Two isolated Shell views identified.'

    Select-FileItem $app $left 'alpha.txt'
    Write-Output 'Source alpha.txt selected.'
    Send-Chord @(0x11) 0x43 # Ctrl+C
    $copied = [Windows.Forms.Clipboard]::GetFileDropList()
    Write-Output "Shell clipboard file-drop count: $($copied.Count)"
    $clipboardSequence = [ExplorerDirsSmokeNative]::GetClipboardSequenceNumber()
    Focus-EmptyView $app $right $threadId
    Write-Output 'Destination view focused.'
    Send-Chord @(0x11) 0x56 # Ctrl+V
    Wait-Until { Test-Path -LiteralPath (Join-Path $destination 'alpha.txt') } 'cross-pane copy'
    if ([IO.File]::ReadAllText((Join-Path $destination 'alpha.txt')) -ne 'Explorer Dirs GUI smoke alpha') {
        throw 'Copied file content differs.'
    }
    $checks.Add('cross-pane Shell copy: PASS')

    $folderButton = [Windows.Automation.AutomationElement]::FromHandle($script:mainWindow).FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, '新建文件夹'),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
    if ($null -eq $folderButton) { throw 'New Folder toolbar button is missing.' }
    Assert-TestForeground $app
    ([Windows.Automation.InvokePattern]$folderButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-Until { @(Get-ChildItem -LiteralPath $destination -Directory).Count -eq 1 } 'Shell new folder'
    $checks.Add('Shell new folder: PASS')

    Wait-Until {
        $currentViews = Get-FileViews $script:mainWindow
        if ($currentViews.Count -ne 2) { return $false }
        $script:right = [IntPtr]$currentViews[1].Handle
        return $null -ne (Find-FileItem $script:right 'alpha.txt')
    } 'copied Shell item after folder rename'
    Select-FileItem $app $right 'alpha.txt'
    Send-Key 0x71 # F2
    Rename-FocusedEdit $app $threadId 'renamed.txt'
    Wait-Until { Test-Path -LiteralPath (Join-Path $destination 'renamed.txt') } 'Shell rename'
    if (Test-Path -LiteralPath (Join-Path $destination 'alpha.txt')) { throw 'Old name still exists after rename.' }
    $checks.Add('Shell rename: PASS')

    $checks | ForEach-Object { Write-Output $_ }
    Write-Output "Isolated files: $runRoot"
}
finally {
    [uint32]$foregroundOwner = 0
    [ExplorerDirsSmokeNative]::GetWindowThreadProcessId([ExplorerDirsSmokeNative]::GetForegroundWindow(), [ref]$foregroundOwner) | Out-Null
    if ($null -ne $app -and $foregroundOwner -eq $app.Id) {
        [ExplorerDirsSmokeNative]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null
    }
    if ($null -ne $clipboardSequence -and [ExplorerDirsSmokeNative]::GetClipboardSequenceNumber() -eq $clipboardSequence) {
        try {
            if ($null -ne $originalClipboard) { [Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
            else { [Windows.Forms.Clipboard]::Clear() }
        }
        catch { Write-Warning 'Could not restore the previous clipboard data.' }
    }
    if ($null -ne $app) {
        $app.Refresh()
        if (-not $app.HasExited) {
            $app.CloseMainWindow() | Out-Null
            if (-not $app.WaitForExit(5000) -and $app.Path -eq $exe) { Stop-Process -Id $app.Id -Force }
        }
    }
}
