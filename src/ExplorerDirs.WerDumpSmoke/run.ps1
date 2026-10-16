$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\.state'))
$id = [guid]::NewGuid().ToString('N')
$registryRoot = 'Software\ExplorerDirs\Smoke\' + $id
$registryBase = $registryRoot + '\LocalDumps'
$dumpFolder = [IO.Path]::GetFullPath((Join-Path $stateRoot ('wer dump smoke ' + $id)))
if ($registryRoot -notmatch '^Software\\ExplorerDirs\\Smoke\\[0-9a-f]{32}$' -or
    -not $dumpFolder.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -or
    $null -ne [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryRoot, $false)) {
    throw 'WER smoke roots are not isolated.'
}
Import-Module (Join-Path $projectRoot 'scripts\WerDump.psm1') -Force

try {
    $initial = Get-ExplorerDirsWerDumpState -AppName 'Q-Dir.exe' -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase
    if ($initial.State -ne 'missing') { throw 'A WER smoke key already exists.' }
    try {
        Enable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' -DumpFolder 'relative-folder' `
            -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
        throw 'A relative dump folder was accepted.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'A relative dump folder was accepted.') { throw }
    }

    Enable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' -DumpFolder $dumpFolder `
        -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
    Enable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' -DumpFolder $dumpFolder `
        -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
    $installed = Get-ExplorerDirsWerDumpState -AppName 'Q-Dir.exe' `
        -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase
    if ($installed.State -ne 'owned' -or -not (Test-Path -LiteralPath $dumpFolder -PathType Container)) {
        throw 'WER dump configuration was not installed idempotently.'
    }
    try {
        Enable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' -DumpFolder (Join-Path $stateRoot 'other-dumps') `
            -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
        throw 'An owned WER setting was silently repointed.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'An owned WER setting was silently repointed.') { throw }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryBase + '\Q-Dir.exe', $true)
    try {
        if ($key.GetValueKind('DumpFolder') -ne [Microsoft.Win32.RegistryValueKind]::ExpandString -or
            $key.GetValueKind('DumpCount') -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
            $key.GetValue('DumpCount') -ne 2 -or $key.GetValue('DumpType') -ne 1) {
            throw 'WER settings are not a two-file mini-dump policy.'
        }
        $key.SetValue('DumpCount', 3, [Microsoft.Win32.RegistryValueKind]::DWord)
    }
    finally { $key.Dispose() }
    try {
        Disable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' `
            -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
        throw 'Externally edited WER settings were deleted.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'Externally edited WER settings were deleted.') { throw }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryBase + '\Q-Dir.exe', $true)
    try { $key.SetValue('DumpCount', 2, [Microsoft.Win32.RegistryValueKind]::DWord) }
    finally { $key.Dispose() }
    Disable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' `
        -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
    if ((Get-ExplorerDirsWerDumpState -AppName 'Q-Dir.exe' `
            -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase).State -ne 'missing' -or
        -not (Test-Path -LiteralPath $dumpFolder -PathType Container)) {
        throw 'Disable removed diagnostic data or left the owned key behind.'
    }

    $foreign = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryBase + '\Q-Dir.exe', $true)
    try { $foreign.SetValue('DumpType', 2, [Microsoft.Win32.RegistryValueKind]::DWord) }
    finally { $foreign.Dispose() }
    try {
        Enable-ExplorerDirsWerDump -AppName 'Q-Dir.exe' -DumpFolder $dumpFolder `
            -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase | Out-Null
        throw 'An existing foreign WER configuration was overwritten.'
    }
    catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -eq 'An existing foreign WER configuration was overwritten.') { throw }
    }
    $foreign = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryBase + '\Q-Dir.exe', $false)
    try {
        if ($foreign.GetValue('DumpType') -ne 2 -or
            (Get-ExplorerDirsWerDumpState -AppName 'Q-Dir.exe' `
                -Hive ([Microsoft.Win32.Registry]::CurrentUser) -RegistryBase $registryBase).State -ne 'conflict') {
            throw 'Foreign WER settings were modified.'
        }
    }
    finally { $foreign.Dispose() }
    'WER dump smoke: PASS'
}
finally {
    if ($registryRoot -match '^Software\\ExplorerDirs\\Smoke\\[0-9a-f]{32}$') {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryRoot, $false)
    }
    if ($dumpFolder.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $dumpFolder)) {
        Remove-Item -LiteralPath $dumpFolder -Recurse -Force
    }
}
