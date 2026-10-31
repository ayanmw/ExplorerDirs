param(
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30,
    [ValidateSet('Desktop', 'ThisPC', 'RecycleBin', 'ControlPanel', 'Libraries', 'NetworkPlaces', 'SearchFolder', 'ZipContainer')]
    [string]$VirtualScope,
    [string]$Probe = 'color',
    [switch]$TestTimeoutPath,
    [string]$TimeoutDumpPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runTimeoutSeconds = $TimeoutSeconds
$syntheticShellProbe = $Probe -in @('synthetic-shell-size-sort-private-desktop', 'synthetic-shell-two-view-target-sync-private-desktop', 'synthetic-shell-focus-recovery-private-desktop', 'synthetic-recursive-directory-size-private-desktop', 'synthetic-recursive-directory-size-slow-column-private-desktop', 'synthetic-recursive-directory-size-wpf-host-private-desktop')
$timeoutTreeScriptPath = $null
$timeoutTreePidPath = $null
if ($TestTimeoutPath) {
    $executable = (Get-Command pwsh -ErrorAction Stop).Source
    $timeoutTreeScriptPath = Join-Path $env:TEMP "ExplorerDirs-watchdog-tree-$([Guid]::NewGuid().ToString('N')).ps1"
    $timeoutTreePidPath = "$timeoutTreeScriptPath.child-pid"
    @'
$child = Start-Process -FilePath (Get-Command pwsh -ErrorAction Stop).Source `
    -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') -PassThru -WindowStyle Hidden
[IO.File]::WriteAllText($args[0], [string]$child.Id)
$child.WaitForExit()
'@ | ForEach-Object { [IO.File]::WriteAllText($timeoutTreeScriptPath, $_, [Text.UTF8Encoding]::new($false)) }
    $arguments = '-NoProfile -File "{0}" "{1}"' -f $timeoutTreeScriptPath, $timeoutTreePidPath
    $runTimeoutSeconds = 5
}
else {
    $probeAssembly = Join-Path $repoRoot 'src/ExplorerDirs.NativeProbe/bin/Release/net10.0-windows/ExplorerDirs.NativeProbe.dll'
    if (-not (Test-Path -LiteralPath $probeAssembly -PathType Leaf)) {
        throw "Build ExplorerDirs.NativeProbe in Release first: $probeAssembly"
    }

    $executable = (Get-Command dotnet -ErrorAction Stop).Source
    $probeArgument = switch ($Probe) {
        'color' { '--ishellview-color-spike' }
        'color-offscreen' { '--ishellview-color-spike-offscreen' }
        'static-shell-column-adapter-private-desktop' { '--static-shell-column-adapter-private-desktop' }
        'static-shell-column-adapter-activated-strict-private-desktop' { '--static-shell-column-adapter-activated-strict-private-desktop' }
        'static-shell-column-adapter-strict-private-desktop' { '--static-shell-column-adapter-strict-private-desktop' }
        'static-shell-column-classid-private-desktop' { '--static-shell-column-classid-private-desktop' }
        'static-shell-item-bound-view-private-desktop' { '--static-shell-item-bound-view-private-desktop' }
        'synthetic-shell-size-sort-private-desktop' { '--synthetic-shell-size-sort-private-desktop' }
        'synthetic-shell-two-view-target-sync-private-desktop' { '--synthetic-shell-two-view-target-sync-private-desktop' }
        'synthetic-shell-focus-recovery-private-desktop' { '--synthetic-shell-focus-recovery-private-desktop' }
        'synthetic-recursive-directory-size-private-desktop' { '--synthetic-recursive-directory-size-private-desktop' }
        'synthetic-recursive-directory-size-slow-column-private-desktop' { '--synthetic-recursive-directory-size-slow-column-private-desktop' }
        'synthetic-recursive-directory-size-wpf-host-private-desktop' { '--synthetic-recursive-directory-size-wpf-host-private-desktop' }
        'auto-column-width' { '--auto-column-width' }
        'photo-print-verb' { '--photo-print-verb' }
        'command-state-readonly-private-desktop' { '--ishellview-command-state-readonly-private-desktop' }
        'host-filter-private-desktop' { '--ishellview-host-filter-private-desktop' }
        'host-menu-commands-private-desktop' { '--ishellview-menu-command-private-desktop' }
        'host-newfolder-private-desktop' { '--ishellview-newfolder-private-desktop' }
        'menu-activation-private-desktop' { '--ishellview-menu-activation-private-desktop' }
        'session-restore-private-desktop' { '--ishellview-session-restore-private-desktop' }
        'session-restore-sync-cleanup-private-desktop' { '--ishellview-session-restore-sync-cleanup-private-desktop' }
        'private-desktop-window-destroy-control' { '--private-desktop-window-destroy-control' }
        'private-desktop-window-visibility-control' { '--private-desktop-window-visibility-control' }
        'private-desktop-shared-window-destroy-control' { '--private-desktop-shared-window-destroy-control' }
        'private-desktop-shell-view-window-destroy-control' { '--private-desktop-shell-view-window-destroy-control' }
        'private-desktop-single-pane-sync-cleanup' { '--private-desktop-single-pane-sync-cleanup' }
        'private-desktop-2pane-sync-cleanup' { '--private-desktop-multi-pane-sync-cleanup 2' }
        'private-desktop-3pane-sync-cleanup' { '--private-desktop-multi-pane-sync-cleanup 3' }
        'private-desktop-4pane-sync-cleanup' { '--private-desktop-multi-pane-sync-cleanup 4' }
        'private-desktop-4pane-menu-sync-cleanup' { '--private-desktop-four-pane-menu-sync-cleanup' }
        'private-desktop-single-pane-menu-sync-cleanup' { '--private-desktop-single-pane-menu-sync-cleanup' }
        'private-desktop-single-pane-menu-nofocus-sync-cleanup' { '--private-desktop-single-pane-menu-nofocus-sync-cleanup' }
        'private-desktop-single-pane-menu-active-sync-cleanup' { '--private-desktop-single-pane-menu-active-sync-cleanup' }
        'private-desktop-single-pane-menu-detached-ime-sync-cleanup' { '--private-desktop-single-pane-menu-detached-ime-sync-cleanup' }
        'private-desktop-single-pane-menu-disabled-ime-sync-cleanup' { '--private-desktop-single-pane-menu-disabled-ime-sync-cleanup' }
        'private-desktop-single-pane-menu-ole-init-sync-cleanup' { '--private-desktop-single-pane-menu-ole-init-sync-cleanup' }
        'private-desktop-single-pane-menu-wpf-dispatcher-sync-cleanup' { '--private-desktop-single-pane-menu-wpf-dispatcher-sync-cleanup' }
        'private-desktop-single-pane-menu-wpf-ole-sync-cleanup' { '--private-desktop-single-pane-menu-wpf-ole-sync-cleanup' }
        'private-desktop-single-pane-menu-wpf-host-sync-cleanup' { '--private-desktop-single-pane-menu-wpf-host-sync-cleanup' }
        'private-desktop-four-pane-menu-wpf-host-sync-cleanup' { '--private-desktop-four-pane-menu-wpf-host-sync-cleanup' }
        'private-desktop-four-pane-wpf-host-keyboard-consumer' { '--private-desktop-four-pane-wpf-host-keyboard-consumer' }
        'private-desktop-four-pane-wpf-host-directory-isolation' { '--private-desktop-four-pane-wpf-host-directory-isolation' }
        'private-desktop-single-pane-menu-detached-view-ime-sync-cleanup' { '--private-desktop-single-pane-menu-detached-view-ime-sync-cleanup' }
        'private-desktop-single-pane-menu-late-coordinator-dispose' { '--private-desktop-single-pane-menu-late-coordinator-dispose' }
        'drop-target-private-desktop' { '--shell-view-drop-target-private-desktop' }
        'host-filter-virtual-private-desktop' {
            if ([string]::IsNullOrWhiteSpace($VirtualScope)) { throw 'Set -VirtualScope for the selected probe.' }
            '--ishellview-host-filter-virtual-private-desktop "{0}"' -f $VirtualScope
        }
        'color-private-desktop' { '--ishellview-color-spike-private-desktop' }
        'color-zip-private-desktop' { '--ishellview-zip-color-pixels-private-desktop' }
        'color-this-pc-private-desktop' { '--ishellview-this-pc-color-pixels-private-desktop' }
        'color-libraries-private-desktop' { '--ishellview-libraries-color-pixels-private-desktop' }
        'color-desktop-private-desktop' { '--ishellview-desktop-color-pixels-private-desktop' }
        'color-readonly-rule-private-desktop' { '--ishellview-color-readonly-private-desktop' }
        'color-directory-rule-private-desktop' { '--ishellview-color-directory-private-desktop' }
        'color-hidden-rule-private-desktop' { '--ishellview-color-hidden-private-desktop' }
        'zip-background-menu-private-desktop' { '--ishellview-zip-background-menu-private-desktop' }
        'zip-extract-private-desktop' { '--ishellview-zip-extract-private-desktop' }
        'zip-copy-items-private-desktop' { '--ishellview-zip-copy-items-private-desktop' }
        'network-places-row-map-private-desktop' { '--ishellview-network-places-row-map-private-desktop' }
        'network-places-color-pixels-private-desktop' { '--ishellview-network-places-color-pixels-private-desktop' }
        'network-places-child-drop-targets-private-desktop' { '--ishellview-network-places-child-drop-targets-private-desktop' }
        'command-state-readonly' { '--ishellview-command-state-readonly' }
        'common-listview-color-baseline-private-desktop' { '--common-listview-color-baseline-private-desktop' }
        default { throw "Unsupported NativeProbe watchdog probe: $Probe" }
    }
    $arguments = '"{0}" {1}' -f $probeAssembly, $probeArgument
}
$logDirectory = Join-Path $repoRoot 'src/.state/native-probe-watchdog'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$timeoutDumpPath = $TimeoutDumpPath
if (-not [string]::IsNullOrWhiteSpace($timeoutDumpPath) -and -not [IO.Path]::IsPathFullyQualified($timeoutDumpPath)) {
    throw 'TimeoutDumpPath must be an absolute path.'
}
if (-not $TestTimeoutPath -and -not [string]::IsNullOrWhiteSpace($timeoutDumpPath)) {
    if ($Probe -in @('synthetic-recursive-directory-size-private-desktop', 'synthetic-recursive-directory-size-slow-column-private-desktop', 'synthetic-recursive-directory-size-wpf-host-private-desktop')) {
        # This view probe has an internal 30-second deadline; capture first so the outer watchdog owns the dump.
        $runTimeoutSeconds = [Math]::Min($runTimeoutSeconds, 25)
    }
    else {
        # Other probes retain time for their own timeout diagnostics before this external fallback.
        $runTimeoutSeconds += 10
    }
}
$timeoutDumpWriterLoaded = $false
function Save-TimeoutDumpFromWatchdog([int]$ProcessId, [string]$Path) {
    if (-not $script:timeoutDumpWriterLoaded) {
        $source = @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class ExplorerDirsTimeoutDumpWriter {
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("dbghelp.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file,
        uint dumpType, IntPtr exceptionInformation, IntPtr userStreamInformation, IntPtr callbackInformation);

    public static long Capture(int processId, string path) {
        const uint processQueryInformation = 0x0400;
        const uint processVmRead = 0x0010;
        var process = OpenProcess(processQueryInformation | processVmRead, false, processId);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed.");
        try {
            var fullPath = Path.GetFullPath(path);
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                const uint miniDumpWithThreadInfo = 0x00001000;
                if (!MiniDumpWriteDump(process, (uint)processId, stream.SafeFileHandle, miniDumpWithThreadInfo,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "MiniDumpWriteDump failed.");
                stream.Flush(true);
                return stream.Length;
            }
        }
        finally { CloseHandle(process); }
    }
}
'@
        Add-Type -TypeDefinition $source -Language CSharp
        $script:timeoutDumpWriterLoaded = $true
    }

    $dumpBytes = [ExplorerDirsTimeoutDumpWriter]::Capture($ProcessId, $Path)
    Write-Output "timeout-minidump=captured; pid=$ProcessId; bytes=$dumpBytes; path=$Path; writer=watchdog-process"
}
$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$stdoutPath = Join-Path $logDirectory "$runId.stdout.log"
$stderrPath = Join-Path $logDirectory "$runId.stderr.log"
$syntheticRunId = if ($syntheticShellProbe) { [Guid]::NewGuid().ToString('N') } else { $null }
$syntheticClassKey = if ($syntheticShellProbe) { "HKCU:\Software\Classes\CLSID\{$([Guid]::ParseExact($syntheticRunId, 'N').ToString('D'))}" } else { $null }
$syntheticNamespaceKey = if ($syntheticShellProbe) { "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{$([Guid]::ParseExact($syntheticRunId, 'N').ToString('D'))}" } else { $null }
$syntheticBackingFolder = if ($syntheticShellProbe) { Join-Path ([IO.Path]::GetTempPath()) "ExplorerDirs-SyntheticShellSort-$syntheticRunId" } else { $null }
$syntheticCopyDestination = if ($syntheticShellProbe) { Join-Path ([IO.Path]::GetTempPath()) "ExplorerDirs-SyntheticShellSort-Copy-$syntheticRunId" } else { $null }
if ($syntheticShellProbe) {
    if (Test-Path -LiteralPath $syntheticClassKey) {
        throw "Refusing synthetic Shell sort probe because its reserved CLSID key already exists: $syntheticClassKey"
    }
    if (Test-Path -LiteralPath $syntheticNamespaceKey) {
        throw "Refusing synthetic Shell sort probe because its reserved Desktop namespace key already exists: $syntheticNamespaceKey"
    }
    if (Test-Path -LiteralPath $syntheticBackingFolder) {
        throw "Refusing synthetic Shell sort probe because its reserved fixture directory already exists: $syntheticBackingFolder"
    }
    if (Test-Path -LiteralPath $syntheticCopyDestination) {
        throw "Refusing synthetic Shell sort probe because its reserved copy destination already exists: $syntheticCopyDestination"
    }
}
function Remove-SyntheticProbeArtifacts {
    $ownerMarker = 'ExplorerDirs isolated synthetic sort probe'
    foreach ($keyPath in @($syntheticNamespaceKey, $syntheticClassKey)) {
        if (-not (Test-Path -LiteralPath $keyPath)) { continue }
        $registeredOwner = (Get-Item -LiteralPath $keyPath).GetValue('')
        if ($registeredOwner -cne $ownerMarker) {
            Write-Warning "Leaving synthetic probe registry key with a different owner marker: $keyPath"
            continue
        }
        Remove-Item -LiteralPath $keyPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    foreach ($directoryPath in @($syntheticBackingFolder, $syntheticCopyDestination)) {
        if (Test-Path -LiteralPath $directoryPath -PathType Container) {
            Remove-Item -LiteralPath $directoryPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
# Keep native Shell probes outside the app process so a probe crash or hang
# cannot terminate Explorer Dirs itself.
$previousSyntheticRunId = [Environment]::GetEnvironmentVariable('EXPLORER_DIRS_NATIVEPROBE_RUN_ID', 'Process')
try {
    if ($syntheticShellProbe) {
        [Environment]::SetEnvironmentVariable('EXPLORER_DIRS_NATIVEPROBE_RUN_ID', $syntheticRunId, 'Process')
    }
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
}
finally {
    [Environment]::SetEnvironmentVariable('EXPLORER_DIRS_NATIVEPROBE_RUN_ID', $previousSyntheticRunId, 'Process')
}

if (-not $process.WaitForExit($runTimeoutSeconds * 1000)) {
    if (-not [string]::IsNullOrWhiteSpace($timeoutDumpPath)) {
        try {
            Save-TimeoutDumpFromWatchdog -ProcessId $process.Id -Path $timeoutDumpPath
        }
        catch {
            Write-Warning "Watchdog could not capture timeout dump for PID $($process.Id): $($_.Exception.Message)"
        }
    }
    try {
        # Process.Kill(bool) is unavailable in Windows PowerShell 5.1; this
        # watchdog owns the direct probe process and kills any test grandchild below explicitly.
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    catch {
        if (-not $process.HasExited) { throw }
    }
    $process.WaitForExit()
    if ($syntheticShellProbe) {
        Remove-SyntheticProbeArtifacts
    }
    if ($TestTimeoutPath) {
        $grandchildId = if (Test-Path -LiteralPath $timeoutTreePidPath) {
            [int](Get-Content -LiteralPath $timeoutTreePidPath -Raw)
        }
        else { 0 }
        $grandchildStillRunning = $grandchildId -gt 0 -and $null -ne (Get-Process -Id $grandchildId -ErrorAction SilentlyContinue)
        if ($grandchildStillRunning) {
            Stop-Process -Id $grandchildId -Force
            $stopDeadline = (Get-Date).AddSeconds(5)
            do {
                Start-Sleep -Milliseconds 50
                $grandchildStillRunning = $null -ne (Get-Process -Id $grandchildId -ErrorAction SilentlyContinue)
            } while ($grandchildStillRunning -and (Get-Date) -lt $stopDeadline)
        }
        $grandchildStillRunning = $grandchildId -gt 0 -and $null -ne (Get-Process -Id $grandchildId -ErrorAction SilentlyContinue)
        Remove-Item -LiteralPath $timeoutTreeScriptPath, $timeoutTreePidPath -Force -ErrorAction SilentlyContinue
        if ($grandchildId -eq 0 -or $grandchildStillRunning) {
            Write-Error "Watchdog timeout did not terminate its process tree (grandchildId=$grandchildId; stillRunning=$grandchildStillRunning)."
            exit 1
        }
        Write-Output "selftest=timeout-tree-passed; seconds=$runTimeoutSeconds; parent-exited=$($process.HasExited); grandchild-exited=True; stdout=$stdoutPath; stderr=$stderrPath"
    }
    else {
        Write-Output "status=timeout; seconds=$runTimeoutSeconds; exitCode=124; stdout=$stdoutPath; stderr=$stderrPath"
    }
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Encoding oem }
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Encoding oem }
    if ($TestTimeoutPath) { exit 0 }
    exit 124
}

$process.WaitForExit() # Drain redirected output after process termination.
$exitCode = $process.ExitCode
if ($syntheticShellProbe) {
    Remove-SyntheticProbeArtifacts
}
Write-Output "status=exited; exitCode=$exitCode; stdout=$stdoutPath; stderr=$stderrPath"
if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Encoding oem }
if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Encoding oem }
if ($TestTimeoutPath) {
    Write-Error 'The watchdog timeout self-test child exited before its deadline.'
    exit 1
}
exit $exitCode
