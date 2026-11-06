[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $projectRoot ('src/.state/background-checks/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$passed = $false
$sourceCommit = $null
$sourceStatus = @()

function Invoke-BackgroundCheck {
    param([string]$Name, [string]$Program, [string[]]$CommandArguments)
    $logPath = Join-Path $runDirectory ($Name + '.log')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Write-Output "Running $Name"
    & $Program @CommandArguments 2>&1 | Tee-Object -FilePath $logPath
    $commandExit = $LASTEXITCODE
    $watch.Stop()
    $results.Add([pscustomobject]@{ Name = $Name; ExitCode = $commandExit; ElapsedMilliseconds = $watch.ElapsedMilliseconds; Log = $logPath })
    if ($commandExit -ne 0) { throw "$Name failed with exit code $commandExit. See $logPath" }
}

Push-Location $projectRoot
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $sourceStatus = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status.' }

    foreach ($adapter in @('static-shell-folder-adapter', 'product-shell-folder-adapter', 'product-shell-folder-adapter-diagnostics')) {
        Invoke-BackgroundCheck ('build-' + $adapter) 'pwsh' @('-NoProfile', '-File', "src/ExplorerDirs.NativeProbe/build-$adapter.ps1")
    }
    foreach ($project in @('App', 'SettingsSmoke', 'HostProbe', 'NativeProbe')) {
        Invoke-BackgroundCheck ('build-' + $project) 'dotnet' @('build', "src/ExplorerDirs.$project/ExplorerDirs.$project.csproj", '-c', 'Release', '--no-restore')
    }
    # Isolated migration ABI + pure lifetime checks; no browser, registration or fixture filesystem access.
    Invoke-BackgroundCheck 'build-size-sort-context-experiment' 'pwsh' @('-NoProfile', '-File',
        'src/ExplorerDirs.NativeProbe/build-product-shell-folder-adapter-diagnostics.ps1', '-SizeSortPolicyExperiment')
    Invoke-BackgroundCheck 'managed-sort-context-lifecycle-smoke' 'dotnet' @('run', '--project',
        'src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj', '-c', 'Release', '--no-build', '--',
        '--managed-sort-context-lifecycle-smoke',
        'src/ExplorerDirs.NativeProbe/bin/native-product-diagnostics-x64-sort-policy/ExplorerDirs.ShellNamespace.Diagnostics.dll',
        'src/ExplorerDirs.NativeProbe/bin/native-product-diagnostics-x64/ExplorerDirs.ShellNamespace.Diagnostics.dll',
        'src/ExplorerDirs.NativeProbe/bin/native-product-x64/ExplorerDirs.ShellNamespace.dll')
    $settings = 'src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj'
    # Only early-dispatched, inspected flags belong here. The default SettingsSmoke suite is intentionally excluded.
    foreach ($flag in @('favorites-catalog-smoke', 'navigation-chrome-smoke', 'focus-traversal-smoke',
        'mixed-size-sort-session-smoke', 'view-state-restore-smoke', 'shell-refresh-presentation-smoke', 'soft-refresh-routing-smoke', 'folder-size-location-policy-smoke',
        'folder-size-data-source-smoke', 'everything-size-protocol-smoke', 'space-analysis-smoke',
        'column-layout-process-roundtrip', 'qdr-location-roundtrip-only')) {
        Invoke-BackgroundCheck $flag 'dotnet' @('run', '--project', $settings, '-c', 'Release', '--no-build', '--', "--$flag")
    }
    Invoke-BackgroundCheck 'folder-size-native-bridge-smoke' 'dotnet' @('run', '--project', $settings, '-c', 'Release', '--no-build', '--',
        '--folder-size-native-bridge-smoke', 'src/ExplorerDirs.NativeProbe/bin/native-x64/ExplorerDirs.StaticShellFolderAdapter.dll')
    $hostProject = 'src/ExplorerDirs.HostProbe/ExplorerDirs.HostProbe.csproj'
    foreach ($flag in @('global-mixed-sort', 'favorites-live-manager', 'folder-navigation-callbacks', 'navigation-focus-requests', 'pane-tab-copy-order')) {
        Invoke-BackgroundCheck $flag 'dotnet' @('run', '--project', $hostProject, '-c', 'Release', '--no-build', '--', "--$flag")
    }
    # This probe registers a random temporary namespace and removes it; it creates no browser or view.
    Invoke-BackgroundCheck 'folder-navigation-provider-callbacks' 'dotnet' @('run', '--project', $hostProject, '-c', 'Release', '--no-build', '--',
        '--folder-navigation-provider-callbacks', 'src/ExplorerDirs.NativeProbe/bin/native-product-x64/ExplorerDirs.ShellNamespace.dll')
    $nativeProject = 'src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj'
    Invoke-BackgroundCheck 'projected-dispatch' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--projected-dispatch-smoke')
    Invoke-BackgroundCheck 'projected-change-ledger' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--projected-change-ledger-smoke')
    Invoke-BackgroundCheck 'projected-watcher-ledger' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--projected-watcher-ledger-smoke')
    Invoke-BackgroundCheck 'product-parsing-identity' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--product-parsing-identity', 'src/ExplorerDirs.NativeProbe/bin/native-product-x64/ExplorerDirs.ShellNamespace.dll')
    Invoke-BackgroundCheck 'product-item-comparison' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--product-item-comparison', 'src/ExplorerDirs.NativeProbe/bin/native-product-x64/ExplorerDirs.ShellNamespace.dll')
    Invoke-BackgroundCheck 'product-drop-attributes' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--product-drop-attributes', 'src/ExplorerDirs.NativeProbe/bin/native-product-x64/ExplorerDirs.ShellNamespace.dll')
    Invoke-BackgroundCheck 'product-notification-capture-smoke' 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--',
        '--product-notification-capture-smoke', 'src/ExplorerDirs.NativeProbe/bin/native-product-diagnostics-x64/ExplorerDirs.ShellNamespace.Diagnostics.dll')
    foreach ($flag in @('synthetic-shell-folder-tree-smoke', 'synthetic-async-on-demand-recursive-size-column-smoke')) {
        Invoke-BackgroundCheck $flag 'dotnet' @('run', '--project', $nativeProject, '-c', 'Release', '--no-build', '--', "--$flag")
    }
    Invoke-BackgroundCheck 'publish-isolation-smoke' 'pwsh' @('-NoProfile', '-File', 'scripts/publish-smoke.ps1')
    $passed = $true
}
finally {
    [pscustomobject]@{
        SourceCommit = $sourceCommit
        SourceStatus = $sourceStatus
        Passed = $passed
        Scope = 'Background contracts only; no visible file view, no external Everything query, no real publication. UI acceptance remains pending.'
        Checks = $results.ToArray()
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
    Pop-Location
    Write-Output ('Background report: ' + (Join-Path $runDirectory 'summary.json'))
}
