param(
    [ValidateRange(0, 2147483647)]
    [int]$ReleaseProcessId = 0
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectRoot = $workspaceRoot
$commonGitDir = (& git -C $workspaceRoot rev-parse --path-format=absolute --git-common-dir 2>$null)
if ($LASTEXITCODE -eq 0 -and $commonGitDir) {
    $commonGitDir = [IO.Path]::GetFullPath(([string]$commonGitDir).Trim())
    if ([IO.Path]::GetFileName($commonGitDir) -eq '.git') {
        $projectRoot = [IO.Path]::GetDirectoryName($commonGitDir)
    }
}
$sampler = Join-Path $workspaceRoot 'scripts\perf-sample.ps1'
$sampledPid = $PID
$expectedExecutablePath = [IO.Path]::GetFullPath((Get-Process -Id $sampledPid).Path)

$raw = powershell.exe -NoProfile -File $sampler -ProcessId $sampledPid `
    -AllowOtherProcess -Samples 3 -IntervalMilliseconds 100 -AsJson | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Process sampler failed.' }
$report = $raw | ConvertFrom-Json
if ($report.SchemaVersion -ne 3 -or $report.Status -ne 'completed' -or $report.CollectedSamples -ne 3 -or
    $report.Samples.Count -ne 3 -or $report.Origin -ne 'other-process' -or
    $report.ExecutablePath -ne $expectedExecutablePath -or
    $report.PeakWorkingSetBytes -le 0 -or $report.PeakPrivateBytes -le 0 -or
    $report.Samples[0].WorkingSetDeltaFromFirstBytes -ne 0 -or
    $report.Samples[0].PrivateBytesDeltaFromFirstBytes -ne 0 -or
    $report.Samples[2].PrivateBytesDeltaFromFirstBytes -ne $report.FinalPrivateBytesDeltaBytes -or
    $report.Samples[2].WorkingSetDeltaFromFirstBytes -ne $report.FinalWorkingSetDeltaBytes -or
    $report.FinalPrivateBytesDeltaBytesPerMinute -ne $report.Samples[2].PrivateBytesDeltaBytesPerMinute -or
    $report.Samples[1].ElapsedSeconds -le $report.Samples[0].ElapsedSeconds -or
    $raw.Contains($projectRoot)) {
    throw 'Process sampler returned invalid data or leaked its executable path.'
}

$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $forbidden = powershell.exe -NoProfile -File $sampler -ProcessId $sampledPid `
        -Samples 2 -IntervalMilliseconds 100 -AsJson 2>&1 | Out-String
    $forbiddenExit = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorAction }
if ($forbiddenExit -eq 0) { throw 'Non-app process was sampled without opt-in.' }

if ($ReleaseProcessId -gt 0) {
    $expectedReleasePath = [IO.Path]::GetFullPath((Get-Process -Id $ReleaseProcessId).Path)
    $releaseRaw = powershell.exe -NoProfile -File $sampler -ProcessId $ReleaseProcessId `
        -Samples 2 -IntervalMilliseconds 100 -AsJson | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Release process sampler failed.' }
    $releaseReport = $releaseRaw | ConvertFrom-Json
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'releases')) + [IO.Path]::DirectorySeparatorChar
    if ($releaseReport.SchemaVersion -ne 3 -or $releaseReport.Origin -ne 'release' -or
        $releaseReport.ExecutablePath -ne $expectedReleasePath -or
        -not $expectedReleasePath.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release process was not identified against the main repository releases directory.'
    }
}
'Performance sampler smoke: PASS'
