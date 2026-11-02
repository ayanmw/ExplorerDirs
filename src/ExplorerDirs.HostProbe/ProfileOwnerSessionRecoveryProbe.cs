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
        var folders = new[] { Path.Combine(root, "fixture-one"), Path.Combine(root, "fixture-two") };
        var sessionPath = Path.Combine(profile, "ExplorerDirs.ini");
        object? owner = null;
        try
        {
            Directory.CreateDirectory(profile);
            CreateFixture(folders[0], 5, 29);
            CreateFixture(folders[1], 7, 31);

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
                .Invoke(null, [folders[0], classId])!;
            var secondIdentity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [folders[1], classId])!;
            var viewState = ProfileOwnerMultiRootShellHostProbe.CreateViewStateForProbe(appAssembly);
            SaveSession(appAssembly, sessionPath, identity, viewState);
            var registryType = appAssembly.GetType("ExplorerDirs.App.WindowSessionRegistry", throwOnError: true)!;
            var registry = Activator.CreateInstance(registryType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [sessionPath], null)!;
            var loadedPaths = (System.Collections.IEnumerable)registryType.GetMethod("Load")!.Invoke(registry, null)!;
            var initialPaths = loadedPaths.Cast<string>().ToArray();
            if (initialPaths.Length != 1 || !string.Equals(initialPaths[0], sessionPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A new profile did not begin with only its primary window session.");
            var secondaryState = CreateSessionState(appAssembly, secondIdentity, viewState);
            var pinnedTabsType = appAssembly.GetType("ExplorerDirs.App.PinnedTabEntry", throwOnError: true)!;
            var emptyPins = Array.CreateInstance(pinnedTabsType, 0);
            var secondarySessionPath = (string)registryType.GetMethod("Create")!.Invoke(registry, [secondaryState, emptyPins])!;
            if (!Directory.Exists(folders[0]) || !Directory.Exists(folders[1]) || !File.Exists(sessionPath) ||
                !File.Exists(secondarySessionPath))
                throw new InvalidOperationException("The profile session fixture was not persisted.");

            (owner as IDisposable)?.Dispose();
            owner = null;
            var child = StartChild(sessionPath, profile);
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Fresh-process multi-window session recovery failed (exit {child.ExitCode}). stdout=[{child.StdOut}] stderr=[{child.StdErr}]");

            Console.WriteLine($"profile-owner-session-recovery: fresh-process=True; registered-windows=2; stable-profile-id=True; view-columns=3; recursive-values=[5|29 || 7|31]; child-exit={child.ExitCode}; child-output=[{child.StdOut.Trim()}]");
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
        var hosts = new List<HwndHost>();
        try
        {
            var registryType = appAssembly.GetType("ExplorerDirs.App.WindowSessionRegistry", throwOnError: true)!;
            var registry = Activator.CreateInstance(registryType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [sessionPath], null)!;
            var sessionPaths = ((System.Collections.IEnumerable)registryType.GetMethod("Load")!.Invoke(registry, null)!)
                .Cast<string>().ToArray();
            if (sessionPaths.Length != 2 || !string.Equals(sessionPaths[0], Path.GetFullPath(sessionPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The fresh process did not restore both registered window sessions: [{string.Join('|', sessionPaths)}].");

            var sessionStoreType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
            var restoredSessions = sessionPaths.Select(path =>
            {
                var store = Activator.CreateInstance(sessionStoreType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [path, Path.Combine(profile, "legacy-session.json")], null)!;
                return sessionStoreType.GetMethod("Load")!.Invoke(store, null)
                    ?? throw new InvalidOperationException($"The fresh process could not load window session '{Path.GetFileName(path)}'.");
            }).ToArray();
            var identities = new string[restoredSessions.Length];
            var viewStates = new object[restoredSessions.Length];
            var expectedSizes = new[] { new ulong[] { 5, 29 }, new ulong[] { 7, 31 } };
            for (var index = 0; index < restoredSessions.Length; index++)
            {
                var firstPane = ((System.Collections.IEnumerable)restoredSessions[index].GetType()
                    .GetProperty("Panes")!.GetValue(restoredSessions[index])!).Cast<object>().First();
                var tab = ((System.Collections.IEnumerable)firstPane).Cast<object>().First();
                identities[index] = (string)tab.GetType().GetProperty("Path")!.GetValue(tab)!;
                viewStates[index] = tab.GetType().GetProperty("View")!.GetValue(tab)!;
            }
            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var viewColumns = new object[viewStates.Length][];
            var paths = new string[identities.Length];
            for (var index = 0; index < identities.Length; index++)
            {
                paths[index] = codec.GetMethod("ToQdrTextPath", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [identities[index], (Guid?)classId]) as string ?? "";
                if (!Directory.Exists(paths[index]))
                    throw new InvalidOperationException($"Restored window {index + 1} provider tab no longer resolves to its physical folder.");
                viewColumns[index] = ((System.Collections.IEnumerable)viewStates[index].GetType()
                    .GetProperty("DisplayColumns")!.GetValue(viewStates[index])!).Cast<object>().ToArray();
                if (viewColumns[index].Length != 3 || !viewColumns[index].Any(column =>
                        column.GetType().GetProperty("Key")!.GetValue(column)!.ToString() ==
                        "{b725f130-47ef-101a-a5f1-02608c9eebac}:12"))
                    throw new InvalidOperationException($"Restored window {index + 1} did not retain the recursive size column.");
            }

            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            var grid = new Grid { Width = 1400, Height = 500 };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < identities.Length; index++)
            {
                var host = (HwndHost)Activator.CreateInstance(hostType, identities[index], viewStates[index])!;
                hosts.Add(host);
                Grid.SetColumn(host, index);
                grid.Children.Add(host);
            }
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs fresh-process multi-window session recovery")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 1400, Height = 500
            });
            source.RootVisual = grid;
            grid.Measure(new Size(1400, 500));
            grid.Arrange(new Rect(0, 0, 1400, 500));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The fresh-process recovery host became visible.");

            var navigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var hostIndex = index;
                WaitUntil(() => Equals(navigated.GetValue(host), true) && (nint)viewWindow.GetValue(host)! != 0,
                    TimeSpan.FromSeconds(15), $"Fresh process could not navigate to registered window session {hostIndex + 1}.");
            }
            var valuesReader = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var allValues = new string[hosts.Count][];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                WaitUntil(() =>
                {
                    allValues[index] = (string[])valuesReader.Invoke(null, [hostType, host, 2])!;
                    return allValues[index].Select(value => ulong.TryParse(value, out var bytes) ? bytes : ulong.MaxValue)
                        .Order().SequenceEqual(expectedSizes[index]);
                }, TimeSpan.FromSeconds(20), $"Fresh process did not restore recursive values for window session {index + 1}.");
            }
            Console.WriteLine($"fresh-process-provider-session: profile-id={classId:D}; registered-window-sessions={sessionPaths.Length}; provider-paths=[{string.Join('|', identities)}]; columns=[{string.Join('|', viewColumns.Select(columns => columns.Length))}]; recursive-values=[{string.Join(" || ", allValues.Select(values => string.Join('|', values.OrderBy(ulong.Parse))))}]; parent-visible=False");
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

    private static object CreateSessionState(Assembly appAssembly, string identity, object viewState)
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
        return sessionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 11)
            .Invoke([1, 4u, panes, 0, new[] { 0 }, -1, null, null, null, null, null]);
    }

    private static void SaveSession(Assembly appAssembly, string path, string identity, object viewState)
    {
        var state = CreateSessionState(appAssembly, identity, viewState);
        var storeType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
        var store = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [path, Path.Combine(Path.GetDirectoryName(path)!, "legacy-session.json")], null)!;
        storeType.GetMethod("Save")!.Invoke(store, [state]);
    }

    private static void CreateFixture(string root, int smallBytes, int largeBytes)
    {
        Directory.CreateDirectory(Path.Combine(root, "folder-small"));
        Directory.CreateDirectory(Path.Combine(root, "folder-large"));
        File.WriteAllBytes(Path.Combine(root, "folder-small", "payload.bin"), new byte[smallBytes]);
        File.WriteAllBytes(Path.Combine(root, "folder-large", "payload.bin"), new byte[largeBytes]);
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
