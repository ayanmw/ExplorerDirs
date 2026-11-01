param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Version = 'preview-0.5.2',
    [switch]$IsolatedProfile,
    [ValidatePattern('^[A-Za-z0-9._,-]*$')]
    [string[]]$BuildTags = @(),
    [string]$ReleaseNotes,
    [string]$ReleaseRoot,
    [string]$ProfileRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [System.IO.Path]::GetFullPath(
    $(if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) { Join-Path $projectRoot 'releases' } else { $ReleaseRoot }))
$profileSourceRoot = if ([string]::IsNullOrWhiteSpace($ProfileRoot)) {
    Join-Path $projectRoot 'profile'
} else {
    [System.IO.Path]::GetFullPath($ProfileRoot)
}
if ($IsolatedProfile -and -not (Test-Path -LiteralPath $profileSourceRoot -PathType Container)) {
    throw "Profile source directory does not exist: $profileSourceRoot"
}
$sourceShellProviderClassId = $null
if ($IsolatedProfile) {
    $shellNamespaceSource = Join-Path $profileSourceRoot 'ShellNamespace.ini'
    if (Test-Path -LiteralPath $shellNamespaceSource -PathType Leaf) {
        $inShellNamespace = $false
        $seenSection = $false
        $formatVersion = $null
        $classIdText = $null
        $validShellNamespace = $true
        foreach ($raw in [IO.File]::ReadAllLines($shellNamespaceSource)) {
            $line = $raw.Trim().TrimStart([char]0xFEFF)
            if ($line.Length -eq 0 -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
            if ($line.StartsWith('[') -and $line.EndsWith(']')) {
                $inShellNamespace = $line.Equals('[ShellNamespace]', [StringComparison]::OrdinalIgnoreCase)
                if (-not $inShellNamespace -or $seenSection) { $validShellNamespace = $false }
                $seenSection = $seenSection -or $inShellNamespace
                continue
            }
            $equals = $line.IndexOf('=')
            if (-not $inShellNamespace -or $equals -lt 1) { $validShellNamespace = $false; continue }
            $key = $line.Substring(0, $equals).Trim()
            $value = $line.Substring($equals + 1).Trim()
            if ($key.Equals('FormatVersion', [StringComparison]::OrdinalIgnoreCase)) {
                if ($null -ne $formatVersion) { $validShellNamespace = $false }
                $formatVersion = $value
            }
            elseif ($key.Equals('ClassId', [StringComparison]::OrdinalIgnoreCase)) {
                if ($null -ne $classIdText) { $validShellNamespace = $false }
                $classIdText = $value
            }
            else { $validShellNamespace = $false }
        }
        [Guid]$parsedShellProviderClassId = [Guid]::Empty
        if (-not $validShellNamespace -or -not $seenSection -or $formatVersion -ne '1' -or
            -not [Guid]::TryParseExact($classIdText, 'D', [ref]$parsedShellProviderClassId) -or
            $parsedShellProviderClassId -eq [Guid]::Empty) {
            throw "Shell namespace profile identity is invalid or unsupported: $shellNamespaceSource"
        }
        $sourceShellProviderClassId = $parsedShellProviderClassId
    }
}

function Update-CopiedTextProviderIdentity([string]$Path, [string]$OldClassId, [string]$NewClassId,
    [switch]$QdrExtension) {
    $reader = [IO.StreamReader]::new($Path, [Text.Encoding]::UTF8, $true)
    try {
        $content = $reader.ReadToEnd()
        $encoding = $reader.CurrentEncoding
    }
    finally { $reader.Dispose() }

    $updated = $content
    if ($QdrExtension) {
        $utf8Strict = [Text.UTF8Encoding]::new($false, $true)
        $statePattern = '(?im)^(ExplorerDirs\.State=)([^\r\n]*)'
        $updated = [regex]::Replace($updated, $statePattern, [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            try { $payloadBytes = [Convert]::FromBase64String($match.Groups[2].Value) }
            catch { return $match.Value }
            try { $payload = $utf8Strict.GetString($payloadBytes) }
            catch { return $match.Value }
            if ($payload.IndexOf($OldClassId, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                return $match.Value
            }
            $rewrittenPayload = $payload.Replace($OldClassId, $NewClassId, [StringComparison]::OrdinalIgnoreCase)
            $rewrittenBase64 = [Convert]::ToBase64String($utf8Strict.GetBytes($rewrittenPayload))
            return $match.Groups[1].Value + $rewrittenBase64
        })
    }
    else {
        $updated = $updated.Replace($OldClassId, $NewClassId, [StringComparison]::OrdinalIgnoreCase)
    }

    if ($updated -cne $content) { [IO.File]::WriteAllText($Path, $updated, $encoding) }
}
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
$sourceCommit = (& git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Cannot capture the source Git commit for build identity.'
}
$commitTimeText = (& git -C $projectRoot log -1 --format=%cI).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitTimeText)) {
    throw 'Cannot capture the source Git committer time for build identity.'
}
$commitTimeUtc = [DateTimeOffset]::Parse($commitTimeText, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString(
    "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
$sourceStatus = & git -C $projectRoot status --porcelain --untracked-files=all
if ($LASTEXITCODE -ne 0) { throw 'Cannot capture the source Git working-tree state for build identity.' }
$sourceTreeState = if ([string]::IsNullOrEmpty([string]$sourceStatus)) { 'clean' } else { 'dirty' }
$buildTagsText = if ($BuildTags.Count -eq 0) { 'none' } else { $BuildTags -join ',' }
$publishArguments += @(
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    "-p:GitCommit=$sourceCommit",
    "-p:GitCommitTimeUtc=$commitTimeUtc",
    "-p:SourceTreeState=$sourceTreeState",
    "-p:BuildTags=$buildTagsText"
)
if ($productVersion) { $publishArguments += "-p:InformationalVersion=$productVersion+$sourceCommit" }
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
$releaseTools = Join-Path $staging 'tools'
New-Item -ItemType Directory -Path $releaseTools -Force | Out-Null
foreach ($name in @('folder-verb.ps1', 'FolderVerb.psm1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $releaseTools $name)
}
if ($IsolatedProfile) {
    $profileCopy = Join-Path $payload 'profile'
    New-Item -ItemType Directory -Path $profileCopy -Force | Out-Null
    # Keep the explicit list aligned with runtime preference stores. The former
    # list omitted newer tab/status/filter options, resetting them in previews.
    # Do not copy all INIs: unrelated files and unregistered sessions stay out.
    foreach ($name in @(
        'ExplorerDirs.ini', 'ExplorerDirs.WindowPlacement.ini', 'Favorites.ini', 'PinnedTabs.ini', 'ShellNamespace.ini',
        'UiPreferences.ini', 'AddressBar.ini', 'NavigationTree.ini', 'NavigationTreeSelection.ini',
        'NavigationTreeDragHoverDelay.ini',
        'NavigationTreeWidths.ini', 'LayoutProportions.ini', 'QuickFilterFavorites.ini', 'QuickFilterHistory.ini',
        'ViewPresets.ini', 'Theme.ini', 'SizeDisplay.ini', 'ProgramStarter.ini', 'HiddenItems.ini',
        'MouseWheelFocus.ini', 'TabStripVisibility.ini', 'MultilineTabHeaders.ini', 'TabHeaderMaximumWidth.ini', 'TabCloseButton.ini', 'TabHoverActivation.ini', 'WindowTitle.ini',
        'NewTabLocation.ini', 'StatusBar.ini', 'StatusBarSelectionSize.ini', 'StatusBarFolderSize.ini',
        'SelectionCheckBoxes.ini', 'FullRowSelection.ini'
    )) {
        $source = Join-Path $profileSourceRoot $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $profileCopy $name)
        }
    }
    $windowRegistry = Join-Path $profileSourceRoot 'WindowSessions.ini'
    if (Test-Path -LiteralPath $windowRegistry -PathType Leaf) {
        Copy-Item -LiteralPath $windowRegistry -Destination (Join-Path $profileCopy 'WindowSessions.ini')
        foreach ($line in [IO.File]::ReadAllLines($windowRegistry)) {
            $match = [regex]::Match($line, '^Session\d+=(ExplorerDirs\.window-[0-9a-fA-F]{32}\.ini)$')
            if (-not $match.Success) { continue }
            $sessionName = $match.Groups[1].Value
            foreach ($name in @($sessionName, ([IO.Path]::GetFileNameWithoutExtension($sessionName) + '.PinnedTabs.ini'), ([IO.Path]::GetFileNameWithoutExtension($sessionName) + '.WindowPlacement.ini'))) {
                $source = Join-Path $profileSourceRoot $name
                if (Test-Path -LiteralPath $source -PathType Leaf) {
                    Copy-Item -LiteralPath $source -Destination (Join-Path $profileCopy $name)
                }
            }
        }
    }
    # WorkspaceCatalog lists only top-level .qdr snapshots. Carry those into the
    # independent profile as well, without copying nested directories or unrelated files.
    $workspaceSource = Join-Path $profileSourceRoot 'Workspaces'
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
    $quickLinksSource = Join-Path $profileSourceRoot 'QuickLinks'
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
    if ($null -ne $sourceShellProviderClassId) {
        $previewShellProviderClassId = [Guid]::NewGuid()
        $oldClassIdText = $sourceShellProviderClassId.ToString('D')
        $newClassIdText = $previewShellProviderClassId.ToString('D')
        foreach ($ini in Get-ChildItem -LiteralPath $profileCopy -Filter '*.ini' -File -Recurse) {
            Update-CopiedTextProviderIdentity -Path $ini.FullName -OldClassId $oldClassIdText -NewClassId $newClassIdText
        }
        $workspaceCopies = Join-Path $profileCopy 'Workspaces'
        if (Test-Path -LiteralPath $workspaceCopies -PathType Container) {
            foreach ($workspace in Get-ChildItem -LiteralPath $workspaceCopies -Filter '*.qdr' -File) {
                Update-CopiedTextProviderIdentity -Path $workspace.FullName -OldClassId $oldClassIdText -NewClassId $newClassIdText -QdrExtension
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
