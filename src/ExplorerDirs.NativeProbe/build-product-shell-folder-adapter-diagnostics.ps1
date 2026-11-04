param([switch]$SizeSortPolicyExperiment)

$ErrorActionPreference = 'Stop'

$outputDirectory = Join-Path $PSScriptRoot 'bin\native-product-diagnostics-x64'
if ($SizeSortPolicyExperiment) { $outputDirectory += '-sort-policy' }
$experimentFlags = if ($SizeSortPolicyExperiment) { '/DEXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT' } else { '' }
$source = Join-Path $PSScriptRoot 'StaticShellFolderAdapter.cpp'
$definition = Join-Path $PSScriptRoot 'ProductShellFolderAdapterDiagnostics.def'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vcvarsCandidates = @()
if (Test-Path -LiteralPath $vswhere) {
    $installations = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvarsCandidates += $installations | ForEach-Object { Join-Path $_ 'VC\Auxiliary\Build\vcvars64.bat' }
}
$vcvarsCandidates += 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$vcvars = $vcvarsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$output = Join-Path $outputDirectory 'ExplorerDirs.ShellNamespace.Diagnostics.dll'

if (-not $vcvars) { throw 'Visual Studio 2022 x64 C++ tools were not found.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$objectFile = Join-Path $outputDirectory 'StaticShellFolderAdapter.obj'
$importLibrary = Join-Path $outputDirectory 'ExplorerDirs.ShellNamespace.Diagnostics.lib'
$command = 'call "{0}" >nul && cl.exe /nologo /LD /EHsc /O2 /DNDEBUG /MD /W4 /WX {6} /DEXPLORERDIRS_PRODUCT_DIAGNOSTICS /DEXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS /DEXPLORERDIRS_FOLDER_SIZE_DISPLAY /Fo:"{3}" "{1}" /link /Brepro /DEF:"{5}" shell32.lib ole32.lib oleaut32.lib propsys.lib user32.lib advapi32.lib shlwapi.lib comctl32.lib /IMPLIB:"{4}" /OUT:"{2}"' -f $vcvars, $source, $output, $objectFile, $importLibrary, $definition, $experimentFlags
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Product diagnostic Shell-folder adapter build failed with exit code $LASTEXITCODE." }
Write-Output "Built diagnostic-only adapter: $output"
