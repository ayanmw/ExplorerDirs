param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Version = 'preview-0.5.2',
    [switch]$IsolatedProfile,
    [string]$ReleaseNotes
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'releases'))
$project = Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj'
$staging = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot (
    '.staging-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))))
$destination = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$buildOutput = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('src\ExplorerDirs.App\bin\publish-build\' + $Version)))

foreach ($path in @($staging, $destination)) {
    if (-not $path.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path escapes releases directory: $path"
    }
}

if (Test-Path -LiteralPath $destination) {
    throw "Release already exists: $destination"
}
if (Test-Path -LiteralPath $staging) {
    throw "Publish staging already exists: $staging"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
# A nested payload uses SessionStore's existing portable profile resolution,
# so temporary previews never upgrade the shared daily-use profile.
$payload = if ($IsolatedProfile) { Join-Path $staging 'app' } else { $staging }
dotnet publish $project --configuration Release --self-contained false --output $payload "-p:OutputPath=$buildOutput"
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed. Staging files were left at $staging for inspection."
}

$exe = Join-Path $payload 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish output is missing $exe"
}

# A same-volume move makes a complete release visible under its final name.
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$dllHash = (Get-FileHash -LiteralPath (Join-Path $payload 'ExplorerDirs.App.dll') -Algorithm SHA256).Hash
[IO.File]::WriteAllLines((Join-Path $payload 'SHA256.txt'), [string[]]@(
    "ExplorerDirs.App.exe  $hash", "ExplorerDirs.App.dll  $dllHash"
), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $payload 'FolderRoutingCapability.ini'), [string[]]@(
    'FormatVersion=1',
    'DirectoryArgument=true',
    "ExecutableSha256=$hash"
), [Text.UTF8Encoding]::new($false))
if ($IsolatedProfile) {
    $profileCopy = Join-Path $payload 'profile'
    New-Item -ItemType Directory -Path $profileCopy -Force | Out-Null
    foreach ($name in @('ExplorerDirs.ini', 'Favorites.ini', 'PinnedTabs.ini', 'UiPreferences.ini')) {
        $source = Join-Path (Join-Path $projectRoot 'profile') $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $profileCopy $name)
        }
    }
}
if ($ReleaseNotes) {
    Copy-Item -LiteralPath $ReleaseNotes -Destination (Join-Path $staging 'RELEASE-NOTES.md')
}
if (Test-Path -LiteralPath $destination) {
    throw "Release appeared during publishing; staging files were left at $staging"
}
[IO.Directory]::Move($staging, $destination)
Write-Output $destination
