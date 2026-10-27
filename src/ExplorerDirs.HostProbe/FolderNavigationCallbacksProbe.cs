using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;

// Real Shell folder PIDLs and actual host callbacks, without attaching the host
// to a window or creating ExplorerBrowser. Callback ordering is synthetic.
internal static class FolderNavigationCallbacksProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type HostType = typeof(ExplorerDirs.App.MainWindow).Assembly
        .GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
    private static readonly Type EventsType = HostType.GetNestedType("IExplorerBrowserEvents", BindingFlags.NonPublic)!;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        foreach (var path in new[] { "shell:MyComputerFolder", "shell:Downloads", "shell:Desktop",
                     "::{645FF040-5081-101B-9F08-00AA002F954E}", Path.GetTempPath() })
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
            try
            {
                CheckCompletion(path, pidl);
                CheckLatePending(path, pidl);
                CheckRepeatedRegistration(path, pidl);
                CheckNativeNavigation(pidl);
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        Ensure(GetForegroundWindow() == foreground, "foreground remained unchanged");
        Console.WriteLine("Folder navigation callbacks: PASS (five real Shell identities; unhosted callbacks, no native view or desktop input)");
        return 0;
    }

    private static void CheckCompletion(string path, nint pidl)
    {
        using var host = NewHost();
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        var version = (long)Field(host, "_navigationRequestVersion")!;
        Event(host, "OnNavigationPending", pidl);
        Ensure((long)Field(host, "_navigationRequestVersion")! == version,
            "an app-issued folder pending callback must not become a new user navigation");
        Event(host, "OnNavigationComplete", pidl);
        Ensure(((IDictionary)Field(host, "_issuedBrowserPaths")!).Count == 0,
            "completion retires registration even when the input is a Shell alias");
        Ensure(Field(host, "_retryPath") is null, "successful alias navigation clears its retry target");
        EnsureUnhosted(host);
    }

    private static void CheckLatePending(string path, nint pidl)
    {
        using var host = NewHost();
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        // This newer request is still in the asynchronous filesystem probe,
        // before Shell can notify Pending for it.
        const string newer = @"C:\ExplorerDirs-unhosted-probe-next";
        Call(host, "Navigate", newer);
        var gate = Field(host, "_navigationGate")!;
        var probe = BeginProbe(gate);
        var version = (long)Field(host, "_navigationRequestVersion")!;
        var failures = 0;
        HostType.GetEvent("NavigationFailed", Members)!.AddEventHandler(host,
            (EventHandler<string>)((_, _) => failures++));
        Event(host, "OnNavigationPending", pidl);
        Ensure((long)Field(host, "_navigationRequestVersion")! == version &&
            (bool)Call(gate, "IsCurrent", probe)!,
            "a delayed app-issued folder callback preserves the newer directory probe");
        Event(host, "OnNavigationFailed", pidl);
        Ensure(failures == 0 && (string?)Field(host, "_retryPath") == newer &&
            (bool)Call(gate, "IsCurrent", probe)!,
            "failure for the earlier folder does not replace the newer request's error or retry state");
        Ensure(((IDictionary)Field(host, "_issuedBrowserPaths")!).Count == 0,
            "failure retires virtual as well as filesystem folder registrations");
        EnsureUnhosted(host);
    }

    private static void CheckNativeNavigation(nint pidl)
    {
        using var host = NewHost();
        var gate = Field(host, "_navigationGate")!;
        var probe = BeginProbe(gate);
        var version = (long)Field(host, "_navigationRequestVersion")!;
        Event(host, "OnNavigationPending", pidl);
        Ensure((long)Field(host, "_navigationRequestVersion")! == version + 1 &&
            !(bool)Call(gate, "IsCurrent", probe)!,
            "an unregistered native folder navigation still supersedes the app's directory probe");
        Event(host, "OnNavigationFailed", pidl);
        EnsureUnhosted(host);
    }

    private static void CheckRepeatedRegistration(string path, nint pidl)
    {
        using var host = NewHost();
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        var issued = (IDictionary)Field(host, "_issuedBrowserPaths")!;
        Ensure(issued.Count == 1 && (int)issued.Values.Cast<object>().Single() == 2,
            "repeated canonical folders share a counted registration");
        Event(host, "OnNavigationFailed", pidl);
        var readiness = Field(host, "_viewReadiness")!;
        Ensure(issued.Count == 1 && (int)issued.Values.Cast<object>().Single() == 1 &&
            (bool)readiness.GetType().GetProperty("Pending", Members)!.GetValue(readiness)!,
            "the first repeated callback does not settle or discard the remaining visit");
        Event(host, "OnNavigationFailed", pidl);
        Ensure(issued.Count == 0 &&
            !(bool)readiness.GetType().GetProperty("Pending", Members)!.GetValue(readiness)! &&
            Field(host, "_retryPath") is not null,
            "all failed visits retire registration while leaving the retry target available");
        EnsureUnhosted(host);
    }

    private static object BeginProbe(object gate)
    {
        var task = (Task)Call(gate, "ProbeAsync", Path.GetTempPath(), null)!;
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static void RegisterFolder(object host, nint pidl)
    {
        // Registration preparation is inspected in NavigateWhenAvailableAsync;
        // this probe exercises callback bookkeeping with real resolved identities.
        var identity = (string?)Call(host, "NavigationIdentity", pidl);
        Ensure(identity is not null, "the Shell PIDL has a supported folder identity");
        Call(host, "TrackIssuedPath", identity!);
    }

    private static HwndHost NewHost()
    {
        var viewType = HostType.Assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var view = viewType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [4u, -1]);
        return (HwndHost)Activator.CreateInstance(HostType, Members, null, [Path.GetTempPath(), view!], null)!;
    }

    private static void EnsureUnhosted(HwndHost host) => Ensure(host.Handle == 0 && Field(host, "_browser") is null &&
        (nint)Field(host, "_viewWindow")! == 0, "the host created no HWND, ExplorerBrowser or file view");
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, Members)!.GetValue(instance);
    private static object? Call(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, Members)!.Invoke(instance, args);
    private static void Event(object host, string name, nint pidl) => EventsType.GetMethod(name)!.Invoke(host, [pidl]);
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint received);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
