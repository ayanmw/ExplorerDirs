param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Version = 'preview-0.3.3'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'releases'))
$project = Join-Path $projectRoot 'src\ExplorerDirs.App\ExplorerDirs.App.csproj'
$staging = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot ('.staging-' + $Version + '-' + $PID)))
$destination = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
$buildOutput = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('src\ExplorerDirs.App\bin\publish-build\' + $Version)))

foreach ($path in @($staging, $destination)) {
    if (-not $path.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path escapes releases directory: $path"
    }
}

if (Test-Path -LiteralPath $destination) {
    throw "Release already exists: $destination"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
dotnet publish $project --configuration Release --self-contained false --output $staging "-p:OutputPath=$buildOutput"
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed. Staging files were left at $staging for inspection."
}

$exe = Join-Path $staging 'ExplorerDirs.App.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish output is missing $exe"
}

# A same-volume move makes a complete release visible under its final name.
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $staging 'SHA256.txt') -Value ("ExplorerDirs.App.exe  " + $hash) -Encoding ASCII
Move-Item -LiteralPath $staging -Destination $destination
Write-Output $destination
