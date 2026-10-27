$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src/.state'))
$fixture = [IO.Path]::GetFullPath((Join-Path $stateRoot ('publish-smoke-' + [Guid]::NewGuid().ToString('N'))))
$fixtureScripts = Join-Path $fixture 'scripts'
$fixtureProfile = Join-Path $fixture 'profile'
try {
New-Item -ItemType Directory -Path $fixtureScripts, $fixtureProfile | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish.ps1') -Destination (Join-Path $fixtureScripts 'publish.ps1')

# Execute the real packaging script against a generated repository. Replace only
# dotnet publish, so no build is deployed and no executable is ever launched.
function dotnet {
    $publishArguments = @($args)
    $global:lastPublishArguments = $publishArguments
    $outputIndex = [Array]::IndexOf($publishArguments, '--output')
    if ($publishArguments[0] -ne 'publish' -or $outputIndex -lt 0) { throw 'Unexpected build command.' }
    $outputDirectory = $publishArguments[$outputIndex + 1]
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $outputDirectory 'ExplorerDirs.App.exe'), 'fixture executable - do not run')
    [IO.File]::WriteAllText((Join-Path $outputDirectory 'ExplorerDirs.App.dll'), 'fixture assembly - do not run')
    $global:LASTEXITCODE = 0
}

$settings = @('ExplorerDirs.ini', 'ExplorerDirs.WindowPlacement.ini', 'Favorites.ini', 'PinnedTabs.ini', 'UiPreferences.ini', 'AddressBar.ini', 'NavigationTree.ini', 'NavigationTreeSelection.ini', 'NavigationTreeWidths.ini', 'LayoutProportions.ini', 'QuickFilterFavorites.ini', 'ViewPresets.ini', 'Theme.ini', 'SizeDisplay.ini', 'ProgramStarter.ini', 'HiddenItems.ini', 'WindowSessions.ini')
foreach ($name in $settings) {
    [IO.File]::WriteAllText((Join-Path $fixtureProfile $name), ('fixture-' + $name))
}
$extraWindowName = 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.ini'
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'WindowSessions.ini'), "[WindowSessions]`nFormatVersion=1`nCount=2`nSession0=ExplorerDirs.ini`nSession1=$extraWindowName`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile $extraWindowName), 'fixture extra window session')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.PinnedTabs.ini'), 'fixture extra window pins')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.WindowPlacement.ini'), 'fixture extra window placement')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-fedcba9876543210fedcba9876543210.ini'), 'unregistered window session')
$workspaces = Join-Path $fixtureProfile 'Workspaces'
New-Item -ItemType Directory -Path $workspaces, (Join-Path $workspaces 'nested') | Out-Null
[IO.File]::WriteAllText((Join-Path $workspaces '工作区.QDR'), 'workspace fixture')
[IO.File]::WriteAllText((Join-Path $workspaces 'notes.txt'), 'not a workspace')
[IO.File]::WriteAllText((Join-Path $workspaces 'nested/ignored.qdr'), 'not listed by WorkspaceCatalog')
$quickLinks = Join-Path $fixtureProfile 'QuickLinks'
New-Item -ItemType Directory -Path $quickLinks, (Join-Path $quickLinks 'nested') | Out-Null
[IO.File]::WriteAllText((Join-Path $quickLinks '我的目录.lnk'), 'shortcut fixture')
[IO.File]::WriteAllText((Join-Path $quickLinks 'notes.txt'), 'not a quick link')
[IO.File]::WriteAllText((Join-Path $quickLinks 'nested/ignored.lnk'), 'not listed by QuickLinkStore')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'unrelated.txt'), 'not app settings')
$before = @{}
Get-ChildItem -LiteralPath $fixtureProfile -Recurse -File | ForEach-Object {
    $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}

$script = Join-Path $fixtureScripts 'publish.ps1'
$release = & $script -Version 'fixture-v1' -IsolatedProfile
$copied = Join-Path $release 'app/profile'
foreach ($name in $settings) {
    $path = Join-Path $copied $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $before[(Join-Path $fixtureProfile $name)]) {
        throw "Isolated preview did not preserve $name."
    }
}
foreach ($name in @($extraWindowName, 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.PinnedTabs.ini', 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.WindowPlacement.ini')) {
    if (-not (Test-Path -LiteralPath (Join-Path $copied $name) -PathType Leaf)) {
        throw "Isolated preview did not carry registered window state $name."
    }
}
$workspaceCopy = Join-Path $copied 'Workspaces/工作区.QDR'
if (-not (Test-Path -LiteralPath $workspaceCopy -PathType Leaf) -or
    (Get-FileHash -LiteralPath $workspaceCopy -Algorithm SHA256).Hash -ne $before[(Join-Path $workspaces '工作区.QDR')]) {
    throw 'Isolated preview did not preserve the saved workspace.'
}
$quickLinkCopy = Join-Path $copied 'QuickLinks/我的目录.lnk'
if (-not (Test-Path -LiteralPath $quickLinkCopy -PathType Leaf) -or
    (Get-FileHash -LiteralPath $quickLinkCopy -Algorithm SHA256).Hash -ne $before[(Join-Path $quickLinks '我的目录.lnk')]) {
    throw 'Isolated preview did not preserve the saved Quick Link.'
}
if ((Get-ChildItem -LiteralPath (Join-Path $copied 'Workspaces') -Recurse -File).Count -ne 1 -or
    (Get-ChildItem -LiteralPath (Join-Path $copied 'QuickLinks') -Recurse -File).Count -ne 1 -or
    (Test-Path -LiteralPath (Join-Path $copied 'unrelated.txt')) -or
    (Test-Path -LiteralPath (Join-Path $copied 'ExplorerDirs.window-fedcba9876543210fedcba9876543210.ini'))) {
    throw 'Unrelated profile content was copied.'
}
foreach ($path in $before.Keys) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $before[$path]) { throw 'Source profile was modified.' }
}

$externalProfile = Join-Path $fixture 'external-profile'
New-Item -ItemType Directory -Path $externalProfile | Out-Null
[IO.File]::WriteAllText((Join-Path $externalProfile 'ExplorerDirs.ini'), 'external-profile-session')
[IO.File]::WriteAllText((Join-Path $externalProfile 'PinnedTabs.ini'), 'external-profile-pins')
$externalSessionHash = (Get-FileHash -LiteralPath (Join-Path $externalProfile 'ExplorerDirs.ini') -Algorithm SHA256).Hash
$externalPinsHash = (Get-FileHash -LiteralPath (Join-Path $externalProfile 'PinnedTabs.ini') -Algorithm SHA256).Hash
$externalRelease = & $script -Version 'fixture-external-profile' -IsolatedProfile -ProfileRoot $externalProfile
$externalCopy = Join-Path $externalRelease 'app/profile'
foreach ($entry in @(@('ExplorerDirs.ini', $externalSessionHash), @('PinnedTabs.ini', $externalPinsHash))) {
    $copiedPath = Join-Path $externalCopy $entry[0]
    if (-not (Test-Path -LiteralPath $copiedPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $copiedPath -Algorithm SHA256).Hash -ne $entry[1]) {
        throw "Isolated preview did not copy $($entry[0]) from the explicitly selected profile root."
    }
}
foreach ($file in Get-ChildItem -LiteralPath $externalProfile -File) {
    if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $externalCopy $file.Name) -Algorithm SHA256).Hash) {
        throw "Explicit profile source was modified during publishing: $($file.Name)"
    }
}
$missingProfileRejected = $false
try { & $script -Version 'fixture-missing-profile' -IsolatedProfile -ProfileRoot (Join-Path $fixture 'missing-profile') | Out-Null }
catch {
    if ($_.Exception.Message -notlike 'Profile source directory does not exist:*') { throw }
    $missingProfileRejected = $true
}
if (-not $missingProfileRejected -or (Test-Path -LiteralPath (Join-Path $fixture 'releases/fixture-missing-profile'))) {
    throw 'Publishing with an explicitly missing profile root was not rejected before creating a release.'
}

$rejectedExisting = $false
try { & $script -Version 'fixture-v1' -IsolatedProfile | Out-Null }
catch {
    if ($_.Exception.Message -notlike 'Release already exists:*') { throw }
    $rejectedExisting = $true
}
if (-not $rejectedExisting) { throw 'An existing release was overwritten.' }
$normal = & $script -Version 'fixture-normal'
if (Test-Path -LiteralPath (Join-Path $normal 'profile')) { throw 'Normal release copied shared profile unexpectedly.' }
$preview = & $script -Version 'preview-0.6.0-temp.4'
if ($global:lastPublishArguments -notcontains '-p:Version=0.6.0-preview.temp.4') {
    throw 'A temporary preview directory version was not applied to the published product version.'
}
$redirectedReleaseRoot = Join-Path $fixture 'redirected-releases'
$redirected = & $script -Version 'fixture-redirected' -ReleaseRoot $redirectedReleaseRoot
if ([IO.Path]::GetFullPath($redirected) -ne [IO.Path]::GetFullPath((Join-Path $redirectedReleaseRoot 'fixture-redirected'))) {
    throw 'The requested release root was not used.'
}
$versioned = & $script -Version 'preview-0.7.0'
if ($global:lastPublishArguments -notcontains '-p:Version=0.7.0-preview') {
    throw 'A normal preview directory version was not applied to the published product version.'
}
Write-Output 'Publish isolation smoke: PASS (fixture payload only; no real release or GUI)'
}
finally {
    if (Test-Path -LiteralPath $fixture -PathType Container) {
        $resolvedProjectRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $projectRoot).Path)
        $projectPrefix = $resolvedProjectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $resolvedStateRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $stateRoot).Path)
        $resolvedFixture = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fixture).Path)
        $statePrefix = $resolvedStateRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $fixtureName = [IO.Path]::GetFileName($resolvedFixture)
        if (-not $resolvedStateRoot.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not $resolvedFixture.StartsWith($statePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            $fixtureName -notmatch '^publish-smoke-[0-9a-f]{32}$') {
            throw "Refusing to remove an unexpected publish smoke fixture path: $resolvedFixture"
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
