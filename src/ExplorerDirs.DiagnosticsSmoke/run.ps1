$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state'))
$runRoot = [IO.Path]::GetFullPath((Join-Path $stateRoot ('crash-summary-smoke-' + [guid]::NewGuid().ToString('N'))))
if (-not $runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke path escaped src/.state.' }
New-Item -ItemType Directory -Path (Join-Path $runRoot 'crash-logs') -Force | Out-Null

try {
    $sourceExe = Join-Path $projectRoot 'src\ExplorerDirs.App\bin\Release\net10.0-windows\ExplorerDirs.App.exe'
    $releaseExe = Join-Path $projectRoot 'releases\preview-0.5.2\ExplorerDirs.App.exe'
    $xml = @"
<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
  <System><Provider Name="Application Error"/><EventID>1000</EventID><TimeCreated SystemTime="2026-10-15T03:27:43Z"/></System>
  <EventData>
    <Data Name="AppName">ExplorerDirs.App.exe</Data>
    <Data Name="ModuleName">C:\PRIVATE\SECRET\third-party.dll</Data>
    <Data Name="ExceptionCode">c0000005</Data>
    <Data Name="AppPath">$sourceExe</Data>
    <Data Name="ModulePath">C:\PRIVATE\SECRET\third-party.dll</Data>
  </EventData>
</Event>
"@
    $xmlPath = Join-Path $runRoot 'event.xml'
    [IO.File]::WriteAllText($xmlPath, $xml)
    [IO.File]::WriteAllText((Join-Path $runRoot 'crash-logs\abrupt-sample.json'), '{}')
    $command = Join-Path $projectRoot 'scripts\crash-summary.ps1'
    $raw = & $command -ProfileDirectory $runRoot -EventXmlPath $xmlPath -AsJson | Out-String
    $report = $raw | ConvertFrom-Json
    if ($report.WindowsCrashEventCount -ne 1 -or $report.SourceTestCrashEventCount -ne 1 -or
        $report.ReleaseCrashEventCount -ne 0 -or $report.StoredUncleanExitMarkerCount -ne 1 -or
        $report.Events[0].FaultingModule -ne 'third-party.dll' -or
        $report.Events[0].ExceptionCode -ne '0xc0000005' -or
        $raw.Contains('PRIVATE') -or $raw.Contains('SECRET')) {
        throw 'Crash summary leaked a path or misclassified the test event.'
    }

    [IO.File]::WriteAllText($xmlPath, $xml.Replace($sourceExe, $releaseExe))
    $releaseReport = (& $command -ProfileDirectory $runRoot -EventXmlPath $xmlPath -AsJson | Out-String) |
        ConvertFrom-Json
    if ($releaseReport.ReleaseCrashEventCount -ne 1 -or $releaseReport.SourceTestCrashEventCount -ne 0) {
        throw 'Crash summary did not distinguish the release executable.'
    }
    'Crash summary smoke: PASS'
}
finally {
    if ($runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $runRoot)) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
