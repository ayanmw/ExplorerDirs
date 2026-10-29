$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FolderVerb.psm1') -Force

$id = [Guid]::NewGuid().ToString('N')
$registryBases = @(
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Owned",
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Conflict",
    "Software\ExplorerDirs.Tests\FolderVerb-$id-Modified",
    "Software\ExplorerDirs.Tests\FolderVerb-$id-WriteFailure"
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
        Assert ($directoryKey.GetValue('') -eq '在 Explorer Dirs 中打开') `
            'the folder context-menu label must match the Chinese application UI'
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

    $rollbackBase = $registryBases[3]
    $partialPath = "$rollbackBase\Directory\shell\ExplorerDirs.Open"
    $emptyKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($partialPath, $true)
    $emptyKey.Dispose()
    $module = Get-Module FolderVerb
    $removedEmpty = & $module {
        param($path, $target, $expectedCommand)
        Remove-VerbOnRollback $path $target $expectedCommand
    } $partialPath $executable ('"' + $executable + '" "%1"')
    Assert $removedEmpty 'rollback must remove an empty key created before the first value write'
    Assert-State $rollbackBase @('missing', 'missing')

    $partialKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($partialPath, $true)
    try {
        $partialKey.SetValue('ExplorerDirsOwner', 'ExplorerDirs.FolderVerb.v1', [Microsoft.Win32.RegistryValueKind]::String)
        $partialKey.SetValue('', '在 Explorer Dirs 中打开', [Microsoft.Win32.RegistryValueKind]::String)
        $partialKey.SetValue('ExplorerDirsTarget', $executable, [Microsoft.Win32.RegistryValueKind]::String)
        $partialKey.SetValue('Icon', $executable, [Microsoft.Win32.RegistryValueKind]::String)
        $emptyCommand = $partialKey.CreateSubKey('command', $true)
        $emptyCommand.Dispose()
    }
    finally { $partialKey.Dispose() }

    $removedPartial = & $module {
        param($path, $target, $expectedCommand)
        Remove-VerbOnRollback $path $target $expectedCommand
    } $partialPath $executable ('"' + $executable + '" "%1"')
    Assert $removedPartial 'rollback must remove a partial key containing only values written by this install'
    Assert-State $rollbackBase @('missing', 'missing')

    $editedPartial = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($partialPath, $true)
    try {
        $editedPartial.SetValue('ExplorerDirsOwner', 'ExplorerDirs.FolderVerb.v1', [Microsoft.Win32.RegistryValueKind]::String)
        $editedPartial.SetValue('', '在 Explorer Dirs 中打开', [Microsoft.Win32.RegistryValueKind]::String)
        $editedPartial.SetValue('ExplorerDirsTarget', $executable, [Microsoft.Win32.RegistryValueKind]::String)
        $editedPartial.SetValue('Icon', 'C:\External\FolderHandler.exe', [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally { $editedPartial.Dispose() }
    $preservedPartial = & $module {
        param($path, $target, $expectedCommand)
        Remove-VerbOnRollback $path $target $expectedCommand
    } $partialPath $executable ('"' + $executable + '" "%1"')
    Assert (-not $preservedPartial) 'rollback must preserve an externally edited partial key'
    Assert-State $rollbackBase @('conflict', 'missing')

    Write-Output 'Folder verb isolated registry smoke: PASS'
}
finally {
    foreach ($registryBase in $registryBases) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryBase, $false)
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    Remove-Module FolderVerb -ErrorAction SilentlyContinue
}
