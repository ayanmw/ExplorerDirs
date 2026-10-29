$ErrorActionPreference = 'Stop'

$outputDirectory = Join-Path $PSScriptRoot 'bin\native-x64'
$source = Join-Path $PSScriptRoot 'StaticShellFolderAdapter.cpp'
$definition = Join-Path $PSScriptRoot 'StaticShellFolderAdapter.def'
$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$output = Join-Path $outputDirectory 'ExplorerDirs.StaticShellFolderAdapter.dll'

if (-not (Test-Path -LiteralPath $vcvars)) { throw "Visual Studio x64 C++ tools were not found: $vcvars" }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$objectFile = Join-Path $outputDirectory 'StaticShellFolderAdapter.obj'
$importLibrary = Join-Path $outputDirectory 'ExplorerDirs.StaticShellFolderAdapter.lib'
$command = 'call "{0}" >nul && cl.exe /nologo /LD /EHsc /W4 /WX /Fo:"{3}" "{1}" /link /DEF:"{5}" shell32.lib ole32.lib oleaut32.lib propsys.lib user32.lib /IMPLIB:"{4}" /OUT:"{2}"' -f $vcvars, $source, $output, $objectFile, $importLibrary, $definition
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Native Shell-folder adapter build failed with exit code $LASTEXITCODE." }
Write-Output "Built: $output"
