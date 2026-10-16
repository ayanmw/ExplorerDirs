param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,
    [ValidateRange(2, 3600)]
    [int]$Samples = 12,
    [ValidateRange(100, 60000)]
    [int]$IntervalMilliseconds = 5000,
    [switch]$AllowOtherProcess,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$process = [Diagnostics.Process]::GetProcessById($ProcessId)
try {
    $process.Refresh()
    if ($process.HasExited) { throw "Process $ProcessId has already exited." }
    if (-not $AllowOtherProcess -and $process.ProcessName -ne 'ExplorerDirs.App') {
        throw "Process $ProcessId is not ExplorerDirs.App."
    }
    $processStart = $process.StartTime.ToUniversalTime()
    $origin = if ($AllowOtherProcess -and $process.ProcessName -ne 'ExplorerDirs.App') {
        'other-process'
    } else { 'other-copy' }
    $binarySha256 = $null
    try {
        $executable = [IO.Path]::GetFullPath($process.MainModule.FileName)
        $releaseRoot = (Join-Path $projectRoot 'releases') + [IO.Path]::DirectorySeparatorChar
        $sourceRoot = (Join-Path $projectRoot 'src') + [IO.Path]::DirectorySeparatorChar
        if ($process.ProcessName -eq 'ExplorerDirs.App') {
            if ($executable.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) { $origin = 'release' }
            elseif ($executable.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { $origin = 'source-test' }
        }
        $binarySha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    }
    catch { } # A protected executable can still be sampled by PID.

    $rows = [Collections.Generic.List[object]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $status = 'completed'
    $previousSeconds = $null
    $previousCpuSeconds = $null
    for ($index = 0; $index -lt $Samples; $index++) {
        if ($index -gt 0) { Start-Sleep -Milliseconds $IntervalMilliseconds }
        try {
            $process.Refresh()
            if ($process.HasExited) { $status = 'exited'; break }
            if ($process.StartTime.ToUniversalTime() -ne $processStart) { $status = 'identity-changed'; break }
            $seconds = $watch.Elapsed.TotalSeconds
            $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
            $cpuPercent = $null
            if ($null -ne $previousSeconds -and $seconds -gt $previousSeconds) {
                $cpuPercent = [Math]::Round(100 * ($cpuSeconds - $previousCpuSeconds) /
                    ($seconds - $previousSeconds) / [Environment]::ProcessorCount, 2)
            }
            $rows.Add([pscustomobject]@{
                TimeUtc = [DateTime]::UtcNow.ToString('o')
                ElapsedSeconds = [Math]::Round($seconds, 3)
                WorkingSetBytes = [long]$process.WorkingSet64
                PrivateBytes = [long]$process.PrivateMemorySize64
                TotalCpuSeconds = [Math]::Round($cpuSeconds, 3)
                CpuPercentOfMachine = $cpuPercent
                HandleCount = [int]$process.HandleCount
                ThreadCount = [int]$process.Threads.Count
            })
            $previousSeconds = $seconds
            $previousCpuSeconds = $cpuSeconds
        }
        catch [System.InvalidOperationException] { $status = 'exited'; break }
    }
    $result = [pscustomobject]@{
        SchemaVersion = 1
        ProcessId = $ProcessId
        ProcessName = $process.ProcessName
        Origin = $origin
        ExecutableSha256 = $binarySha256
        ProcessStartUtc = $processStart.ToString('o')
        Status = $status
        RequestedSamples = $Samples
        CollectedSamples = $rows.Count
        IntervalMilliseconds = $IntervalMilliseconds
        PeakWorkingSetBytes = if ($rows.Count) { ($rows | Measure-Object -Property WorkingSetBytes -Maximum).Maximum } else { $null }
        PeakPrivateBytes = if ($rows.Count) { ($rows | Measure-Object -Property PrivateBytes -Maximum).Maximum } else { $null }
        Samples = @($rows)
    }
    if ($AsJson) { $result | ConvertTo-Json -Depth 5 }
    else { $result }
}
finally { $process.Dispose() }
