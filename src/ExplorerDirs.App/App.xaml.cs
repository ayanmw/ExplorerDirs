using System.Windows;

namespace ExplorerDirs.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CrashLog? _crashLog;
    private RunMarkerTracker? _runMarkerTracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        var profileDirectory = System.IO.Path.GetDirectoryName(new SessionStore().Path)!;
        var diagnosticDirectory = System.IO.Path.Combine(profileDirectory, "crash-logs");
        _crashLog = new CrashLog(diagnosticDirectory);
        _runMarkerTracker = RunMarkerTracker.TryStart(diagnosticDirectory);
        DispatcherUnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.DispatcherUnhandled, args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.AppDomainUnhandled, args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.UnobservedTask, args.Exception);
        base.OnStartup(e);
        var requestedDirectory = StartupDirectory.Parse(e.Args);
        var warnings = new List<string>();
        if (_runMarkerTracker?.PriorUncleanExit == true)
            warnings.Add("检测到上次运行未正常结束；已尝试恢复保存的会话，退出原因尚未确定。");
        if (requestedDirectory.Warning is { } argumentWarning) warnings.Add(argumentWarning);
        MainWindow = new MainWindow(requestedDirectory.Directory,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runMarkerTracker?.Dispose();
        base.OnExit(e);
    }
}
