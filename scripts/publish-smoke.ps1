$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src/.state'))
$fixture = [IO.Path]::GetFullPath((Join-Path $stateRoot ('publish-smoke-' + [Guid]::NewGuid().ToString('N'))))
$fixtureScripts = Join-Path $fixture 'scripts'
$fixtureProfile = Join-Path $fixture 'profile'
try {
New-Item -ItemType Directory -Path $fixtureScripts, $fixtureProfile | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish.ps1') -Destination (Join-Path $fixtureScripts 'publish.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'folder-verb.ps1') -Destination (Join-Path $fixtureScripts 'folder-verb.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FolderVerb.psm1') -Destination (Join-Path $fixtureScripts 'FolderVerb.psm1')

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
    [IO.File]::WriteAllText((Join-Path $outputDirectory 'ExplorerDirs.ShellNamespace.dll'), 'fixture native adapter - do not load')
    $global:LASTEXITCODE = 0
}

$global:fakeGitStatus = ''
function git {
    $joined = $args -join ' '
    $global:LASTEXITCODE = 0
    if ($joined -match 'rev-parse HEAD') { return ('a' * 40) }
    if ($joined -match 'log -1 --format=%cI') { return '2026-10-30T12:30:00+08:00' }
    if ($joined -match 'status --porcelain') { return $global:fakeGitStatus }
    throw "Unexpected Git metadata query: $joined"
}

$settings = @(
    'ExplorerDirs.ini', 'ExplorerDirs.WindowPlacement.ini', 'Favorites.ini', 'CommonLocations.ini', 'RecentAddresses.ini', 'PinnedTabs.ini',
    'UiPreferences.ini', 'Sorting.ini', 'AddressBar.ini', 'NavigationTree.ini', 'NavigationTreeSelection.ini',
    'NavigationTreeDragHoverDelay.ini',
    'NavigationTreeWidths.ini', 'LayoutProportions.ini', 'QuickFilterFavorites.ini', 'QuickFilterHistory.ini',
    'ViewPresets.ini', 'Theme.ini', 'SizeDisplay.ini', 'FolderSize.ini', 'ProgramStarter.ini', 'HiddenItems.ini',
    'MouseWheelFocus.ini', 'TabStripVisibility.ini', 'MultilineTabHeaders.ini', 'TabHeaderMaximumWidth.ini', 'TabCloseButton.ini', 'TabHoverActivation.ini', 'WindowTitle.ini',
    'NewTabLocation.ini', 'StatusBar.ini', 'StatusBarSelectionSize.ini', 'StatusBarFolderSize.ini',
    'SelectionCheckBoxes.ini', 'FullRowSelection.ini', 'WindowSessions.ini'
)
foreach ($name in $settings) {
    [IO.File]::WriteAllText((Join-Path $fixtureProfile $name), ('fixture-' + $name))
}
$extraWindowName = 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.ini'
[Guid]$sourceProviderClassId = 'b0f47a2f-1690-4888-a0d6-cfe4a4abf92f'
$sourceProviderIdentity = "::{$($sourceProviderClassId.ToString('D'))}\edpath:0043003A005C0070"
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ShellNamespace.ini'),
    "; fixture Shell namespace identity`r`n[ShellNamespace]`r`nFormatVersion=1`r`nClassId=$($sourceProviderClassId.ToString('D'))`r`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.ini'), "Path=$sourceProviderIdentity`r`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'Favorites.ini'), "Item0=$sourceProviderIdentity`r`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'PinnedTabs.ini'), "Path0=$sourceProviderIdentity`r`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'WindowSessions.ini'), "[WindowSessions]`nFormatVersion=1`nCount=2`nSession0=ExplorerDirs.ini`nSession1=$extraWindowName`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile $extraWindowName), "Path=$sourceProviderIdentity`r`n")
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.PinnedTabs.ini'), 'fixture extra window pins')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-0123456789abcdef0123456789abcdef.WindowPlacement.ini'), 'fixture extra window placement')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'ExplorerDirs.window-fedcba9876543210fedcba9876543210.ini'), 'unregistered window session')
$workspaces = Join-Path $fixtureProfile 'Workspaces'
New-Item -ItemType Directory -Path $workspaces, (Join-Path $workspaces 'nested') | Out-Null
$workspaceExtension = @{ Session = "Path=$sourceProviderIdentity`r`n"; Pins = @(@{ Path = $sourceProviderIdentity }) } |
    ConvertTo-Json -Depth 8 -Compress
$workspaceState = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($workspaceExtension))
[IO.File]::WriteAllText((Join-Path $workspaces '工作区.QDR'), "Dir1=C:\preview-fixture`r`nExplorerDirs.State=$workspaceState`r`n")
[IO.File]::WriteAllText((Join-Path $workspaces 'notes.txt'), 'not a workspace')
[IO.File]::WriteAllText((Join-Path $workspaces 'nested/ignored.qdr'), 'not listed by WorkspaceCatalog')
$quickLinks = Join-Path $fixtureProfile 'QuickLinks'
New-Item -ItemType Directory -Path $quickLinks, (Join-Path $quickLinks 'nested') | Out-Null
[IO.File]::WriteAllText((Join-Path $quickLinks '我的目录.lnk'), 'shortcut fixture')
[IO.File]::WriteAllText((Join-Path $quickLinks '我的目录.lnk.identity'), $sourceProviderIdentity)
[IO.File]::WriteAllText((Join-Path $quickLinks 'notes.txt'), 'not a quick link')
[IO.File]::WriteAllText((Join-Path $quickLinks 'nested/ignored.lnk'), 'not listed by QuickLinkStore')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'unrelated.txt'), 'not app settings')
[IO.File]::WriteAllText((Join-Path $fixtureProfile 'unrelated.ini'), 'not an Explorer Dirs setting')
$before = @{}
Get-ChildItem -LiteralPath $fixtureProfile -Recurse -File | ForEach-Object {
    $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}

$script = Join-Path $fixtureScripts 'publish.ps1'
$release = & $script -Version 'fixture-v1' -IsolatedProfile
$packagedFolderScript = Join-Path $release 'tools/folder-verb.ps1'
if (-not (Test-Path -LiteralPath $packagedFolderScript -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $release 'tools/FolderVerb.psm1') -PathType Leaf)) {
    throw 'Isolated preview omitted the folder-entry manager.'
}
$folderPreview = & $packagedFolderScript -Mode Preview
if (-not $folderPreview.DirectoryArgumentDeclared -or
    [IO.Path]::GetFullPath($folderPreview.Executable) -ne
    [IO.Path]::GetFullPath((Join-Path $release 'app/ExplorerDirs.App.exe'))) {
    throw 'Packaged folder-entry manager did not resolve its own isolated app and capability manifest.'
}
$folderPreviewJson = & $packagedFolderScript -Mode Preview -AsJson | ConvertFrom-Json
if (-not $folderPreviewJson.DirectoryArgumentDeclared -or
    [IO.Path]::GetFullPath($folderPreviewJson.Executable) -ne
    [IO.Path]::GetFullPath((Join-Path $release 'app/ExplorerDirs.App.exe')) -or
    $folderPreviewJson.Sha256 -ne (Get-FileHash -LiteralPath (Join-Path $release 'app/ExplorerDirs.App.exe') -Algorithm SHA256).Hash -or
    $folderPreviewJson.CurrentState.Count -ne 2) {
    throw 'Packaged folder-entry manager emitted incomplete machine-readable status.'
}
$folderStatusJson = & $packagedFolderScript -Mode Status -AsJson | ConvertFrom-Json
if ($folderStatusJson.Count -ne 2 -or
    @($folderStatusJson | Where-Object { $_.State -notin @('missing', 'owned', 'conflict') }).Count -ne 0) {
    throw 'Packaged folder-entry manager emitted invalid machine-readable status.'
}
$copied = Join-Path $release 'app/profile'
$adapterPath = Join-Path $release 'app/ExplorerDirs.ShellNamespace.dll'
if (-not (Test-Path -LiteralPath $adapterPath -PathType Leaf)) {
    throw 'The published app payload omitted its native Shell namespace adapter.'
}
$adapterHash = (Get-FileHash -LiteralPath $adapterPath -Algorithm SHA256).Hash
$checksumLines = [IO.File]::ReadAllLines((Join-Path $release 'app/SHA256.txt'))
if (-not ($checksumLines -contains "ExplorerDirs.ShellNamespace.dll  $adapterHash")) {
    throw 'The published SHA256 manifest omitted or misidentified the native Shell namespace adapter.'
}
$copiedProviderClassId = [Guid]::Empty
$classIdLine = [IO.File]::ReadAllLines((Join-Path $copied 'ShellNamespace.ini')) |
    Where-Object { $_ -match '^ClassId=' } | Select-Object -First 1
if ($null -eq $classIdLine -or
    -not [Guid]::TryParseExact($classIdLine.Substring('ClassId='.Length), 'D', [ref]$copiedProviderClassId) -or
    $copiedProviderClassId -eq $sourceProviderClassId) {
    throw 'Isolated preview did not assign a separate Shell namespace CLSID.'
}
$secondIdentityRelease = & $script -Version 'fixture-v1-identity-copy' -IsolatedProfile
$secondIdentityClassId = [Guid]::Empty
$secondClassIdLine = [IO.File]::ReadAllLines((Join-Path $secondIdentityRelease 'app/profile/ShellNamespace.ini')) |
    Where-Object { $_ -match '^ClassId=' } | Select-Object -First 1
if ($null -eq $secondClassIdLine -or
    -not [Guid]::TryParseExact($secondClassIdLine.Substring('ClassId='.Length), 'D', [ref]$secondIdentityClassId) -or
    $secondIdentityClassId -eq $copiedProviderClassId) {
    throw 'Two isolated previews copied from one profile reused the same Shell namespace CLSID.'
}
foreach ($name in @('ExplorerDirs.ini', 'Favorites.ini', 'PinnedTabs.ini', $extraWindowName)) {
    $rewritten = [IO.File]::ReadAllText((Join-Path $copied $name))
    if (-not $rewritten.Contains($copiedProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase) -or
        $rewritten.Contains($sourceProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Isolated preview did not remap provider identity in $name."
    }
}
foreach ($name in $settings) {
    $path = Join-Path $copied $name
    $remappedSessionFile = $name -in @('ExplorerDirs.ini', 'Favorites.ini', 'PinnedTabs.ini')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (-not $remappedSessionFile -and
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $before[(Join-Path $fixtureProfile $name)])) {
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
    (Get-Content -LiteralPath $workspaceCopy -Raw) -notmatch 'Dir1=C:\\preview-fixture') {
    throw 'Isolated preview did not preserve the saved workspace.'
}
$copiedWorkspaceLines = [IO.File]::ReadAllLines($workspaceCopy)
$copiedWorkspaceState = ($copiedWorkspaceLines | Where-Object { $_ -like 'ExplorerDirs.State=*' } | Select-Object -First 1).Substring('ExplorerDirs.State='.Length)
$copiedWorkspaceExtension = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($copiedWorkspaceState))
if (-not $copiedWorkspaceExtension.Contains($copiedProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase) -or
    $copiedWorkspaceExtension.Contains($sourceProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Isolated preview did not remap provider identities in the Q-Dir workspace extension.'
}
$quickLinkCopy = Join-Path $copied 'QuickLinks/我的目录.lnk'
if (-not (Test-Path -LiteralPath $quickLinkCopy -PathType Leaf) -or
    (Get-FileHash -LiteralPath $quickLinkCopy -Algorithm SHA256).Hash -ne $before[(Join-Path $quickLinks '我的目录.lnk')]) {
    throw 'Isolated preview did not preserve the saved Quick Link.'
}
$quickLinkIdentityCopy = $quickLinkCopy + '.identity'
if (-not (Test-Path -LiteralPath $quickLinkIdentityCopy -PathType Leaf) -or
    -not [IO.File]::ReadAllText($quickLinkIdentityCopy).Contains($copiedProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase) -or
    [IO.File]::ReadAllText($quickLinkIdentityCopy).Contains($sourceProviderClassId.ToString('D'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Isolated preview did not remap the copied Quick Link parsing identity.'
}
if ((Get-ChildItem -LiteralPath (Join-Path $copied 'Workspaces') -Recurse -File).Count -ne 1 -or
    (Get-ChildItem -LiteralPath (Join-Path $copied 'QuickLinks') -Recurse -File).Count -ne 2 -or
    (Test-Path -LiteralPath (Join-Path $copied 'unrelated.txt')) -or
    (Test-Path -LiteralPath (Join-Path $copied 'unrelated.ini')) -or
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

foreach ($invalidIdentityCase in @(
    @{ Name = 'invalid-guid'; Content = "[ShellNamespace]`r`nFormatVersion=1`r`nClassId=not-a-guid`r`n" },
    @{ Name = 'future-format'; Content = "[ShellNamespace]`r`nFormatVersion=2`r`nClassId=$($sourceProviderClassId.ToString('D'))`r`n" }
)) {
    $invalidShellProfile = Join-Path $fixture ('invalid-shell-' + $invalidIdentityCase.Name)
    New-Item -ItemType Directory -Path $invalidShellProfile | Out-Null
    [IO.File]::WriteAllText((Join-Path $invalidShellProfile 'ShellNamespace.ini'), $invalidIdentityCase.Content)
    $invalidShellRejected = $false
    try { & $script -Version ('fixture-' + $invalidIdentityCase.Name) -IsolatedProfile -ProfileRoot $invalidShellProfile | Out-Null }
    catch {
        if ($_.Exception.Message -notlike 'Shell namespace profile identity is invalid or unsupported:*') { throw }
        $invalidShellRejected = $true
    }
    if (-not $invalidShellRejected -or
        (Test-Path -LiteralPath (Join-Path $fixture ('releases/fixture-' + $invalidIdentityCase.Name)))) {
        throw "Publishing did not reject the $($invalidIdentityCase.Name) Shell namespace identity before creating a release."
    }
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
$normalFolderPreview = & (Join-Path $normal 'tools/folder-verb.ps1') -Mode Preview
if (-not $normalFolderPreview.DirectoryArgumentDeclared -or
    [IO.Path]::GetFullPath($normalFolderPreview.Executable) -ne
    [IO.Path]::GetFullPath((Join-Path $normal 'ExplorerDirs.App.exe'))) {
    throw 'Packaged folder-entry manager did not resolve its normal release executable.'
}
$normalFolderPreviewJson = & (Join-Path $normal 'tools/folder-verb.ps1') -Mode Preview -AsJson | ConvertFrom-Json
if (-not $normalFolderPreviewJson.DirectoryArgumentDeclared -or
    [IO.Path]::GetFullPath($normalFolderPreviewJson.Executable) -ne
    [IO.Path]::GetFullPath((Join-Path $normal 'ExplorerDirs.App.exe')) -or
    $normalFolderPreviewJson.CurrentState.Count -ne 2) {
    throw 'Normal folder-entry manager emitted incomplete machine-readable status.'
}
$preview = & $script -Version 'preview-0.6.0-temp.4' -BuildTags @('shell-probe', 'dark-ui')
if ($global:lastPublishArguments -notcontains '-p:Version=0.6.0-preview.temp.4') {
    throw 'A temporary preview directory version was not applied to the published product version.'
}
foreach ($expectedArgument in @(
    ('-p:GitCommit=' + ('a' * 40)),
    '-p:GitCommitTimeUtc=2026-10-30T04:30:00Z',
    '-p:SourceTreeState=clean',
    '-p:BuildTags=shell-probe,dark-ui',
    ('-p:InformationalVersion=0.6.0-preview.temp.4+' + ('a' * 40)),
    '-p:IncludeSourceRevisionInInformationalVersion=false'
)) {
    if ($global:lastPublishArguments -notcontains $expectedArgument) {
        throw "Published build identity omitted $expectedArgument."
    }
}
$global:fakeGitStatus = ' M src/ExplorerDirs.App/MainWindow.xaml.cs'
$dirtyPreview = & $script -Version 'fixture-dirty-source'
if ($global:lastPublishArguments -notcontains '-p:SourceTreeState=dirty') {
    throw 'A dirty source tree was not marked in the published build identity.'
}
$global:fakeGitStatus = ''
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
