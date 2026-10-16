using System.Windows;

namespace ExplorerDirs.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CrashLog? _crashLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        var profileDirectory = System.IO.Path.GetDirectoryName(new SessionStore().Path)!;
        _crashLog = new CrashLog(System.IO.Path.Combine(profileDirectory, "crash-logs"));
        DispatcherUnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.DispatcherUnhandled, args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.AppDomainUnhandled, args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            _crashLog.TryWrite(CrashEventKind.UnobservedTask, args.Exception);
        base.OnStartup(e);
        var requestedDirectory = StartupDirectory.Parse(e.Args);
        MainWindow = new MainWindow(requestedDirectory.Directory, requestedDirectory.Warning);
        MainWindow.Show();
    }
}
