using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProfileOwnerSessionRecoveryProbe
{
    internal static int RunParent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProfileOwnerSession-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folder = Path.Combine(root, "fixture");
        var sessionPath = Path.Combine(profile, "ExplorerDirs.ini");
        object? owner = null;
        try
        {
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(Path.Combine(folder, "folder-small"));
            Directory.CreateDirectory(Path.Combine(folder, "folder-large"));
            File.WriteAllBytes(Path.Combine(folder, "folder-small", "payload.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(folder, "folder-large", "payload.bin"), new byte[29]);

            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var profileStoreType = appAssembly.GetType("ExplorerDirs.App.ShellNamespaceProfileStore", throwOnError: true)!;
            var profileStore = Activator.CreateInstance(profileStoreType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [Path.Combine(profile, "ShellNamespace.ini")], null)!;
            var classId = (Guid?)profileStoreType.GetMethod("LoadOrCreate")!.Invoke(profileStore, null)
                ?? throw new InvalidOperationException("The profile Shell namespace ID could not be created.");
            owner = StartOwner(appAssembly, classId, profile);

            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [folder, classId])!;
            var viewState = ProfileOwnerMultiRootShellHostProbe.CreateViewStateForProbe(appAssembly);
            SaveSession(appAssembly, sessionPath, identity, viewState);
            if (!Directory.Exists(folder) || !File.Exists(sessionPath))
                throw new InvalidOperationException("The profile session fixture was not persisted.");

            (owner as IDisposable)?.Dispose();
            owner = null;
            var child = StartChild(sessionPath, profile);
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Fresh-process session recovery failed (exit {child.ExitCode}). stdout=[{child.StdOut}] stderr=[{child.StdErr}]");

            Console.WriteLine($"profile-owner-session-recovery: fresh-process=True; stable-profile-id=True; session-tab-path=True; view-columns=3; recursive-values=[5|29]; child-exit={child.ExitCode}; child-output=[{child.StdOut.Trim()}]");
            return 0;
        }
        finally
        {
            (owner as IDisposable)?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static int RunChild(string sessionPath, string profile)
    {
        var appAssembly = Assembly.Load("ExplorerDirs.App");
        var profileStoreType = appAssembly.GetType("ExplorerDirs.App.ShellNamespaceProfileStore", throwOnError: true)!;
        var profileStore = Activator.CreateInstance(profileStoreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [Path.Combine(profile, "ShellNamespace.ini")], null)!;
        var classId = (Guid?)profileStoreType.GetMethod("LoadOrCreate")!.Invoke(profileStore, null)
            ?? throw new InvalidOperationException("The restarted process could not read the stable profile ID.");
        var owner = StartOwner(appAssembly, classId, profile);
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        HwndSource? source = null;
        try
        {
            var sessionStoreType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
            var sessionStore = Activator.CreateInstance(sessionStoreType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [sessionPath, Path.Combine(profile, "legacy-session.json")], null)!;
            var session = sessionStoreType.GetMethod("Load")!.Invoke(sessionStore, null)
                ?? throw new InvalidOperationException("The fresh process could not load its persisted session.");
            var firstPane = ((System.Collections.IEnumerable)session.GetType().GetProperty("Panes")!.GetValue(session)!)
                .Cast<object>().First();
            var tab = ((System.Collections.IEnumerable)firstPane).Cast<object>().First();
            var identity = (string)tab.GetType().GetProperty("Path")!.GetValue(tab)!;
            var viewState = tab.GetType().GetProperty("View")!.GetValue(tab)!;
            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var path = codec.GetMethod("ToQdrTextPath", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [identity, (Guid?)classId]) as string;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new InvalidOperationException("The persisted provider tab no longer resolves to its physical folder.");

            var viewColumns = ((System.Collections.IEnumerable)viewState.GetType().GetProperty("DisplayColumns")!.GetValue(viewState)!)
                .Cast<object>().ToArray();
            if (viewColumns.Length != 3 || !viewColumns.Any(column =>
                    column.GetType().GetProperty("Key")!.GetValue(column)!.ToString() ==
                    "{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1"))
                throw new InvalidOperationException("The persisted session did not restore the recursive size column.");

            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            var host = (HwndHost)Activator.CreateInstance(hostType, identity, viewState)!;
            var grid = new Grid { Width = 700, Height = 360 };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs fresh-process session recovery")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 700, Height = 360
            });
            source.RootVisual = grid;
            grid.Measure(new Size(700, 360));
            grid.Arrange(new Rect(0, 0, 700, 360));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The fresh-process recovery host became visible.");

            var navigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() => Equals(navigated.GetValue(host), true) && (nint)viewWindow.GetValue(host)! != 0,
                TimeSpan.FromSeconds(15), "The fresh process could not navigate to the saved provider tab.");
            var valuesReader = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] values = [];
            WaitUntil(() =>
            {
                values = (string[])valuesReader.Invoke(null, [hostType, host, 2])!;
                return values.Length == 2 && values.Select(value => ulong.TryParse(value, out var bytes)
                        ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual([5UL, 29UL]);
            }, TimeSpan.FromSeconds(20), "The fresh process did not restore the persisted recursive values.");
            Console.WriteLine($"fresh-process-provider-session: profile-id={classId:D}; provider-path={identity}; physical-path={path}; columns={viewColumns.Length}; recursive-values=[{string.Join('|', values.OrderBy(ulong.Parse))}]; parent-visible=False");
            return 0;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            (owner as IDisposable)?.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            var registrationRemains = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(classKeyPath) is not null ||
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null;
            if (registrationRemains)
                throw new InvalidOperationException("The fresh process left volatile Shell registration behind.");
        }
    }

    private static object StartOwner(Assembly appAssembly, Guid classId, string profile)
    {
        var ownerType = appAssembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", throwOnError: true)!;
        return ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [classId, profile, ProfileOwnerMultiRootShellHostProbe.FindProductAdapterForProbe()])!;
    }

    private static void SaveSession(Assembly appAssembly, string path, string identity, object viewState)
    {
        var tabType = appAssembly.GetType("ExplorerDirs.App.TabSettings", throwOnError: true)!;
        var tab = Activator.CreateInstance(tabType, [identity, viewState, null, false, false])!;
        var paneListType = typeof(List<>).MakeGenericType(tabType);
        var pane = Activator.CreateInstance(paneListType)!;
        paneListType.GetMethod("Add")!.Invoke(pane, [tab]);
        var panesType = typeof(List<>).MakeGenericType(paneListType);
        var panes = Activator.CreateInstance(panesType)!;
        panesType.GetMethod("Add")!.Invoke(panes, [pane]);
        var sessionType = appAssembly.GetType("ExplorerDirs.App.SessionState", throwOnError: true)!;
        var state = sessionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 11)
            .Invoke([1, 4u, panes, 0, new[] { 0 }, -1, null, null, null, null, null]);
        var storeType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
        var store = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [path, Path.Combine(Path.GetDirectoryName(path)!, "legacy-session.json")], null)!;
        storeType.GetMethod("Save")!.Invoke(store, [state]);
    }

    private static (int ExitCode, string StdOut, string StdErr) StartChild(string sessionPath, string profile)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current runtime executable.");
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--profile-owner-session-recovery-child");
        start.ArgumentList.Add(sessionPath);
        start.ArgumentList.Add(profile);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the fresh-process HostProbe.");
        if (!process.WaitForExit(45000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("The fresh-process HostProbe exceeded its 45 second deadline.");
        }
        return (process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message) =>
        typeof(ShellProviderLocationProbe).GetMethod("WaitUntil", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [condition, timeout, message]);

    private static bool IsWindowVisible(nint window) => window != 0 && IsWindowVisibleNative(window);

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsWindowVisibleNative(nint window);
}
