param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Version = 'preview-0.5.2',
    [switch]$IsolatedProfile,
    [string]$ReleaseNotes,
    [string]$ReleaseRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [System.IO.Path]::GetFullPath(
    $(if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) { Join-Path $projectRoot 'releases' } else { $ReleaseRoot }))
$releasePrefix = if ($releaseRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $releaseRoot
} else {
    $releaseRoot + [System.IO.Path]::DirectorySeparatorChar
}
$project = Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj'
$productVersion = $null
if ($Version -match '^preview-(\d+\.\d+\.\d+)-temp\.(\d+)$') {
    $productVersion = "$($Matches[1])-preview.temp.$($Matches[2])"
}
elseif ($Version -match '^preview-(\d+\.\d+\.\d+)$') {
    $productVersion = "$($Matches[1])-preview"
}
elseif ($Version -match '^(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
    $productVersion = $Matches[1]
}
$staging = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot (
    '.staging-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))))
$destination = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$buildOutput = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('src\ExplorerDirs.App\bin\publish-build\' + $Version)))

foreach ($path in @($staging, $destination)) {
    if (-not $path.StartsWith($releasePrefix,
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
$publishArguments = @('publish', $project, '--configuration', 'Release', '--self-contained', 'false',
    '--output', $payload, "-p:OutputPath=$buildOutput")
if ($productVersion) { $publishArguments += "-p:Version=$productVersion" }
dotnet @publishArguments
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
    # Match the separate preference stores: omitting the tree setting made an
    # isolated preview silently revert the user's navigation preference.
    foreach ($name in @('ExplorerDirs.ini', 'Favorites.ini', 'PinnedTabs.ini', 'UiPreferences.ini', 'AddressBar.ini', 'NavigationTree.ini', 'NavigationTreeWidths.ini', 'LayoutProportions.ini', 'QuickFilterFavorites.ini', 'ViewPresets.ini', 'Theme.ini', 'SizeDisplay.ini', 'ProgramStarter.ini', 'HiddenItems.ini')) {
        $source = Join-Path (Join-Path $projectRoot 'profile') $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $profileCopy $name)
        }
    }
    $windowRegistry = Join-Path (Join-Path $projectRoot 'profile') 'WindowSessions.ini'
    if (Test-Path -LiteralPath $windowRegistry -PathType Leaf) {
        Copy-Item -LiteralPath $windowRegistry -Destination (Join-Path $profileCopy 'WindowSessions.ini')
        foreach ($line in [IO.File]::ReadAllLines($windowRegistry)) {
            $match = [regex]::Match($line, '^Session\d+=(ExplorerDirs\.window-[0-9a-fA-F]{32}\.ini)$')
            if (-not $match.Success) { continue }
            $sessionName = $match.Groups[1].Value
            foreach ($name in @($sessionName, ([IO.Path]::GetFileNameWithoutExtension($sessionName) + '.PinnedTabs.ini'))) {
                $source = Join-Path (Join-Path $projectRoot 'profile') $name
                if (Test-Path -LiteralPath $source -PathType Leaf) {
                    Copy-Item -LiteralPath $source -Destination (Join-Path $profileCopy $name)
                }
            }
        }
    }
    # WorkspaceCatalog lists only top-level .qdr snapshots. Carry those into the
    # independent profile as well, without copying nested directories or unrelated files.
    $workspaceSource = Join-Path (Join-Path $projectRoot 'profile') 'Workspaces'
    if (Test-Path -LiteralPath $workspaceSource -PathType Container) {
        $workspaceFiles = @(Get-ChildItem -LiteralPath $workspaceSource -File -Force |
            Where-Object { $_.Extension -ieq '.qdr' })
        if ($workspaceFiles.Count -gt 0) {
            $workspaceCopy = Join-Path $profileCopy 'Workspaces'
            New-Item -ItemType Directory -Path $workspaceCopy | Out-Null
            foreach ($file in $workspaceFiles) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $workspaceCopy $file.Name)
            }
        }
    }
    # Quick Links are user-managed Shell shortcuts. Copy only the top-level
    # .lnk files the runtime menu reads; do not carry unrelated profile files.
    $quickLinksSource = Join-Path (Join-Path $projectRoot 'profile') 'QuickLinks'
    if (Test-Path -LiteralPath $quickLinksSource -PathType Container) {
        $quickLinks = @(Get-ChildItem -LiteralPath $quickLinksSource -File -Force |
            Where-Object { $_.Extension -ieq '.lnk' })
        if ($quickLinks.Count -gt 0) {
            $quickLinksCopy = Join-Path $profileCopy 'QuickLinks'
            New-Item -ItemType Directory -Path $quickLinksCopy | Out-Null
            foreach ($link in $quickLinks) {
                Copy-Item -LiteralPath $link.FullName -Destination (Join-Path $quickLinksCopy $link.Name)
            }
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
