$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sampler = Join-Path $projectRoot 'scripts\perf-sample.ps1'
$sampledPid = $PID

$raw = powershell.exe -NoProfile -File $sampler -ProcessId $sampledPid `
    -AllowOtherProcess -Samples 3 -IntervalMilliseconds 100 -AsJson | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Process sampler failed.' }
$report = $raw | ConvertFrom-Json
if ($report.Status -ne 'completed' -or $report.CollectedSamples -ne 3 -or
    $report.Samples.Count -ne 3 -or $report.Origin -ne 'other-process' -or
    $report.PeakWorkingSetBytes -le 0 -or $report.PeakPrivateBytes -le 0 -or
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
'Performance sampler smoke: PASS'
