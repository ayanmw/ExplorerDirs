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

        var profileDirectory = System.IO.Path.GetDirectoryName(profilePath)!;
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
        if (initialForward == FolderForwardResult.Uncertain)
            warnings.Add("未能确认现有窗口是否已接收目录；本次可能重复打开。");

        var windowReady = new TaskCompletionSource<MainWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        _folderOpenRouter = FolderOpenRouter.TryStart(profilePath, async path =>
        {
            var receivingWindow = await windowReady.Task.ConfigureAwait(false);
            return await Dispatcher.InvokeAsync(() => receivingWindow.OpenExternalDirectory(path))
                .Task.ConfigureAwait(false);
        });
        if (_folderOpenRouter is null && initialForward == FolderForwardResult.Uncertain)
            warnings.Add("已改为独立窗口，会话保存可能冲突。");
        if (_folderOpenRouter is null && initialForward != FolderForwardResult.Uncertain &&
            requestedDirectory.Directory is { } retryTarget)
        {
            try
            {
                var retry = FolderOpenRouter.TryForwardAsync(profilePath, retryTarget,
                    TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                if (retry == FolderForwardResult.Accepted)
                {
                    Shutdown();
                    return;
                }
                warnings.Add(retry == FolderForwardResult.Uncertain
                    ? "未能确认现有窗口是否已接收目录；已改为独立窗口，会话保存可能冲突。"
                    : "目录转发不可用；已改为独立窗口，会话保存可能冲突。");
            }
            catch (Exception)
            {
                warnings.Add("目录转发失败；已改为独立窗口，会话保存可能冲突。");
            }
        }

        try
        {
            var window = new MainWindow(requestedDirectory.Directory,
                warnings.Count == 0 ? null : string.Join(" ", warnings));
            MainWindow = window;
            windowReady.TrySetResult(window);
            window.Show();
        }
        catch (Exception)
        {
            windowReady.TrySetCanceled();
            _folderOpenRouter?.Dispose();
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _folderOpenRouter?.Dispose();
        _runMarkerTracker?.Dispose();
        base.OnExit(e);
    }
}
