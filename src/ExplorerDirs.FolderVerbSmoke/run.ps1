$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state'))
$id = [guid]::NewGuid().ToString('N')
$runRoot = [IO.Path]::GetFullPath((Join-Path $stateRoot ('folder verb smoke ' + $id)))
$registryRoot = 'Software\ExplorerDirs\Smoke\' + $id
$registryBase = $registryRoot + '\Classes'
if (-not $runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    $registryRoot -notmatch '^Software\\ExplorerDirs\\Smoke\\[0-9a-f]{32}$') {
    throw 'Smoke paths are outside the isolated test roots.'
}
if ($null -ne [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryRoot, $false)) {
    throw 'The isolated registry root already exists.'
}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$exe = Join-Path $runRoot 'ExplorerDirs.App.exe'
[IO.File]::WriteAllBytes($exe, [byte[]]@())
Import-Module (Join-Path $projectRoot 'scripts\FolderVerb.psm1') -Force

try {
    $sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $capability = Join-Path $runRoot 'FolderRoutingCapability.ini'
    [IO.File]::WriteAllLines($capability, [string[]]@(
        'FormatVersion=1', 'DirectoryArgument=true', "ExecutableSha256=$sha256"
    ))
    if (-not (Test-ExplorerDirsRoutingCapability -Executable $exe -Sha256 $sha256) -or
        (Test-ExplorerDirsRoutingCapability -Executable $exe -Sha256 ('0' * 64))) {
        throw 'Routing capability does not bind to the executable hash.'
    }
    $initial = @(Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)
    if (@($initial | Where-Object State -ne 'missing').Count -ne 0) { throw 'Test verbs already exist.' }

    Enable-ExplorerDirsFolderVerb -Executable $exe -RegistryBase $registryBase | Out-Null
    Enable-ExplorerDirsFolderVerb -Executable $exe -RegistryBase $registryBase | Out-Null
    $installed = @(Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)
    if (@($installed | Where-Object State -ne 'owned').Count -ne 0) { throw 'Test verbs were not installed.' }
    $commandPath = $registryBase + '\Directory\shell\ExplorerDirs.Open\command'
    $commandKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($commandPath, $true)
    try {
        $expected = '"' + $exe + '" "%1"'
        if ($commandKey.GetValue('') -ne $expected) { throw 'Folder argument was not quoted.' }
        $commandKey.SetValue('', 'externally edited', [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally { $commandKey.Dispose() }
    try {
        Disable-ExplorerDirsFolderVerb -RegistryBase $registryBase | Out-Null
        throw 'Externally edited verb was deleted.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'Externally edited verb was deleted.') { throw }
    }
    $preserved = @(Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)
    if ($preserved[0].State -ne 'conflict' -or $preserved[1].State -ne 'owned') {
        throw 'A conflict did not preserve both verbs.'
    }
    $commandKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($commandPath, $true)
    try { $commandKey.SetValue('', $expected, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $commandKey.Dispose() }
    Disable-ExplorerDirsFolderVerb -RegistryBase $registryBase | Out-Null
    $removed = @(Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)
    if (@($removed | Where-Object State -ne 'missing').Count -ne 0) { throw 'Test verbs were not removed.' }

    $foreignPath = $registryBase + '\Directory\shell\ExplorerDirs.Open'
    $foreignKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($foreignPath, $true)
    try { $foreignKey.SetValue('', 'Foreign command', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $foreignKey.Dispose() }
    try {
        Enable-ExplorerDirsFolderVerb -Executable $exe -RegistryBase $registryBase | Out-Null
        throw 'Existing foreign verb was overwritten.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'Existing foreign verb was overwritten.') { throw }
    }
    $foreignKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($foreignPath, $false)
    try {
        if ($foreignKey.GetValue('') -ne 'Foreign command' -or
            (Get-ExplorerDirsFolderVerbState -RegistryBase $registryBase)[1].State -ne 'missing') {
            throw 'A foreign verb or the other class was modified.'
        }
    }
    finally { $foreignKey.Dispose() }
    'Folder verb smoke: PASS'
}
finally {
    if ($registryRoot -match '^Software\\ExplorerDirs\\Smoke\\[0-9a-f]{32}$') {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryRoot, $false)
    }
    if ($runRoot.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $runRoot)) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
