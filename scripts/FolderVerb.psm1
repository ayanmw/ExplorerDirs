$ErrorActionPreference = 'Stop'
$verbName = 'ExplorerDirs.Open'
$owner = 'ExplorerDirs.FolderVerb.v1'
$label = '在 Explorer Dirs 中打开'

function Get-VerbPaths([string]$basePath) {
    @(
        "$basePath\Directory\shell\$verbName",
        "$basePath\Drive\shell\$verbName"
    )
}

function Get-VerbCommand([string]$executable) {
    if ($executable.IndexOfAny([char[]]@('"', '%', "`r", "`n")) -ge 0) {
        throw 'Executable path contains characters unsafe for a Shell command.'
    }
    '"' + $executable + '" "%1"'
}

function Test-ExplorerDirsRoutingCapability {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Sha256
    )
    $path = Join-Path (Split-Path -Parent $Executable) 'FolderRoutingCapability.ini'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $lines = @(Get-Content -LiteralPath $path | Where-Object { $_.Trim().Length -gt 0 })
    return $lines.Count -eq 3 -and
        $lines[0] -eq 'FormatVersion=1' -and
        $lines[1] -eq 'DirectoryArgument=true' -and
        $lines[2] -eq "ExecutableSha256=$Sha256"
}

function Test-OwnedVerb([Microsoft.Win32.RegistryKey]$key) {
    if ($null -eq $key -or $key.GetValue('ExplorerDirsOwner') -ne $owner -or
        $key.GetValue('') -ne $label) { return $false }
    $target = [string]$key.GetValue('ExplorerDirsTarget')
    if ([string]::IsNullOrWhiteSpace($target) -or
        [IO.Path]::GetFileName($target) -ne 'ExplorerDirs.App.exe' -or
        $key.GetValue('Icon') -ne $target) { return $false }
    try { $expected = Get-VerbCommand $target }
    catch { return $false }
    $names = @($key.GetValueNames() | Sort-Object)
    if ((@($names) -join '|') -ne ((@('', 'ExplorerDirsOwner', 'ExplorerDirsTarget', 'Icon') | Sort-Object) -join '|') -or
        (@($key.GetSubKeyNames()) -join '|') -ne 'command') { return $false }
    $commandKey = $key.OpenSubKey('command', $false)
    if ($null -eq $commandKey) { return $false }
    try {
        return (@($commandKey.GetValueNames()) -join '|') -eq '' -and
            $commandKey.GetSubKeyNames().Count -eq 0 -and
            $commandKey.GetValue('') -eq $expected
    }
    finally { $commandKey.Dispose() }
}

function Test-VerbRollbackState([Microsoft.Win32.RegistryKey]$key, [string]$Executable, [string]$Command) {
    if ($null -eq $key) { return $false }
    $expectedValues = @(
        [pscustomobject]@{ Name = 'ExplorerDirsOwner'; Value = $owner },
        [pscustomobject]@{ Name = ''; Value = $label },
        [pscustomobject]@{ Name = 'ExplorerDirsTarget'; Value = $Executable },
        [pscustomobject]@{ Name = 'Icon'; Value = $Executable }
    )
    $valueNames = @($key.GetValueNames())
    # An empty leaf can result from failure before the first write; its path is tracked as newly created by this attempt.
    if ($valueNames.Count -gt $expectedValues.Count) { return $false }
    # Registry value enumeration order is unspecified; validate membership against the write prefix instead.
    for ($index = 0; $index -lt $valueNames.Count; $index++) {
        $expected = $expectedValues[$index]
        if (-not ($valueNames -ccontains $expected.Name) -or
            $key.GetValueKind($expected.Name) -ne [Microsoft.Win32.RegistryValueKind]::String -or
            $key.GetValue($expected.Name) -cne $expected.Value) { return $false }
    }
    $subkeyNames = @($key.GetSubKeyNames())
    if ($subkeyNames.Count -gt 1 -or ($subkeyNames.Count -eq 1 -and $subkeyNames[0] -cne 'command')) { return $false }
    if ($subkeyNames.Count -eq 1) {
        if ($valueNames.Count -ne $expectedValues.Count) { return $false }
        $commandKey = $key.OpenSubKey('command', $false)
        if ($null -eq $commandKey) { return $false }
        try {
            $commandValueNames = @($commandKey.GetValueNames())
            if ($commandKey.GetSubKeyNames().Count -ne 0 -or $commandValueNames.Count -gt 1 -or
                ($commandValueNames.Count -eq 1 -and
                    ($commandValueNames[0] -cne '' -or
                     $commandKey.GetValueKind('') -ne [Microsoft.Win32.RegistryValueKind]::String -or
                     $commandKey.GetValue('') -cne $Command))) { return $false }
        }
        finally { $commandKey.Dispose() }
    }
    return $true
}

function Remove-VerbOnRollback([string]$Path, [string]$Executable, [string]$Command) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Path, $false)
    try {
        if ($null -eq $key -or -not (Test-VerbRollbackState $key $Executable $Command)) { return $false }
        $key.Dispose()
        $key = $null
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($Path, $false)
        return $true
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Get-ExplorerDirsFolderVerbState {
    param([string]$RegistryBase = 'Software\Classes')
    foreach ($path in Get-VerbPaths $RegistryBase) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $false)
        try {
            $state = if ($null -eq $key) { 'missing' }
                elseif (Test-OwnedVerb $key) { 'owned' }
                else { 'conflict' }
            [pscustomobject]@{ Path = "HKCU\$path"; State = $state }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
    }
}

function Enable-ExplorerDirsFolderVerb {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [string]$RegistryBase = 'Software\Classes'
    )
    $fullExecutable = [IO.Path]::GetFullPath($Executable)
    if ([IO.Path]::GetFileName($fullExecutable) -ne 'ExplorerDirs.App.exe' -or
        -not [IO.File]::Exists($fullExecutable)) { throw 'A real ExplorerDirs.App.exe is required.' }
    $command = Get-VerbCommand $fullExecutable
    $paths = @(Get-VerbPaths $RegistryBase)
    foreach ($path in $paths) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $false)
        try {
            if ($null -ne $key -and -not (Test-OwnedVerb $key)) {
                throw "Registry verb already exists or was edited: HKCU\$path"
            }
            if ($null -ne $key -and $key.GetValue('ExplorerDirsTarget') -ne $fullExecutable) {
                throw "Registry verb points to another executable: HKCU\$path"
            }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
    }

    $created = [Collections.Generic.List[string]]::new()
    try {
        foreach ($path in $paths) {
            $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $false)
            if ($null -ne $existing) { $existing.Dispose(); continue }
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path, $true)
            $created.Add($path)
            try {
                $key.SetValue('ExplorerDirsOwner', $owner, [Microsoft.Win32.RegistryValueKind]::String)
                $key.SetValue('', $label, [Microsoft.Win32.RegistryValueKind]::String)
                $key.SetValue('ExplorerDirsTarget', $fullExecutable, [Microsoft.Win32.RegistryValueKind]::String)
                $key.SetValue('Icon', $fullExecutable, [Microsoft.Win32.RegistryValueKind]::String)
                $commandKey = $key.CreateSubKey('command', $true)
                try { $commandKey.SetValue('', $command, [Microsoft.Win32.RegistryValueKind]::String) }
                finally { $commandKey.Dispose() }
            }
            finally { $key.Dispose() }
        }
    }
    catch {
        foreach ($path in $created) {
            # A failed write can leave a partial key; preserve it if any external value or child was added/changed.
            Remove-VerbOnRollback $path $fullExecutable $command | Out-Null
        }
        throw
    }
    @(Get-ExplorerDirsFolderVerbState -RegistryBase $RegistryBase)
}

function Disable-ExplorerDirsFolderVerb {
    param([string]$RegistryBase = 'Software\Classes')
    $states = @(Get-ExplorerDirsFolderVerbState -RegistryBase $RegistryBase)
    if (@($states | Where-Object State -eq 'conflict').Count -gt 0) {
        throw 'A folder verb was changed externally; no registry key was removed.'
    }
    foreach ($path in Get-VerbPaths $RegistryBase) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $false)
        try {
            if ($null -ne $key -and -not (Test-OwnedVerb $key)) {
                throw "Registry verb changed during removal: HKCU\$path"
            }
            if ($null -ne $key) {
                $key.Dispose()
                [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path, $false)
            }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
    }
    @(Get-ExplorerDirsFolderVerbState -RegistryBase $RegistryBase)
}

Export-ModuleMember -Function Get-ExplorerDirsFolderVerbState, Enable-ExplorerDirsFolderVerb, Disable-ExplorerDirsFolderVerb, Test-ExplorerDirsRoutingCapability
