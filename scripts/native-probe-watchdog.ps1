param(
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30,
    [ValidateSet('Desktop', 'ThisPC', 'RecycleBin', 'Libraries', 'NetworkPlaces', 'SearchFolder', 'ZipContainer')]
    [string]$VirtualScope,
    [ValidateSet('color', 'color-offscreen', 'command-state-readonly', 'command-state-readonly-private-desktop', 'auto-column-width', 'photo-print-verb', 'host-filter-private-desktop', 'host-filter-virtual-private-desktop', 'host-menu-commands-private-desktop', 'menu-activation-private-desktop', 'color-private-desktop', 'color-readonly-rule-private-desktop', 'common-listview-color-baseline-private-desktop')]
    [string]$Probe = 'color',
    [switch]$TestTimeoutPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runTimeoutSeconds = $TimeoutSeconds
if ($TestTimeoutPath) {
    $executable = (Get-Command pwsh -ErrorAction Stop).Source
    $arguments = '-NoProfile -Command "Start-Sleep -Seconds 30"'
    $runTimeoutSeconds = 5
}
else {
    $probeAssembly = Join-Path $repoRoot 'src/ExplorerDirs.NativeProbe/bin/Release/net10.0-windows/ExplorerDirs.NativeProbe.dll'
    if (-not (Test-Path -LiteralPath $probeAssembly -PathType Leaf)) {
        throw "Build ExplorerDirs.NativeProbe in Release first: $probeAssembly"
    }

    $executable = (Get-Command dotnet -ErrorAction Stop).Source
    $probeArgument = switch ($Probe) {
        'auto-column-width' { '--auto-column-width' }
        'photo-print-verb' { '--photo-print-verb' }
        'command-state-readonly-private-desktop' { '--ishellview-command-state-readonly-private-desktop' }
        'host-filter-private-desktop' { '--ishellview-host-filter-private-desktop' }
        'host-menu-commands-private-desktop' { '--ishellview-menu-command-private-desktop' }
        'menu-activation-private-desktop' { '--ishellview-menu-activation-private-desktop' }
        'host-filter-virtual-private-desktop' {
            if ([string]::IsNullOrWhiteSpace($VirtualScope)) { throw 'Set -VirtualScope for the selected probe.' }
            '--ishellview-host-filter-virtual-private-desktop "{0}"' -f $VirtualScope
        }
        'color-private-desktop' { '--ishellview-color-spike-private-desktop' }
        'color-readonly-rule-private-desktop' { '--ishellview-color-readonly-private-desktop' }
        'command-state-readonly' { '--ishellview-command-state-readonly' }
        'common-listview-color-baseline-private-desktop' { '--common-listview-color-baseline-private-desktop' }
        'color-offscreen' { '--ishellview-color-spike-offscreen' }
        default { '--ishellview-color-spike' }
    }
    $arguments = '"{0}" {1}' -f $probeAssembly, $probeArgument
}

$logDirectory = Join-Path $repoRoot 'src/.state/native-probe-watchdog'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$stdoutPath = Join-Path $logDirectory "$runId.stdout.log"
$stderrPath = Join-Path $logDirectory "$runId.stderr.log"
# Keep native Shell probes outside the app process so a probe crash or hang
# cannot terminate Explorer Dirs itself.
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $repoRoot `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

if (-not $process.WaitForExit($runTimeoutSeconds * 1000)) {
    try {
        if (-not $process.HasExited) { $process.Kill() }
    }
    catch {
        if (-not $process.HasExited) { throw }
    }
    $process.WaitForExit()
    if ($TestTimeoutPath) {
        Write-Output "selftest=timeout-path-passed; seconds=$runTimeoutSeconds; child-exited=$($process.HasExited); stdout=$stdoutPath; stderr=$stderrPath"
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
Write-Output "status=exited; exitCode=$exitCode; stdout=$stdoutPath; stderr=$stderrPath"
if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Encoding oem }
if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Encoding oem }
if ($TestTimeoutPath) {
    Write-Error 'The watchdog timeout self-test child exited before its deadline.'
    exit 1
}
exit $exitCode
