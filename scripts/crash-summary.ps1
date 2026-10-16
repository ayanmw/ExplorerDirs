param(
    [ValidateRange(1, 365)]
    [int]$Days = 30,
    [string]$ProfileDirectory,
    [string]$EventXmlPath,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
$since = (Get-Date).AddDays(-$Days)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ProfileDirectory)) {
    $ProfileDirectory = Join-Path $projectRoot 'profile'
}
$profile = [IO.Path]::GetFullPath($ProfileDirectory)
$markerDirectory = Join-Path $profile 'crash-logs'

function Get-AppOrigin([string]$appPath) {
    try { $fullPath = [IO.Path]::GetFullPath($appPath) }
    catch { return 'other-copy' }
    $releases = (Join-Path $projectRoot 'releases') + [IO.Path]::DirectorySeparatorChar
    $source = (Join-Path $projectRoot 'src') + [IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($releases, [StringComparison]::OrdinalIgnoreCase)) { return 'release' }
    if ($fullPath.StartsWith($source, [StringComparison]::OrdinalIgnoreCase)) { return 'source-test' }
    return 'other-copy'
}

function Convert-CrashEvent([string]$xmlText, [datetime]$fallbackTime) {
    [xml]$document = $xmlText
    $provider = $document.SelectSingleNode("//*[local-name()='System']/*[local-name()='Provider']")
    $eventId = $document.SelectSingleNode("//*[local-name()='System']/*[local-name()='EventID']")
    if ($provider.GetAttribute('Name') -ne 'Application Error' -or $eventId.InnerText -ne '1000') {
        return $null
    }

    $data = @{}
    foreach ($node in $document.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']")) {
        if ($node.HasAttribute('Name')) { $data[$node.GetAttribute('Name')] = $node.InnerText }
    }
    if ($data['AppName'] -ne 'ExplorerDirs.App.exe') { return $null }

    $module = [IO.Path]::GetFileName([string]$data['ModuleName'])
    if ([string]::IsNullOrWhiteSpace($module)) { $module = '(unknown)' }
    $code = [string]$data['ExceptionCode']
    if ($code -notmatch '^(0x)?[0-9a-fA-F]{8}$') { $code = '(unknown)' }
    if ($code -ne '(unknown)' -and -not $code.StartsWith('0x')) { $code = '0x' + $code }

    $time = $fallbackTime.ToUniversalTime()
    $timeNode = $document.SelectSingleNode("//*[local-name()='System']/*[local-name()='TimeCreated']")
    if ($null -ne $timeNode) {
        $parsed = [datetimeoffset]::MinValue
        if ([datetimeoffset]::TryParse($timeNode.GetAttribute('SystemTime'), [ref]$parsed)) {
            $time = $parsed.UtcDateTime
        }
    }
    return [pscustomobject]@{
        TimeUtc = $time.ToString('o')
        Origin = Get-AppOrigin ([string]$data['AppPath'])
        FaultingModule = $module
        ExceptionCode = $code
    }
}

$crashes = @()
if (-not [string]::IsNullOrWhiteSpace($EventXmlPath)) {
    $candidate = Convert-CrashEvent ([IO.File]::ReadAllText([IO.Path]::GetFullPath($EventXmlPath))) (Get-Date)
    if ($null -ne $candidate) { $crashes = @($candidate) }
}
else {
    $filter = @{
        LogName = 'Application'
        ProviderName = 'Application Error'
        Id = 1000
        Data = 'ExplorerDirs.App.exe'
        StartTime = $since
    }
    try { $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction Stop) }
    catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound,*') { $events = @() }
        else { throw }
    }
    foreach ($eventRecord in $events) {
        $candidate = Convert-CrashEvent $eventRecord.ToXml() $eventRecord.TimeCreated
        if ($null -ne $candidate) { $crashes += $candidate }
    }
}

$uncleanMarkers = 0
if (Test-Path -LiteralPath $markerDirectory -PathType Container) {
    $uncleanMarkers = @(Get-ChildItem -LiteralPath $markerDirectory -Filter 'abrupt-*.json' -File).Count
}
$result = [pscustomobject]@{
    SinceUtc = $since.ToUniversalTime().ToString('o')
    WindowsCrashEventCount = $crashes.Count
    ReleaseCrashEventCount = @($crashes | Where-Object Origin -eq 'release').Count
    SourceTestCrashEventCount = @($crashes | Where-Object Origin -eq 'source-test').Count
    StoredUncleanExitMarkerCount = $uncleanMarkers
    Events = $crashes
}
if ($AsJson) { $result | ConvertTo-Json -Depth 4 }
else { $result }
