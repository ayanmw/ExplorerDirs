$ErrorActionPreference = 'Stop'
$ownerPrefix = 'ExplorerDirs.WerDump.v1'
$miniDumpType = 1
$maxDumpCount = 2

function Get-WerDumpPath([string]$registryBase, [string]$appName) {
    $registryBase.TrimEnd('\') + '\' + $appName
}

function Get-WerDumpOwner([string]$folder) {
    "$ownerPrefix|$folder|$miniDumpType|$maxDumpCount"
}

function Test-OwnedWerDumpKey([Microsoft.Win32.RegistryKey]$key) {
    if ($null -eq $key -or $key.GetSubKeyNames().Count -ne 0) { return $false }
    $names = @($key.GetValueNames() | Sort-Object)
    if (($names -join '|') -ne ((@('DumpCount', 'DumpFolder', 'DumpType', 'ExplorerDirsOwner') |
        Sort-Object) -join '|')) { return $false }
    if ($key.GetValueKind('DumpFolder') -ne [Microsoft.Win32.RegistryValueKind]::ExpandString -or
        $key.GetValueKind('DumpCount') -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
        $key.GetValueKind('DumpType') -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
        $key.GetValueKind('ExplorerDirsOwner') -ne [Microsoft.Win32.RegistryValueKind]::String -or
        $key.GetValue('DumpCount') -ne $maxDumpCount -or
        $key.GetValue('DumpType') -ne $miniDumpType) { return $false }
    $folder = [string]$key.GetValue('DumpFolder', $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    if ([string]::IsNullOrWhiteSpace($folder) -or -not [IO.Path]::IsPathRooted($folder)) {
        return $false
    }
    try {
        if ([IO.Path]::GetFullPath($folder) -ne $folder) { return $false }
    }
    catch { return $false }
    return $key.GetValue('ExplorerDirsOwner') -eq (Get-WerDumpOwner $folder)
}

function Get-ExplorerDirsWerDumpState {
    param(
        [ValidateSet('ExplorerDirs.App.exe', 'Q-Dir.exe')][string]$AppName = 'ExplorerDirs.App.exe',
        [Microsoft.Win32.RegistryKey]$Hive = [Microsoft.Win32.Registry]::LocalMachine,
        [string]$RegistryBase = 'SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps'
    )
    $path = Get-WerDumpPath $RegistryBase $AppName
    $key = $Hive.OpenSubKey($path, $false)
    try {
        $state = if ($null -eq $key) { 'missing' }
            elseif (Test-OwnedWerDumpKey $key) { 'owned' }
            else { 'conflict' }
        [pscustomobject]@{
            AppName = $AppName
            RegistryPath = $Hive.Name + '\' + $path
            State = $state
            DumpFolder = if ($state -eq 'owned') {
                [string]$key.GetValue('DumpFolder', $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            } else { $null }
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Test-PartialWerDumpKey([Microsoft.Win32.RegistryKey]$key, [string]$folder) {
    if ($null -eq $key -or $key.GetSubKeyNames().Count -ne 0 -or
        $key.GetValue('ExplorerDirsOwner') -ne (Get-WerDumpOwner $folder)) { return $false }
    foreach ($name in $key.GetValueNames()) {
        if ($name -notin @('ExplorerDirsOwner', 'DumpFolder', 'DumpType', 'DumpCount')) { return $false }
    }
    if ($key.GetValueNames() -contains 'DumpFolder' -and
        $key.GetValue('DumpFolder', $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -ne $folder) { return $false }
    if ($key.GetValueNames() -contains 'DumpType' -and
        $key.GetValue('DumpType') -ne $miniDumpType) { return $false }
    if ($key.GetValueNames() -contains 'DumpCount' -and
        $key.GetValue('DumpCount') -ne $maxDumpCount) { return $false }
    return $true
}

function Enable-ExplorerDirsWerDump {
    param(
        [ValidateSet('ExplorerDirs.App.exe', 'Q-Dir.exe')][string]$AppName = 'ExplorerDirs.App.exe',
        [Parameter(Mandatory = $true)][string]$DumpFolder,
        [Microsoft.Win32.RegistryKey]$Hive = [Microsoft.Win32.Registry]::LocalMachine,
        [string]$RegistryBase = 'SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps'
    )
    if (-not [IO.Path]::IsPathRooted($DumpFolder)) { throw 'DumpFolder must be an absolute path.' }
    $folder = [IO.Path]::GetFullPath($DumpFolder)
    $state = Get-ExplorerDirsWerDumpState -AppName $AppName -Hive $Hive -RegistryBase $RegistryBase
    if ($state.State -eq 'conflict' -or
        ($state.State -eq 'owned' -and $state.DumpFolder -ne $folder)) {
        throw 'A WER setting already exists or was changed externally; nothing was overwritten.'
    }
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    if ($state.State -eq 'owned') { return $state }

    $parent = $Hive.CreateSubKey($RegistryBase, $true)
    if ($null -eq $parent) { throw 'Cannot open the WER LocalDumps registry key.' }
    try {
        $existing = $parent.OpenSubKey($AppName, $false)
        if ($null -ne $existing) {
            $existing.Dispose()
            throw 'A WER setting appeared during Enable; nothing was overwritten.'
        }
        $key = $parent.CreateSubKey($AppName, $true)
        if ($null -eq $key) { throw 'Cannot create the per-application WER setting.' }
        try {
            $key.SetValue('ExplorerDirsOwner', (Get-WerDumpOwner $folder),
                [Microsoft.Win32.RegistryValueKind]::String)
            $key.SetValue('DumpFolder', $folder, [Microsoft.Win32.RegistryValueKind]::ExpandString)
            $key.SetValue('DumpType', $miniDumpType, [Microsoft.Win32.RegistryValueKind]::DWord)
            $key.SetValue('DumpCount', $maxDumpCount, [Microsoft.Win32.RegistryValueKind]::DWord)
        }
        catch {
            $key.Dispose()
            $partial = $parent.OpenSubKey($AppName, $false)
            try {
                if (Test-PartialWerDumpKey $partial $folder) {
                    $partial.Dispose()
                    $parent.DeleteSubKey($AppName, $false)
                }
            }
            finally { if ($null -ne $partial) { $partial.Dispose() } }
            throw
        }
        finally { $key.Dispose() }
    }
    finally { $parent.Dispose() }
    Get-ExplorerDirsWerDumpState -AppName $AppName -Hive $Hive -RegistryBase $RegistryBase
}

function Disable-ExplorerDirsWerDump {
    param(
        [ValidateSet('ExplorerDirs.App.exe', 'Q-Dir.exe')][string]$AppName = 'ExplorerDirs.App.exe',
        [Microsoft.Win32.RegistryKey]$Hive = [Microsoft.Win32.Registry]::LocalMachine,
        [string]$RegistryBase = 'SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps'
    )
    $state = Get-ExplorerDirsWerDumpState -AppName $AppName -Hive $Hive -RegistryBase $RegistryBase
    if ($state.State -eq 'missing') { return $state }
    if ($state.State -ne 'owned') {
        throw 'The WER setting changed externally; no registry key was removed.'
    }
    $parent = $Hive.OpenSubKey($RegistryBase, $true)
    if ($null -eq $parent) { throw 'The WER setting changed before removal.' }
    try {
        $key = $parent.OpenSubKey($AppName, $false)
        try {
            if (-not (Test-OwnedWerDumpKey $key) -or
                $key.GetValue('DumpFolder', $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -ne $state.DumpFolder) {
                throw 'The WER setting changed during removal; no registry key was removed.'
            }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
        $parent.DeleteSubKey($AppName, $false)
    }
    finally { $parent.Dispose() }
    Get-ExplorerDirsWerDumpState -AppName $AppName -Hive $Hive -RegistryBase $RegistryBase
}

Export-ModuleMember -Function Get-ExplorerDirsWerDumpState, Enable-ExplorerDirsWerDump, Disable-ExplorerDirsWerDump
