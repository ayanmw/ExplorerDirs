param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,
    [ValidateRange(2, 3600)]
    [int]$Samples = 12,
    [ValidateRange(100, 60000)]
    [int]$IntervalMilliseconds = 5000,
    [string]$WorkloadDescription,
    [switch]$AllowOtherProcess,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
if (-not ('ExplorerDirs.Diagnostics.GuiResources' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ExplorerDirs.Diagnostics {
    public static class GuiResources {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        private static extern void ResetLastError(uint error);
        public static uint? Read(IntPtr process, uint flags) {
            ResetLastError(0);
            uint count = GetGuiResources(process, flags);
            return count != 0 || Marshal.GetLastWin32Error() == 0 ? (uint?)count : null;
        }
    }
}
'@
}
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
try {
    $commonGitDir = (& git -C $PSScriptRoot rev-parse --path-format=absolute --git-common-dir 2>$null)
    if ($LASTEXITCODE -eq 0 -and $commonGitDir) {
        $commonGitDir = [IO.Path]::GetFullPath(([string]$commonGitDir).Trim())
        if ([IO.Path]::GetFileName($commonGitDir) -eq '.git') {
            $projectRoot = [IO.Path]::GetDirectoryName($commonGitDir)
        }
    }
}
catch { } # Keep working in source archives that do not include Git metadata.
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
    $executablePath = $null
    $binarySha256 = $null
    $managedAssemblyPath = $null
    $managedAssemblySha256 = $null
    $managedAssemblyProductVersion = $null
    try {
        $executablePath = [IO.Path]::GetFullPath($process.MainModule.FileName)
        $releaseRoot = (Join-Path $projectRoot 'releases') + [IO.Path]::DirectorySeparatorChar
        $sourceRoot = (Join-Path $projectRoot 'src') + [IO.Path]::DirectorySeparatorChar
        if ($process.ProcessName -eq 'ExplorerDirs.App') {
            if ($executablePath.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) { $origin = 'release' }
            elseif ($executablePath.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { $origin = 'source-test' }
        }
        $binarySha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        if ($process.ProcessName -eq 'ExplorerDirs.App') {
            $assemblyPath = Join-Path ([IO.Path]::GetDirectoryName($executablePath)) 'ExplorerDirs.App.dll'
            if (Test-Path -LiteralPath $assemblyPath -PathType Leaf) {
                $managedAssemblyPath = $assemblyPath
                $managedAssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
                $managedAssemblyProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
            }
        }
    }
    catch { } # A protected executable can still be sampled by PID.

    $rows = [Collections.Generic.List[object]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $status = 'completed'
    $previousSeconds = $null
    $previousCpuSeconds = $null
    $firstWorkingSetBytes = $null
    $firstPrivateBytes = $null
    for ($index = 0; $index -lt $Samples; $index++) {
        if ($index -gt 0) { Start-Sleep -Milliseconds $IntervalMilliseconds }
        try {
            $process.Refresh()
            if ($process.HasExited) { $status = 'exited'; break }
            if ($process.StartTime.ToUniversalTime() -ne $processStart) { $status = 'identity-changed'; break }
            $seconds = $watch.Elapsed.TotalSeconds
            $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
            $workingSetBytes = [long]$process.WorkingSet64
            $privateBytes = [long]$process.PrivateMemorySize64
            $gdiObjects = $null
            $userObjects = $null
            try {
                $gdiObjects = [ExplorerDirs.Diagnostics.GuiResources]::Read($process.Handle, 0)
                $userObjects = [ExplorerDirs.Diagnostics.GuiResources]::Read($process.Handle, 1)
            }
            catch [ComponentModel.Win32Exception] { } # Keep memory sampling when GUI queries are unavailable.
            if ($index -eq 0) {
                $firstWorkingSetBytes = $workingSetBytes
                $firstPrivateBytes = $privateBytes
            }
            $workingSetDelta = $workingSetBytes - $firstWorkingSetBytes
            $privateBytesDelta = $privateBytes - $firstPrivateBytes
            $privateBytesDeltaPerMinute = if ($seconds -gt 0) {
                [Math]::Round($privateBytesDelta / $seconds * 60, 2)
            } else { $null }
            $cpuPercent = $null
            if ($null -ne $previousSeconds -and $seconds -gt $previousSeconds) {
                $cpuPercent = [Math]::Round(100 * ($cpuSeconds - $previousCpuSeconds) /
                    ($seconds - $previousSeconds) / [Environment]::ProcessorCount, 2)
            }
            $rows.Add([pscustomobject]@{
                TimeUtc = [DateTime]::UtcNow.ToString('o')
                ElapsedSeconds = [Math]::Round($seconds, 3)
                WorkingSetBytes = $workingSetBytes
                PrivateBytes = $privateBytes
                WorkingSetDeltaFromFirstBytes = $workingSetDelta
                PrivateBytesDeltaFromFirstBytes = $privateBytesDelta
                PrivateBytesDeltaBytesPerMinute = $privateBytesDeltaPerMinute
                TotalCpuSeconds = [Math]::Round($cpuSeconds, 3)
                CpuPercentOfMachine = $cpuPercent
                HandleCount = [int]$process.HandleCount
                ThreadCount = [int]$process.Threads.Count
                GdiObjectCount = $gdiObjects
                UserObjectCount = $userObjects
            })
            $previousSeconds = $seconds
            $previousCpuSeconds = $cpuSeconds
        }
        catch [System.InvalidOperationException] { $status = 'exited'; break }
    }
    $result = [pscustomobject]@{
        SchemaVersion = 5
        ProcessId = $ProcessId
        ProcessName = $process.ProcessName
        WorkloadDescription = $WorkloadDescription
        Origin = $origin
        ExecutablePath = $executablePath
        ExecutableSha256 = $binarySha256
        BinaryIdentitySource = 'on-disk-at-start'
        ManagedAssemblyPath = $managedAssemblyPath
        ManagedAssemblySha256 = $managedAssemblySha256
        ManagedAssemblyProductVersion = $managedAssemblyProductVersion
        ProcessStartUtc = $processStart.ToString('o')
        Status = $status
        RequestedSamples = $Samples
        CollectedSamples = $rows.Count
        IntervalMilliseconds = $IntervalMilliseconds
        PeakWorkingSetBytes = if ($rows.Count) { ($rows | Measure-Object -Property WorkingSetBytes -Maximum).Maximum } else { $null }
        PeakPrivateBytes = if ($rows.Count) { ($rows | Measure-Object -Property PrivateBytes -Maximum).Maximum } else { $null }
        FinalWorkingSetDeltaBytes = if ($rows.Count) { $rows[-1].WorkingSetDeltaFromFirstBytes } else { $null }
        FinalPrivateBytesDeltaBytes = if ($rows.Count) { $rows[-1].PrivateBytesDeltaFromFirstBytes } else { $null }
        FinalPrivateBytesDeltaBytesPerMinute = if ($rows.Count) { $rows[-1].PrivateBytesDeltaBytesPerMinute } else { $null }
        Samples = @($rows)
    }
    if ($AsJson) { $result | ConvertTo-Json -Depth 5 }
    else { $result }
}
finally { $process.Dispose() }
