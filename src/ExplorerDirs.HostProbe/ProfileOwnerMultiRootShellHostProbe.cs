using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProfileOwnerMultiRootShellHostProbe
{
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProfileOwnerMultiRoot-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folders = Enumerable.Range(0, 4).Select(index => Path.Combine(root, $"pane-{index + 1}")).ToArray();
        var expectedSizes = new[] { new ulong[] { 5, 29 }, new ulong[] { 7, 31 },
            new ulong[] { 11, 37 }, new ulong[] { 13, 41 } };
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        object? owner = null;
        HwndSource? source = null;
        var hosts = new List<HwndHost>();
        var foreground = GetForegroundWindow();
        try
        {
            Directory.CreateDirectory(profile);
            for (var index = 0; index < folders.Length; index++)
            {
                Directory.CreateDirectory(folders[index]);
                CreateFolder(folders[index], "folder-small", checked((int)expectedSizes[index][0]));
                CreateFolder(folders[index], "folder-large", checked((int)expectedSizes[index][1]));
            }

            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var ownerType = appAssembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", throwOnError: true)!;
            owner = ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [classId, profile, FindProductAdapter()]);
            if (owner is not IDisposable) throw new InvalidOperationException("The production profile owner is not disposable.");

            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var viewState = CreateViewState(appAssembly);
            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            var hostNavigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hostViewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readValues = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var selectItem = typeof(ShellProviderLocationProbe).GetMethod("SelectShellViewItemByPath",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var readSelection = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewSelectionState",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            var grid = new Grid { Width = 960, Height = 640 };
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < folders.Length; index++)
            {
                var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [folders[index], classId])!;
                var host = (HwndHost)Activator.CreateInstance(hostType, identity, viewState)!;
                hosts.Add(host);
                Grid.SetRow(host, index / 2);
                Grid.SetColumn(host, index % 2);
                grid.Children.Add(host);
            }
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs four-pane product Shell host probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 960, Height = 640
            });
            source.RootVisual = grid;
            grid.Measure(new Size(960, 640));
            grid.Arrange(new Rect(0, 0, 960, 640));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The four-pane product Shell host parent became visible.");

            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var currentHost = host;
                var expectedIdentity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [folders[index], classId])!;
                WaitUntil(() => Equals(hostNavigated.GetValue(currentHost), true) &&
                    (nint)hostViewWindow.GetValue(currentHost)! != 0,
                    TimeSpan.FromSeconds(15), $"Product pane {index + 1} did not navigate.");
                if (!string.Equals((string)hostType.GetProperty("CurrentPath")!.GetValue(host)!, expectedIdentity,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Product pane {index + 1} lost its independent provider identity.");
                WaitUntil(() =>
                {
                    var counts = readCounts.Invoke(host, [false, false]);
                    return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == 2;
                }, TimeSpan.FromSeconds(15), $"Product pane {index + 1} did not enumerate its own two rows.");
            }

            var paneValues = new string[hosts.Count][];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                WaitUntil(() =>
                {
                    paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, 2])!;
                    return paneValues[index].Select(value => ulong.TryParse(value, out var bytes) ? bytes : ulong.MaxValue)
                        .Order().SequenceEqual(expectedSizes[index]);
                }, TimeSpan.FromSeconds(20), $"Product pane {index + 1} received another pane's recursive values.");

                var captured = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(host, null)!;
                var capturedColumns = ((System.Collections.IEnumerable)captured.GetType()
                    .GetProperty("DisplayColumns")!.GetValue(captured)!).Cast<object>().ToArray();
                if (capturedColumns.Length != 3 || !capturedColumns.Any(column =>
                        column.GetType().GetProperty("Key")!.GetValue(column)!.ToString() ==
                        "{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1"))
                    throw new InvalidOperationException($"Product pane {index + 1} did not retain its own recursive column state.");
            }

            for (var index = 0; index < hosts.Count; index++)
                selectItem.Invoke(null, [hostType, hosts[index], Path.Combine(folders[index], index % 2 == 0 ? "folder-small" : "folder-large")]);
            var paneSelections = hosts.Select(host =>
            {
                var viewWindow = (nint)hostViewWindow.GetValue(host)!;
                var selection = readSelection.Invoke(null, [viewWindow])!;
                return (string[])selection.GetType().GetField("Item1")!.GetValue(selection)!;
            }).ToArray();
            for (var index = 0; index < paneSelections.Length; index++)
            {
                var expectedSelection = index % 2 == 0 ? "folder-small" : "folder-large";
                if (!paneSelections[index].SequenceEqual([expectedSelection], StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Selection crossed pane {index + 1} identity: [{string.Join('|', paneSelections[index])}].");
            }

            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The product panes changed the foreground window.");
            Console.WriteLine($"profile-owner-multi-root-shell-host: simultaneous-hosts={hosts.Count}; roots=independent; recursive-values=[{string.Join(" || ", paneValues.Select(values => string.Join('|', values.OrderBy(ulong.Parse)) ))}]; selections=[{string.Join('|', paneSelections.Select(values => string.Join(',', values)))}]; parents-hidden=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else
            {
                foreach (var host in hosts)
                    if (host is IDisposable disposable) disposable.Dispose();
            }
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            if (owner is IDisposable lifetime) lifetime.Dispose();
            var registrationRemains = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(classKeyPath) is not null ||
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            HostProbeForegroundGuard.EnsureUnchanged(foreground,
                GetForegroundWindow(), "profile-owned multi-root Shell hosts");
            if (registrationRemains)
                throw new InvalidOperationException("The production profile owner left volatile Shell registration behind.");
        }
    }

    private static void CreateFolder(string root, string name, int bytes)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "payload.bin"), new byte[bytes]);
    }

    private static object CreateViewState(Assembly appAssembly)
    {
        var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
        var keyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
        var columnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
        var basic = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        var columns = Array.CreateInstance(columnType, 3);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType, [basic, 10u])!, 240]), 0);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType,
            [Guid.Parse("9E5E05AC-1936-4A75-94F7-4704B8B01923"), 1u])!, 176]), 1);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType, [basic, 4u])!, 160]), 2);
        var constructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate => candidate.GetParameters().Length == 6);
        return constructor.Invoke([4u, 16, Array.CreateInstance(
            appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!, 0), null, true, columns]);
    }

    private static string FindProductAdapter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-x64",
                "ExplorerDirs.ShellNamespace.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build the product Shell namespace adapter before this probe.");
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message) =>
        typeof(ShellProviderLocationProbe).GetMethod("WaitUntil", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [condition, timeout, message]);

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool IsWindowVisible(nint window);
}
