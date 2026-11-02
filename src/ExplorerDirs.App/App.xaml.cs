using System.Windows;
using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CrashLog? _crashLog;
    private RunMarkerTracker? _runMarkerTracker;
    private FolderOpenRouter? _folderOpenRouter;
    private ProfileShellNamespaceOwner? _shellNamespaceOwner;
    private WindowSessionRegistry? _windowSessions;
    private ApplicationThemeController? _themeController;
    private readonly List<MainWindow> _openWindows = [];
    private MainWindow? _folderTargetWindow;
    private bool _restartRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupRequest = StartupDirectory.Parse(e.Args);
        var profilePath = new SessionStore().Path;
        // A launch retry is the same logical request, while a second launch gets a fresh id.
        var startupRequestId = Guid.NewGuid();
        var initialForward = FolderForwardResult.Unavailable;
        if (startupRequest.SelectPath is { } selectedPath && startupRequest.Directory is { } selectedDirectory)
        {
            try
            {
                initialForward = FolderOpenRouter.TryForwardSelectionAsync(profilePath, selectedDirectory, selectedPath,
                    TimeSpan.FromMilliseconds(150), startupRequestId).GetAwaiter().GetResult();
                if (initialForward == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception) { /* Open the parent and select the item in this process if routing is unavailable. */ }
        }
        else if (startupRequest.Directory is { } target)
        {
            try
            {
                initialForward = FolderOpenRouter.TryForwardAsync(profilePath, target,
                    TimeSpan.FromMilliseconds(150), startupRequestId).GetAwaiter().GetResult();
                if (initialForward == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception) { /* Open the directory in this process if routing is unavailable. */ }
        }
        else if (startupRequest.QdrFile is { } qdrPath)
        {
            try
            {
                initialForward = FolderOpenRouter.TryForwardQdrAsync(profilePath, qdrPath,
                    TimeSpan.FromMilliseconds(150), startupRequestId).GetAwaiter().GetResult();
                if (initialForward == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception) { /* Load the state in this process if routing is unavailable. */ }
        }
        else
        {
            try
            {
                initialForward = FolderOpenRouter.TryActivateAsync(profilePath,
                    TimeSpan.FromMilliseconds(150), startupRequestId).GetAwaiter().GetResult();
                if (initialForward == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception) { /* Continue only if this process can own the profile route. */ }
        }

        _windowSessions = new WindowSessionRegistry(profilePath);
        var sessionPaths = _windowSessions.Load();
        var profileDirectory = System.IO.Path.GetDirectoryName(profilePath)!;
        _themeController = new ApplicationThemeController(this, profileDirectory);
        var diagnosticDirectory = System.IO.Path.Combine(profileDirectory, "crash-logs");
        _crashLog = new CrashLog(diagnosticDirectory);
        _runMarkerTracker = RunMarkerTracker.TryStart(diagnosticDirectory);
        DispatcherUnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.DispatcherUnhandled, args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.AppDomainUnhandled, args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.UnobservedTask, args.Exception);
        var warnings = new List<string>();
        if (_runMarkerTracker?.PriorUncleanExit == true)
            warnings.Add("检测到上次运行未正常结束；已尝试恢复保存的会话，退出原因尚未确定。");
        if (startupRequest.Warning is { } argumentWarning) warnings.Add(argumentWarning);
        if (_windowSessions.LoadWarning is { } sessionWarning) warnings.Add(sessionWarning);
        if (_themeController.LoadWarning is { } themeWarning) warnings.Add(themeWarning);
        if (initialForward == FolderForwardResult.Uncertain)
            warnings.Add("未能确认现有窗口是否已接收目录；本次可能重复打开。");

        _folderOpenRouter = FolderOpenRouter.TryStart(profilePath, async path =>
        {
            return await Dispatcher.InvokeAsync(() =>
                (_folderTargetWindow ?? _openWindows.FirstOrDefault())?.OpenExternalDirectory(path) ?? false)
                .Task.ConfigureAwait(false);
        }, ActivateExistingWindowAsync, OpenQdrWorkspaceRequestAsync, async (directory, selected) =>
        {
            return await Dispatcher.InvokeAsync(() =>
                (_folderTargetWindow ?? _openWindows.FirstOrDefault())?.OpenExternalSelection(directory, selected) ?? false)
                .Task.ConfigureAwait(false);
        });
        if (_folderOpenRouter is null)
        {
            FolderForwardResult retry;
            try
            {
                retry = startupRequest.SelectPath is { } retrySelection && startupRequest.Directory is { } retryDirectory
                    ? FolderOpenRouter.TryForwardSelectionAsync(profilePath, retryDirectory, retrySelection,
                        TimeSpan.FromSeconds(2), startupRequestId)
                        .GetAwaiter().GetResult()
                    : startupRequest.Directory is { } retryTarget
                    ? FolderOpenRouter.TryForwardAsync(profilePath, retryTarget, TimeSpan.FromSeconds(2), startupRequestId)
                        .GetAwaiter().GetResult()
                    : startupRequest.QdrFile is { } retryQdr
                        ? FolderOpenRouter.TryForwardQdrAsync(profilePath, retryQdr, TimeSpan.FromSeconds(2), startupRequestId)
                            .GetAwaiter().GetResult()
                        : FolderOpenRouter.TryActivateAsync(profilePath, TimeSpan.FromSeconds(2), startupRequestId)
                            .GetAwaiter().GetResult();
                if (retry == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception)
            {
                retry = FolderForwardResult.Uncertain;
            }
            MessageBox.Show(retry == FolderForwardResult.Uncertain
                    ? "无法确认运行中的窗口是否已接收请求。为避免两个进程同时写入同一组会话，Explorer Dirs 未启动新实例。"
                    : "此 profile 的单实例路由锁不可用。为避免两个进程同时写入同一组会话，Explorer Dirs 未启动新实例。",
                "Explorer Dirs 正在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var shellNamespaceProfileStore = new ShellNamespaceProfileStore(Path.Combine(profileDirectory, "ShellNamespace.ini"));
        var shellNamespaceClassId = shellNamespaceProfileStore.LoadOrCreate();
        if (shellNamespaceProfileStore.LoadWarning is { } identityWarning)
            warnings.Add(identityWarning);
        if (shellNamespaceClassId is { } classId)
        {
            try
            {
                _shellNamespaceOwner = ProfileShellNamespaceOwner.Start(classId, profileDirectory,
                    Path.Combine(AppContext.BaseDirectory, "ExplorerDirs.ShellNamespace.dll"));
                _shellNamespaceOwner.SetFolderSizeSource(new FolderSizeOptionsStore(Path.Combine(profileDirectory, "FolderSize.ini")).Load().Source);
            }
            catch (Exception exception)
            {
                warnings.Add($"Shell 递归大小列组件未能启动：{exception.Message}");
            }
        }

        try
        {
            for (var index = 0; index < sessionPaths.Count; index++)
            {
                var window = CreateWindow(sessionPaths[index],
                    index == 0 ? startupRequest.Directory : null,
                    index == 0 && warnings.Count > 0 ? string.Join(" ", warnings) : null,
                    index == 0 ? startupRequest.SelectPath : null);
                if (index > 0 && !window.HasSavedWindowPlacement)
                {
                    var previous = _openWindows[^2];
                    window.CascadeFrom(previous);
                }
                if (index == 0) MainWindow = window;
                window.Show();
            }
            if (startupRequest.QdrFile is { } startupQdr && initialForward != FolderForwardResult.Accepted)
            {
                try
                {
                    var imported = Task.Run(() => QdrImporter.Load(startupQdr)).GetAwaiter().GetResult();
                    CreateQdrWorkspaceWindow(imported, showErrors: true);
                }
                catch (Exception ex)
                {
                    var message = $"无法从状态文件启动工作区：{ex.Message}";
                    if (_openWindows.FirstOrDefault() is { } main) main.ReportSessionIssue(message);
                    MessageBox.Show(MainWindow, message, "无法打开工作区状态",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception)
        {
            ReleaseShellNamespaceOwner();
            _folderOpenRouter?.Dispose();
            throw;
        }
    }

    internal void SetFolderSizeSource(FolderSizeSource source) => _shellNamespaceOwner?.SetFolderSizeSource(source);

    private MainWindow CreateWindow(string sessionPath, string? startupDirectory, string? warning,
        string? startupSelectPath = null)
    {
        var window = new MainWindow(_themeController!, startupDirectory, warning, sessionPath,
            System.IO.Path.GetDirectoryName(new SessionStore().Path), OpenSiblingWindow, RestartApplication,
            startupSelectPath);
        window.Closed += (_, _) => OnWindowClosed(window);
        window.Activated += (_, _) => _folderTargetWindow = window;
        _openWindows.Add(window);
        return window;
    }

    private async Task<bool> ActivateExistingWindowAsync()
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            var window = _folderTargetWindow ?? _openWindows.FirstOrDefault();
            if (window is null) return false;
            window.RestoreFromMinimized();
            window.Activate();
            return true;
        }).Task.ConfigureAwait(false);
    }

    private async Task<bool> OpenQdrWorkspaceRequestAsync(string path)
    {
        QdrImportResult imported;
        try { imported = await Task.Run(() => QdrImporter.Load(path)).ConfigureAwait(false); }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessageBox.Show(
                _folderTargetWindow, $"无法打开工作区状态：{ex.Message}", "无法打开工作区状态",
                MessageBoxButton.OK, MessageBoxImage.Warning)).Task.ConfigureAwait(false);
            return false;
        }

        return await Dispatcher.InvokeAsync(() => CreateQdrWorkspaceWindow(imported, showErrors: true))
            .Task.ConfigureAwait(false);
    }

    private bool CreateQdrWorkspaceWindow(QdrImportResult imported, bool showErrors)
    {
        if (_windowSessions is null) return false;
        string? sessionPath = null;
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var state = QdrLaunchSession.Create(imported, home);
            sessionPath = _windowSessions.Create(state, imported.Pins ?? []);
            var profileDirectory = Path.GetDirectoryName(new SessionStore().Path)!;
            if (imported.Proportions is { Count: > 0 } proportions)
            {
                var store = new LayoutProportionsStore(Path.Combine(profileDirectory, "LayoutProportions.ini"));
                var merged = store.Load();
                foreach (var pair in proportions) merged[pair.Key] = pair.Value;
                store.Save(merged);
            }
            if (imported.NavigationTreeWidths is { } treeWidths)
            {
                var store = new NavigationTreeWidthsStore(Path.Combine(profileDirectory, "NavigationTreeWidths.ini"));
                store.Load();
                store.Save(treeWidths);
            }

            var window = CreateWindow(sessionPath, null, null);
            var source = _folderTargetWindow ?? _openWindows.FirstOrDefault(candidate => !ReferenceEquals(candidate, window));
            if (source is not null)
            {
                window.CascadeFrom(source);
            }
            window.Show();
            window.Activate();
            return true;
        }
        catch (Exception ex)
        {
            if (sessionPath is not null)
            {
                try { _windowSessions.Remove(sessionPath); }
                catch (Exception) { }
            }
            if (showErrors)
                MessageBox.Show(_folderTargetWindow, $"无法创建工作区窗口：{ex.Message}", "无法打开工作区状态",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private MainWindow? OpenSiblingWindow(MainWindow source)
    {
        if (_windowSessions is null) return null;
        string? sessionPath = null;
        try
        {
            sessionPath = _windowSessions.Create(source.SnapshotSession(), source.SnapshotPinnedTabs());
            var window = CreateWindow(sessionPath, null, null);
            window.CascadeFrom(source);
            window.Show();
            window.Activate();
            return window;
        }
        catch (Exception ex)
        {
            if (sessionPath is not null)
            {
                try { _windowSessions.Remove(sessionPath); }
                catch (Exception) { }
            }
            source.ReportSessionIssue($"新窗口创建失败：{ex.Message}");
            return null;
        }
    }

    private void OnWindowClosed(MainWindow window)
    {
        if (!_restartRequested && _windowSessions is not null && _openWindows.Count > 1)
        {
            try { _windowSessions.Remove(window.SessionPath); }
            catch (Exception ex)
            {
                var remainingWindow = _openWindows.FirstOrDefault(candidate => !ReferenceEquals(candidate, window));
                remainingWindow?.ReportSessionIssue($"窗口恢复清单未能更新：{ex.Message}");
            }
        }
        _openWindows.Remove(window);
        if (ReferenceEquals(_folderTargetWindow, window))
            _folderTargetWindow = _openWindows.LastOrDefault();
        if (ReferenceEquals(MainWindow, window) && _openWindows.Count > 0)
            MainWindow = _openWindows[0];
    }

    private void RestartApplication()
    {
        if (_restartRequested || _openWindows.Count == 0) return;
        foreach (var window in _openWindows.ToArray())
            if (!window.SaveSessionForRestart()) return;
        _restartRequested = true;
        foreach (var window in _openWindows.ToArray()) window.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseShellNamespaceOwner();
        _folderOpenRouter?.Dispose();
        _folderOpenRouter = null;
        _themeController?.Dispose();
        _runMarkerTracker?.Dispose();
        if (_restartRequested)
        {
            try
            {
                var executable = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                };
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
                Process.Start(start);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"会话已保存，但重新启动失败：{ex.Message}", "Explorer Dirs",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        base.OnExit(e);
    }

    private void ReleaseShellNamespaceOwner()
    {
        try { _shellNamespaceOwner?.Dispose(); }
        catch (Exception exception) { Debug.WriteLine($"Shell namespace registration cleanup failed: {exception}"); }
        finally { _shellNamespaceOwner = null; }
    }
}
