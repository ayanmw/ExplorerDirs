using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerDirs.App;

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
                CheckSupersededCompletion(path, pidl);
                CheckRepeatedRegistration(path, pidl);
                CheckNativeNavigation(pidl);
                CheckUnacceptedCallbacks(path, pidl);
                if (path == "shell:MyComputerFolder") CheckQueuedCompletion(path, pidl);
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        CheckSearchHandoff();
        Ensure(GetForegroundWindow() == foreground, "foreground remained unchanged");
        Console.WriteLine("Folder navigation callbacks: PASS (five folder identities and search handoff; actual queued unhosted callbacks, no native view or desktop input)");
        return 0;
    }

    internal static int RunProvider(string adapterPath)
    {
        var foreground = GetForegroundWindow();
        var root = Path.Combine(AppContext.BaseDirectory, "provider-callbacks-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "中文子目录");
        var grandchild = Path.Combine(child, "第二层");
        var classId = Guid.NewGuid();
        var classKey = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKey = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        IDisposable? owner = null;
        Directory.CreateDirectory(grandchild);
        foreach (var directory in new[] { root, child, grandchild })
            File.WriteAllText(Path.Combine(directory, "selected.txt"), directory);
        try
        {
            var ownerType = HostType.Assembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", true)!;
            owner = (IDisposable)ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [classId, root, Path.GetFullPath(adapterPath)])!;
            var codec = HostType.Assembly.GetType("ExplorerDirs.App.QdrShellPathCodec", true)!;
            var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [root, classId])!;
            foreach (var (native, physical) in new[] { (identity, root), (identity + @"\中文子目录", child),
                (identity + @"\中文子目录\第二层", grandchild) })
            {
                var parseResult = SHParseDisplayName(native, 0, out var pidl, 0, out _);
                if (parseResult < 0)
                {
                    if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
                    throw new InvalidOperationException($"Provider parsing failed for {physical}: 0x{parseResult:X8}");
                }
                try
                {
                    using var host = NewHost();
                    Call(host, "ConfigureFolderSizeLocations", classId, true);
                    var logicalIdentity = (string?)Call(host, "NavigationIdentity", pidl);
                    Ensure(logicalIdentity == physical,
                        $"provider callbacks retain the physical logical identity: expected={physical}; actual={logicalIdentity}; native={Call(host, "NativeNavigationIdentity", pidl)}");
                    var nativeIdentity = (string?)Call(host, "NativeNavigationIdentity", pidl);
                    Ensure(nativeIdentity is not null && nativeIdentity.StartsWith("::{" + classId.ToString(), StringComparison.OrdinalIgnoreCase),
                        "provider callback matching retains the namespace identity");
                    Call(host, "TrackIssuedPath", physical);
                    Event(host, "OnNavigationPending", pidl);
                    Event(host, "OnNavigationComplete", pidl);
                    Ensure((string?)HostType.GetProperty("CurrentPath", Members)!.GetValue(host) == physical,
                        "published provider location is physical");
                    Ensure((string?)Field(host, "_nativeDirectoryIdentity") == nativeIdentity,
                        "published location retains the native parent for selection binding");
                    Ensure(((IDictionary)Field(host, "_issuedBrowserPaths")!).Count == 0,
                        "provider completion retires its physical request registration");
                    CheckProviderSelection(nativeIdentity!, physical, classId);
                    EnsureUnhosted(host);
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            Ensure(!TryBindDirectChild(identity, identity + @"\中文子目录\selected.txt"),
                "provider selection rejects a non-direct descendant even when it exists");
            Ensure(!TryBindDirectChild(identity + @"\中文子目录", identity + @"\selected.txt"),
                "provider selection rejects a same-name file from another parent");
        }
        finally
        {
            owner?.Dispose();
            using var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(classKey);
            using var namespaceRegistration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(namespaceKey);
            Ensure(registration is null && namespaceRegistration is null, "temporary provider registration was removed");
            Directory.Delete(root, true);
        }
        Ensure(GetForegroundWindow() == foreground, "provider callbacks preserved foreground");
        Console.WriteLine("Provider callbacks: PASS (real product PIDLs, Unicode nested folders, logical/native identities, direct-child selection and wrong-parent rejection, registration cleanup; no HWND/ExplorerBrowser/file view)");
        return 0;
    }

    private static void CheckProviderSelection(string nativeParent, string physicalParent, Guid classId)
    {
        var file = Path.Combine(physicalParent, "selected.txt");
        var policy = HostType.Assembly.GetType("ExplorerDirs.App.FolderSizeLocationPolicy", true)!;
        object?[] arguments = [nativeParent, physicalParent, file, classId, null];
        Ensure((bool)policy.GetMethod("TryGetProviderChildIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!, "physical selected file maps into its current provider parent");
        var nativeFile = (string)arguments[4]!;
        Ensure(TryBindDirectChild(nativeParent, nativeFile), "mapped selection binds as one native child");
        Marshal.ThrowExceptionForHR(SHParseDisplayName(nativeFile, 0, out var pidl, 0, out _));
        try
        {
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0x80028000, out var name));
            try
            {
                var parsingName = Marshal.PtrToStringUni(name)!;
                Ensure((string?)policy.GetMethod("LogicalPath", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [parsingName, classId]) == file,
                    "selected file absolute parsing name round-trips to its physical file");
            }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static bool TryBindDirectChild(string parent, string file)
    {
        var type = HostType.Assembly.GetType("ExplorerDirs.App.ShellSelectionPidl", true)!;
        object?[] arguments = [parent, file, nint.Zero];
        var success = (bool)type.GetMethod("TryGetDirectChild", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var child = (nint)arguments[2]!;
        Ensure(success == (child != 0), "child binding success agrees with PIDL ownership");
        if (child != 0) Marshal.FreeCoTaskMem(child);
        return success;
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

    private static void CheckUnacceptedCallbacks(string path, nint pidl)
    {
        using var host = NewHost();
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        var readiness = Field(host, "_viewReadiness")!;
        var generationProperty = readiness.GetType().GetProperty("Generation", Members)!;
        var before = (long)generationProperty.GetValue(readiness)!;
        // A cancelled Pending never registered a visit. Its terminal callbacks must not consume
        // a later same-location app registration before that replacement's Pending arrives.
        Event(host, "OnNavigationFailed", pidl);
        Event(host, "OnNavigationComplete", pidl);
        Ensure(Count((IDictionary)Field(host, "_issuedBrowserPaths")!) == 1 &&
            (long)generationProperty.GetValue(readiness)! == before,
            "unaccepted callbacks leave replacement registration and read generation intact");
        Event(host, "OnNavigationPending", pidl);
        Event(host, "OnNavigationComplete", pidl);
        Ensure(((IDictionary)Field(host, "_issuedBrowserPaths")!).Count == 0,
            "accepted completion still retires its own registration");
        var settled = (long)generationProperty.GetValue(readiness)!;
        Event(host, "OnNavigationFailed", pidl);
        Event(host, "OnNavigationComplete", pidl);
        Ensure((long)generationProperty.GetValue(readiness)! == settled,
            "duplicate terminal callbacks do not invalidate a settled read generation");
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
            (EventHandler<ExplorerDirs.App.ShellNavigationNotice>)((_, _) => failures++));
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

    private static void CheckSupersededCompletion(string path, nint pidl)
    {
        using var host = NewHost();
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        // The second same-path submission has not reached native Pending yet.
        Call(host, "Navigate", path);
        Event(host, "OnNavigationComplete", pidl);
        Ensure(Field(host, "_retryPath") is not null,
            "an older same-path completion must not retire the newer unissued request");
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        Event(host, "OnNavigationComplete", pidl);
        Ensure(Field(host, "_retryPath") is null, "the newer request clears its own retry target on completion");
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
        Ensure(issued.Count == 1 && Count(issued) == 2,
            "repeated canonical folders share a counted registration");
        Event(host, "OnNavigationFailed", pidl);
        var readiness = Field(host, "_viewReadiness")!;
        Ensure(issued.Count == 1 && Count(issued) == 1 &&
            (bool)readiness.GetType().GetProperty("Pending", Members)!.GetValue(readiness)!,
            "the first repeated callback does not settle or discard the remaining visit");
        Event(host, "OnNavigationFailed", pidl);
        Ensure(issued.Count == 0 &&
            !(bool)readiness.GetType().GetProperty("Pending", Members)!.GetValue(readiness)! &&
            Field(host, "_retryPath") is not null,
            "all failed visits retire registration while leaving the retry target available");
        EnsureUnhosted(host);
    }

    private static void CheckQueuedCompletion(string path, nint pidl)
    {
        using var host = NewHost();
        var notices = new List<ShellNavigationNotice>();
        HostType.GetEvent("Navigated", Members)!.AddEventHandler(host,
            (EventHandler<ShellNavigationNotice>)((_, notice) => notices.Add(notice)));
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        var earlier = (long)Field(host, "_navigationRequestVersion")!;
        Event(host, "OnNavigationComplete", pidl); // Notification is queued, not delivered yet.
        Call(host, "Navigate", path);
        // The unshown host deliberately cannot take focus; seed only its
        // pending intent, then let the real queued callback attempt to retire it.
        HostType.GetField("_focusWhenReady", Members)!.SetValue(host, true);
        Drain(host.Dispatcher);
        Ensure(notices.Count == 1 && notices[0].RequestVersion == earlier &&
            !(bool)Call(host, "CanCompleteNavigationRequest", notices[0].RequestVersion)!,
            "queued actual location publication retains its original request number");
        Ensure((bool)Field(host, "_focusWhenReady")!, "old queued completion does not consume newer focus intent");

        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        Event(host, "OnNavigationComplete", pidl);
        Drain(host.Dispatcher);
        Ensure(notices.Count == 2 && (bool)Call(host, "CanCompleteNavigationRequest", notices[1].RequestVersion)! &&
            !(bool)Field(host, "_focusWhenReady")!, "the matching queued completion consumes its own focus request");

        // A second same-target request can fail before reaching native Pending.
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        Event(host, "OnNavigationPending", pidl);
        Call(host, "Navigate", path);
        RegisterFolder(host, pidl);
        Call(host, "CompleteIssuedPath", Call(host, "NavigationIdentity", pidl));
        var failed = (long)Field(host, "_navigationRequestVersion")!;
        Call(host, "ReportNavigationFailure", "probe browse rejection", failed);
        Event(host, "OnNavigationComplete", pidl);
        Drain(host.Dispatcher);
        Ensure(Field(host, "_retryPath") is not null && !(bool)Call(host, "CanCompleteNavigationRequest", failed)!,
            "a late same-target success preserves an already reported failure and retry target");
        EnsureUnhosted(host);
    }

    private static void Drain(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void CheckSearchHandoff()
    {
        var type = HostType.Assembly.GetType("ExplorerDirs.App.ShellSearchLocation", true)!;
        var uri = (string)type.GetMethod("BuildUri")!.Invoke(null, [Path.GetTempPath(), "ExplorerDirs request handoff probe"])!;
        var pidl = (nint)type.GetMethod("ParsePidl")!.Invoke(null, [uri])!;
        try
        {
            foreach (var fail in new[] { false, true })
            {
                using var host = NewHost();
                var history = Field(host, "_searchHistory")!;
                Call(history, "Remember", pidl, uri);
                Call(host, "TrackIssuedSearch", uri);
                var earlier = (long)Field(host, "_navigationRequestVersion")!;
                var notices = new List<ShellNavigationNotice>();
                var failures = 0;
                HostType.GetEvent("SearchNavigated", Members)!.AddEventHandler(host,
                    (EventHandler<ShellNavigationNotice>)((_, notice) => notices.Add(notice)));
                HostType.GetEvent("NavigationFailed", Members)!.AddEventHandler(host,
                    (EventHandler<ShellNavigationNotice>)((_, _) => failures++));
                Call(host, "Navigate", Path.GetTempPath());
                Event(host, "OnNavigationPending", pidl);
                Event(host, fail ? "OnNavigationFailed" : "OnNavigationComplete", pidl);
                Drain(host.Dispatcher);
                Ensure(failures == 0 && Field(host, "_retryPath") is not null &&
                    ((IDictionary)Field(host, "_issuedSearches")!).Count == 0,
                    "an older search retires without clearing the newer directory retry or publishing an error");
                Ensure(fail ? notices.Count == 0 : notices.Count == 1 && notices[0].RequestVersion == earlier &&
                    !(bool)Call(host, "CanCompleteNavigationRequest", earlier)!,
                    "search publication carries the search's original input identity");
                EnsureUnhosted(host);
            }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static object BeginProbe(object gate)
    {
        var task = (Task)Call(gate, "ProbeAsync", Path.GetTempPath(), null)!;
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static int Count(IDictionary issued)
    {
        var request = issued.Values.Cast<object>().Single();
        return (int)request.GetType().GetProperty("Count", Members)!.GetValue(request)!;
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
    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint pidl, uint kind, out nint name);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
