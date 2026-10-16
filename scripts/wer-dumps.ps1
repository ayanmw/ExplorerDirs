param(
    [ValidateSet('Status', 'Preview', 'Enable', 'Disable')]
    [string]$Mode = 'Status',
    [ValidateSet('ExplorerDirs.App.exe', 'Q-Dir.exe')]
    [string]$AppName = 'ExplorerDirs.App.exe',
    [string]$DumpFolder,
    [switch]$ConfirmDiagnosticCollection
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'WerDump.psm1') -Force

if ([string]::IsNullOrWhiteSpace($DumpFolder)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $DumpFolder = Join-Path (Join-Path $localData 'ExplorerDirs\WerDumps') $AppName
}
$fullFolder = [IO.Path]::GetFullPath($DumpFolder)
$state = Get-ExplorerDirsWerDumpState -AppName $AppName

if ($Mode -eq 'Status') { $state; return }
if ($Mode -eq 'Preview') {
    [pscustomobject]@{
        AppName = $AppName
        RegistryPath = $state.RegistryPath
        CurrentState = $state.State
        CurrentDumpFolder = $state.DumpFolder
        ProposedDumpFolder = $fullFolder
        DumpType = 'Mini (1)'
        MaximumDumps = 2
        RequiresAdministrator = $true
        WritesOnPreview = $false
    }
    return
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Enable and Disable require an elevated PowerShell process. No WER setting was changed.'
}
if ($Mode -eq 'Enable') {
    if (-not $ConfirmDiagnosticCollection) {
        throw 'Enable requires -ConfirmDiagnosticCollection after reviewing Preview. No WER setting was changed.'
    }
    Enable-ExplorerDirsWerDump -AppName $AppName -DumpFolder $fullFolder
    return
}
Disable-ExplorerDirsWerDump -AppName $AppName
