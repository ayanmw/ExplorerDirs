param(
    [ValidateRange(1, 86400)]
    [int]$DurationSeconds = 300,

    [ValidateRange(1, 3600)]
    [int]$IntervalSeconds = 5,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $sampleDirectory = Join-Path $PSScriptRoot '..\src\.state\memory-samples'
    $sampleDirectory = [System.IO.Path]::GetFullPath($sampleDirectory)
    [System.IO.Directory]::CreateDirectory($sampleDirectory) | Out-Null
    $OutputPath = Join-Path $sampleDirectory ("ExplorerDirs-memory-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
    if (-not [string]::IsNullOrEmpty($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }
}

$processes = @(Get-Process -Name 'ExplorerDirs.App' -ErrorAction SilentlyContinue)
if ($processes.Count -eq 0) {
    [Console]::Error.WriteLine('No running ExplorerDirs.App process was found. The sampler never launches the application.')
    exit 2
}

$header = 'timestamp_utc,elapsed_seconds,pid,process_start_utc,working_set_bytes,private_bytes,virtual_bytes,handle_count,thread_count'
$writer = [System.IO.StreamWriter]::new($OutputPath, $false, [System.Text.UTF8Encoding]::new($false))
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$rowCount = 0

try {
    $writer.WriteLine($header)
    do {
        $timestamp = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        $processes = @(Get-Process -Name 'ExplorerDirs.App' -ErrorAction SilentlyContinue)
        if ($processes.Count -eq 0 -and $rowCount -gt 0) { break }
        foreach ($process in $processes) {
            try {
                $process.Refresh()
                $startTime = $process.StartTime.ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
                $elapsed = $clock.Elapsed.TotalSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)
                $row = @(
                    $timestamp,
                    $elapsed,
                    $process.Id,
                    $startTime,
                    $process.WorkingSet64,
                    $process.PrivateMemorySize64,
                    $process.VirtualMemorySize64,
                    $process.HandleCount,
                    $process.Threads.Count
                ) -join ','
                $writer.WriteLine($row)
                $rowCount++
            }
            catch [System.InvalidOperationException] {
                # The process exited between enumeration and sampling.
            }
        }

        $writer.Flush()
        $remaining = $DurationSeconds - $clock.Elapsed.TotalSeconds
        if ($remaining -le 0) { break }
        $sleepSeconds = [Math]::Min($IntervalSeconds, [Math]::Ceiling($remaining))
        Start-Sleep -Seconds ([int]$sleepSeconds)
    } while ($true)
}
finally {
    $writer.Dispose()
    $clock.Stop()
}

if ($rowCount -eq 0) {
    [Console]::Error.WriteLine('No process samples were captured; the process may have exited before the first sample.')
    exit 3
}

Write-Output ("Captured {0} samples for ExplorerDirs.App to {1}" -f $rowCount, $OutputPath)
