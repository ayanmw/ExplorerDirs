$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FolderVerb.psm1') -Force

$id = [Guid]::NewGuid().ToString('N')
$registryBases = @(
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Owned",
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Conflict",
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Modified"
)
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "ExplorerDirs-FolderVerb-$id"
$executable = Join-Path $tempRoot 'release package\ExplorerDirs.App.exe'

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw "Folder verb smoke failed: $message" }
}

function Assert-State([string]$registryBase, [string[]]$expected) {
    $actual = @(Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)
    $states = @($actual | ForEach-Object State)
    Assert (($states -join ',') -eq ($expected -join ',')) `
        "expected [$($expected -join ',')] under HKCU\$registryBase, got [$($states -join ',')]"
}

try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $executable) -Force | Out-Null
    [IO.File]::WriteAllText($executable, 'fixture executable; never run')
    $executableHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $capabilityPath = Join-Path (Split-Path -Parent $executable) 'FolderRoutingCapability.ini'
    function Write-Capability([string[]]$lines) {
        [IO.File]::WriteAllLines($capabilityPath, $lines, [Text.UTF8Encoding]::new($false))
    }
    Write-Capability @('FormatVersion=1', 'DirectoryArgument=true', "ExecutableSha256=$executableHash")
    Assert (Test-ExplorerDirsRoutingCapability -Executable $executable -Sha256 $executableHash) `
        'a capability manifest must authorize its exact executable hash'
    Assert (-not (Test-ExplorerDirsRoutingCapability -Executable $executable -Sha256 ('0' * 64))) `
        'a capability manifest must not authorize a different executable hash'
    Write-Capability @('FormatVersion=1', 'DirectoryArgument=false', "ExecutableSha256=$executableHash")
    Assert (-not (Test-ExplorerDirsRoutingCapability -Executable $executable -Sha256 $executableHash)) `
        'a capability manifest without the directory argument must be rejected'
    Write-Capability @('FormatVersion=1', 'DirectoryArgument=true', "ExecutableSha256=$executableHash", 'Unexpected=true')
    Assert (-not (Test-ExplorerDirsRoutingCapability -Executable $executable -Sha256 $executableHash)) `
        'a capability manifest with unknown fields must be rejected'

    $ownedBase = $registryBases[0]
    Assert-State $ownedBase @('missing', 'missing')
    Enable-ExplorerDirsFolderVerb -Executable $executable -RegistryBase $ownedBase | Out-Null
    Assert-State $ownedBase @('owned', 'owned')

    $directoryPath = "$ownedBase\Directory\shell\ExplorerDirs.Open"
    $directoryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($directoryPath, $false)
    try {
        $expectedCommand = '"' + $executable + '" "%1"'
        Assert ($directoryKey.GetValue('command') -eq $null) 'the verb key must not store its command at the wrong level'
        $commandKey = $directoryKey.OpenSubKey('command', $false)
        try { Assert ($commandKey.GetValue('') -eq $expectedCommand) 'the command must quote the executable and one directory argument' }
        finally { $commandKey.Dispose() }
    }
    finally { $directoryKey.Dispose() }

    Enable-ExplorerDirsFolderVerb -Executable $executable -RegistryBase $ownedBase | Out-Null
    Assert-State $ownedBase @('owned', 'owned')
    Disable-ExplorerDirsFolderVerb -RegistryBase $ownedBase | Out-Null
    Assert-State $ownedBase @('missing', 'missing')

    $conflictBase = $registryBases[1]
    $drivePath = "$conflictBase\Drive\shell\ExplorerDirs.Open"
    $driveKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($drivePath, $true)
    try { $driveKey.SetValue('', 'Owned by another app', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $driveKey.Dispose() }
    $enableRejected = $false
    try { Enable-ExplorerDirsFolderVerb -Executable $executable -RegistryBase $conflictBase | Out-Null }
    catch {
        if ($_.Exception.Message -notlike 'Registry verb already exists or was edited:*') { throw }
        $enableRejected = $true
    }
    Assert $enableRejected 'an existing Drive verb conflict must reject installation'
    Assert-State $conflictBase @('missing', 'conflict')

    $modifiedBase = $registryBases[2]
    Enable-ExplorerDirsFolderVerb -Executable $executable -RegistryBase $modifiedBase | Out-Null
    $changedCommand = 'C:\External\FolderHandler.exe "%1"'
    $commandKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        "$modifiedBase\Directory\shell\ExplorerDirs.Open\command", $true)
    try { $commandKey.SetValue('', $changedCommand, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $commandKey.Dispose() }
    $disableRejected = $false
    try { Disable-ExplorerDirsFolderVerb -RegistryBase $modifiedBase | Out-Null }
    catch {
        if ($_.Exception.Message -ne 'A folder verb was changed externally; no registry key was removed.') { throw }
        $disableRejected = $true
    }
    Assert $disableRejected 'uninstall must refuse an externally replaced command'
    Assert-State $modifiedBase @('conflict', 'owned')
    $commandKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        "$modifiedBase\Directory\shell\ExplorerDirs.Open\command", $false)
    try { Assert ($commandKey.GetValue('') -eq $changedCommand) 'uninstall must preserve the external command verbatim' }
    finally { $commandKey.Dispose() }

    Write-Output 'Folder verb isolated registry smoke: PASS'
}
finally {
    foreach ($registryBase in $registryBases) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryBase, $false)
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    Remove-Module FolderVerb -ErrorAction SilentlyContinue
}
