param(
    [ValidateSet('Status', 'Preview', 'Enable', 'Disable')]
    [string]$Mode = 'Status',
    [string]$Executable,
    [string]$ExpectedSha256,
    [switch]$ConfirmVerifiedRelease
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FolderVerb.psm1') -Force

if ($Mode -eq 'Status') {
    Get-ExplorerDirsFolderVerbState
    return
}
if ($Mode -eq 'Disable') {
    Disable-ExplorerDirsFolderVerb
    return
}

$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagedApp = Join-Path $packageRoot 'app/ExplorerDirs.App.exe'
$isPackagedRelease = [IO.File]::Exists($packagedApp) -or
    [IO.File]::Exists((Join-Path $packageRoot 'ExplorerDirs.App.exe'))
if ($isPackagedRelease) {
    $releaseRoot = $packageRoot
    if ([string]::IsNullOrWhiteSpace($Executable)) {
        $Executable = if ([IO.File]::Exists($packagedApp)) { $packagedApp } else { Join-Path $packageRoot 'ExplorerDirs.App.exe' }
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($Executable)) {
        throw 'Preview and Enable require -Executable when run from the source tree.'
    }
    $projectRoot = $packageRoot
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'releases'))
}
$fullExecutable = [IO.Path]::GetFullPath($Executable)
if (-not $fullExecutable.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($fullExecutable) -ne 'ExplorerDirs.App.exe' -or
    -not [IO.File]::Exists($fullExecutable)) {
    throw 'Choose an existing ExplorerDirs.App.exe under this project releases directory.'
}
$hash = (Get-FileHash -LiteralPath $fullExecutable -Algorithm SHA256).Hash
$capabilityDeclared = Test-ExplorerDirsRoutingCapability -Executable $fullExecutable -Sha256 $hash
if ($Mode -eq 'Preview') {
    [pscustomobject]@{
        Executable = $fullExecutable
        Sha256 = $hash
        DirectoryArgumentDeclared = $capabilityDeclared
        DirectoryVerb = 'HKCU\Software\Classes\Directory\shell\ExplorerDirs.Open'
        DriveVerb = 'HKCU\Software\Classes\Drive\shell\ExplorerDirs.Open'
        Command = '"' + $fullExecutable + '" "%1"'
        CurrentState = @(Get-ExplorerDirsFolderVerbState)
    }
    return
}

if (-not $capabilityDeclared -or -not $ConfirmVerifiedRelease -or
    $ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    $hash -ne $ExpectedSha256.ToUpperInvariant()) {
    throw 'Enable requires a matching routing capability, -ConfirmVerifiedRelease and the exact SHA256 shown by Preview.'
}
Enable-ExplorerDirsFolderVerb -Executable $fullExecutable
