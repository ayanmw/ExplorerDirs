$ErrorActionPreference = 'Stop'

$outputDirectory = Join-Path $PSScriptRoot 'bin\native-product-x64'
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
$command = 'call "{0}" >nul && cl.exe /nologo /LD /EHsc /W4 /WX /Fo:"{3}" "{1}" /link /DEF:"{5}" shell32.lib ole32.lib oleaut32.lib propsys.lib user32.lib /IMPLIB:"{4}" /OUT:"{2}"' -f $vcvars, $source, $output, $objectFile, $importLibrary, $definition
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Product Shell-folder adapter build failed with exit code $LASTEXITCODE." }
Write-Output "Built: $output"
