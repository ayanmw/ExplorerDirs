param(
    [ValidateRange(1, 365)]
    [int]$Days = 30,
    [ValidateSet('ExplorerDirs.App.exe', 'Q-Dir.exe')]
    [string]$AppName = 'ExplorerDirs.App.exe',
    [string]$ProfileDirectory,
    [string[]]$EventXmlPath,
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
    if ($AppName -eq 'Q-Dir.exe') { return 'benchmark' }
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
    if ($data['AppName'] -ne $AppName) { return $null }

    $module = [IO.Path]::GetFileName([string]$data['ModuleName'])
    if ([string]::IsNullOrWhiteSpace($module)) { $module = '(unknown)' }
    $code = [string]$data['ExceptionCode']
    if ($code -notmatch '^(0x)?[0-9a-fA-F]{8}$') { $code = '(unknown)' }
    if ($code -ne '(unknown)' -and -not $code.StartsWith('0x')) { $code = '0x' + $code }
    $version = [string]$data['AppVersion']
    if ($version -notmatch '^\d+(\.\d+){1,3}$') { $version = '(unknown)' }
    $eventProcessId = [string]$data['ProcessId']
    $created = [string]$data['ProcessCreationTime']
    $identity = if ($eventProcessId -match '^(0x)?[0-9a-fA-F]+$' -and
        $created -match '^(0x)?[0-9a-fA-F]+$') {
        $eventProcessId.ToLowerInvariant() + ':' + $created.ToLowerInvariant()
    } else {
        # Without both fields, never merge separate event records speculatively.
        'event-' + [guid]::NewGuid().ToString('N')
    }

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
        AppVersion = $version
        ProcessIdentity = $identity
    }
}

$crashes = @()
if ($null -ne $EventXmlPath -and $EventXmlPath.Count -gt 0) {
    foreach ($path in $EventXmlPath) {
        $candidate = Convert-CrashEvent ([IO.File]::ReadAllText([IO.Path]::GetFullPath($path))) (Get-Date)
        if ($null -ne $candidate) { $crashes += $candidate }
    }
}
else {
    $filter = @{
        LogName = 'Application'
        ProviderName = 'Application Error'
        Id = 1000
        Data = $AppName
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

$uncleanMarkers = $null
if ($AppName -eq 'ExplorerDirs.App.exe') {
    $uncleanMarkers = 0
    if (Test-Path -LiteralPath $markerDirectory -PathType Container) {
        $uncleanMarkers = @(Get-ChildItem -LiteralPath $markerDirectory -Filter 'abrupt-*.json' -File).Count
    }
}
$processes = @($crashes | Group-Object ProcessIdentity | ForEach-Object { $_.Group[0] })
$byVersion = @($processes | Group-Object AppVersion | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Version = $_.Name; Processes = $_.Count }
})
$result = [pscustomobject]@{
    AppName = $AppName
    SinceUtc = $since.ToUniversalTime().ToString('o')
    WindowsCrashEventCount = $crashes.Count
    DistinctProcessCrashCount = $processes.Count
    EventsWithoutProcessIdentityCount = @($crashes | Where-Object ProcessIdentity -Like 'event-*').Count
    ReleaseCrashEventCount = @($crashes | Where-Object Origin -eq 'release').Count
    SourceTestCrashEventCount = @($crashes | Where-Object Origin -eq 'source-test').Count
    StoredUncleanExitMarkerCount = $uncleanMarkers
    CrashesByVersion = $byVersion
    Events = @($crashes | Select-Object TimeUtc, Origin, FaultingModule, ExceptionCode, AppVersion)
}
if ($AsJson) { $result | ConvertTo-Json -Depth 4 }
else { $result }
