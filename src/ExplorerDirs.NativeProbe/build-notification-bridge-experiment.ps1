param([switch]$TraceSizeRequests, [switch]$NamespaceAttributes, [switch]$DisplayText)

$ErrorActionPreference = 'Stop'

$outputDirectory = Join-Path $PSScriptRoot 'bin\native-notification-bridge-experiment-x64'
if ($TraceSizeRequests) { $outputDirectory += '-trace' }
if ($NamespaceAttributes) { $outputDirectory += '-namespace-attributes' }
if ($DisplayText) { $outputDirectory += '-display-text' }
$source = Join-Path $PSScriptRoot 'StaticShellFolderAdapter.cpp'
$definition = Join-Path $PSScriptRoot 'ProductShellFolderAdapter.def'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vcvarsCandidates = @()
if (Test-Path -LiteralPath $vswhere) {
    $installations = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvarsCandidates += $installations | ForEach-Object {
        Join-Path $_ 'VC\Auxiliary\Build\vcvars64.bat'
    }
}
$vcvarsCandidates += 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$vcvars = $vcvarsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$output = Join-Path $outputDirectory 'ExplorerDirs.ShellNamespace.dll'

if (-not $vcvars) { throw 'Visual Studio 2022 x64 C++ tools were not found. Install the Desktop development with C++ workload.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$objectFile = Join-Path $outputDirectory 'StaticShellFolderAdapter.obj'
$importLibrary = Join-Path $outputDirectory 'ExplorerDirs.ShellNamespace.lib'
$traceDefine = if ($TraceSizeRequests) { '/DEXPLORERDIRS_SIZE_TRACE_EXPERIMENT' } else { '' }
if ($NamespaceAttributes) { $traceDefine += ' /DEXPLORERDIRS_NAMESPACE_ATTRIBUTES_EXPERIMENT' }
if ($DisplayText) { $traceDefine += ' /DEXPLORERDIRS_FOLDER_SIZE_DISPLAY' }
$command = 'call "{0}" >nul && cl.exe /nologo /LD /EHsc /DEXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS {6} /W4 /WX /Fo:"{3}" "{1}" /link /DEF:"{5}" shell32.lib ole32.lib oleaut32.lib propsys.lib user32.lib advapi32.lib shlwapi.lib comctl32.lib /IMPLIB:"{4}" /OUT:"{2}"' -f $vcvars, $source, $output, $objectFile, $importLibrary, $definition, $traceDefine
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Product Shell-folder adapter build failed with exit code $LASTEXITCODE." }
Write-Output "Built: $output"
