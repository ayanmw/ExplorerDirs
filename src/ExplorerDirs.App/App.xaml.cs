using System.Windows;

namespace ExplorerDirs.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CrashLog? _crashLog;
    private RunMarkerTracker? _runMarkerTracker;
    private FolderOpenRouter? _folderOpenRouter;
    private WindowSessionRegistry? _windowSessions;
    private ApplicationThemeController? _themeController;
    private readonly List<MainWindow> _openWindows = [];
    private MainWindow? _folderTargetWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var requestedDirectory = StartupDirectory.Parse(e.Args);
        var profilePath = new SessionStore().Path;
        var initialForward = FolderForwardResult.Unavailable;
        if (requestedDirectory.Directory is { } target)
        {
            try
            {
                initialForward = FolderOpenRouter.TryForwardAsync(profilePath, target,
                    TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult();
                if (initialForward == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception) { /* Open the directory in this process if routing is unavailable. */ }
        }
        else
        {
            try
            {
                initialForward = FolderOpenRouter.TryActivateAsync(profilePath,
                    TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult();
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
        if (requestedDirectory.Warning is { } argumentWarning) warnings.Add(argumentWarning);
        if (_windowSessions.LoadWarning is { } sessionWarning) warnings.Add(sessionWarning);
        if (_themeController.LoadWarning is { } themeWarning) warnings.Add(themeWarning);
        if (initialForward == FolderForwardResult.Uncertain)
            warnings.Add("未能确认现有窗口是否已接收目录；本次可能重复打开。");

        _folderOpenRouter = FolderOpenRouter.TryStart(profilePath, async path =>
        {
            return await Dispatcher.InvokeAsync(() =>
                (_folderTargetWindow ?? _openWindows.FirstOrDefault())?.OpenExternalDirectory(path) ?? false)
                .Task.ConfigureAwait(false);
        }, ActivateExistingWindowAsync);
        if (_folderOpenRouter is null)
        {
            FolderForwardResult retry;
            try
            {
                retry = requestedDirectory.Directory is { } retryTarget
                    ? FolderOpenRouter.TryForwardAsync(profilePath, retryTarget, TimeSpan.FromSeconds(2))
                        .GetAwaiter().GetResult()
                    : FolderOpenRouter.TryActivateAsync(profilePath, TimeSpan.FromSeconds(2))
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

        try
        {
            for (var index = 0; index < sessionPaths.Count; index++)
            {
                var window = CreateWindow(sessionPaths[index],
                    index == 0 ? requestedDirectory.Directory : null,
                    index == 0 && warnings.Count > 0 ? string.Join(" ", warnings) : null);
                if (index > 0)
                {
                    var previous = _openWindows[^2];
                    window.Left = Math.Min(previous.Left + 32, SystemParameters.WorkArea.Right - window.Width);
                    window.Top = Math.Min(previous.Top + 32, SystemParameters.WorkArea.Bottom - window.Height);
                }
                if (index == 0) MainWindow = window;
                window.Show();
            }
        }
        catch (Exception)
        {
            _folderOpenRouter?.Dispose();
            throw;
        }
    }

    private MainWindow CreateWindow(string sessionPath, string? startupDirectory, string? warning)
    {
        var window = new MainWindow(_themeController!, startupDirectory, warning, sessionPath,
            System.IO.Path.GetDirectoryName(new SessionStore().Path), OpenSiblingWindow);
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
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            return true;
        }).Task.ConfigureAwait(false);
    }

    private void OpenSiblingWindow(MainWindow source)
    {
        if (_windowSessions is null) return;
        string? sessionPath = null;
        try
        {
            sessionPath = _windowSessions.Create(source.SnapshotSession(), source.SnapshotPinnedTabs());
            var window = CreateWindow(sessionPath, null, null);
            window.Left = Math.Min(source.Left + 32, SystemParameters.WorkArea.Right - window.Width);
            window.Top = Math.Min(source.Top + 32, SystemParameters.WorkArea.Bottom - window.Height);
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            if (sessionPath is not null)
            {
                try { _windowSessions.Remove(sessionPath); }
                catch (Exception) { }
            }
            source.ReportSessionIssue($"新窗口创建失败：{ex.Message}");
        }
    }

    private void OnWindowClosed(MainWindow window)
    {
        if (_windowSessions is not null && _openWindows.Count > 1)
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

    protected override void OnExit(ExitEventArgs e)
    {
        _folderOpenRouter?.Dispose();
        _themeController?.Dispose();
        _runMarkerTracker?.Dispose();
        base.OnExit(e);
    }
}
