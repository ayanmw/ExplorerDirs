param(
    [ValidateRange(1, 100)][int]$Count = 10,
    [ValidateRange(0, 5000)][int]$InterProcessDelayMilliseconds = 200,
    [ValidateRange(0, 5000)][int]$SetupDelayMilliseconds = 150,
    [string]$ProbePath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProbePath)) {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $ProbePath = Join-Path $projectRoot 'src/ExplorerDirs.HostProbe/bin/Release/net10.0-windows/ExplorerDirs.HostProbe.exe'
}

$ProbePath = (Resolve-Path -LiteralPath $ProbePath).Path
$successes = 0
$registrationFailures = 0
$deliveryFailures = 0
$otherFailures = 0
$previousSetupDelay = $env:EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_SETUP_DELAY_MS
$env:EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_SETUP_DELAY_MS = [string]$SetupDelayMilliseconds

try {
    for ($run = 1; $run -le $Count; $run++) {
        $output = & $ProbePath --shell-update-notification-smoke 2>&1
        $exitCode = $LASTEXITCODE
        $combined = $output -join ' '

        if ($exitCode -eq 0 -and $combined.Contains('shell-update-notification-smoke PASS')) {
            $successes++
        } elseif ($combined.Contains('Shell change registration failed')) {
            $registrationFailures++
            $registrationMatch = [regex]::Match($combined, 'Shell change registration failed: .*?sources=(0x[0-9A-Fa-f]+), events=(0x[0-9A-Fa-f]+), message=(0x[0-9A-Fa-f]+), entries=(\d+), last-error=(\d+)')
            if ($registrationMatch.Success) {
                Write-Output "run=$run failure=registration sources=$($registrationMatch.Groups[1].Value) events=$($registrationMatch.Groups[2].Value) message=$($registrationMatch.Groups[3].Value) entries=$($registrationMatch.Groups[4].Value) last-error=$($registrationMatch.Groups[5].Value)"
            } else {
                Write-Output "run=$run failure=registration details=unavailable"
            }
        } elseif ($combined.Contains('Shell did not deliver SHCNE_UPDATEITEM') -or
                  $combined.Contains('Shell did not deliver SHCNE_CREATE')) {
            $deliveryFailures++
            $summaryMatch = [regex]::Match($combined, 'lock-failures=(\d+); targets=(.*?); timeline-ms=(.*?); omitted=(\d+)')
            if ($summaryMatch.Success) {
                Write-Output "run=$run failure=delivery lock-failures=$($summaryMatch.Groups[1].Value) targets=$($summaryMatch.Groups[2].Value) timeline-ms=$($summaryMatch.Groups[3].Value) omitted=$($summaryMatch.Groups[4].Value)"
            } else {
                Write-Output "run=$run failure=delivery diagnostic-summary=unavailable"
            }
        } else {
            $otherFailures++
            Write-Output "run=$run failure=other exit=$exitCode output=$combined"
        }

        if ($run -lt $Count -and $InterProcessDelayMilliseconds -gt 0) {
            Start-Sleep -Milliseconds $InterProcessDelayMilliseconds
        }
    }
}
finally {
    $env:EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_SETUP_DELAY_MS = $previousSetupDelay
}

Write-Output "runs=$Count successes=$successes registration-failures=$registrationFailures delivery-failures=$deliveryFailures other-failures=$otherFailures setup-delay-ms=$SetupDelayMilliseconds interprocess-gap-ms=$InterProcessDelayMilliseconds"

if ($successes -ne $Count) { exit 1 }
