using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProfileOwnerShellHostProbe
{
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProfileOwnerHost-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folder = Path.Combine(root, "fixture");
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        object? owner = null;
        HwndSource? source = null;
        HwndHost? host = null;
        var foreground = GetForegroundWindow();
        try
        {
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(Path.Combine(folder, "folder-small"));
            Directory.CreateDirectory(Path.Combine(folder, "folder-large"));
            File.WriteAllBytes(Path.Combine(folder, "folder-small", "payload.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(folder, "folder-large", "payload.bin"), new byte[29]);

            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var adapterPath = FindProductAdapter();
            var ownerType = appAssembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", throwOnError: true)!;
            owner = ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [classId, profile, adapterPath]);
            if (owner is not IDisposable) throw new InvalidOperationException("The production profile owner is not disposable.");

            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [folder, classId])!;
            var textPath = codec.GetMethod("ToQdrTextPath", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [identity, (Guid?)classId]);
            if (!string.Equals(textPath as string, folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The experiment provider identity did not round-trip to its physical Q-Dir path.");

            var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var keyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var columnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var columns = Array.CreateInstance(columnType, 3);
            var basic = Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            columns.SetValue(Activator.CreateInstance(columnType,
                [Activator.CreateInstance(keyType, [basic, 10u])!, 240]), 0);
            columns.SetValue(Activator.CreateInstance(columnType,
                [Activator.CreateInstance(keyType, [Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u])!, 176]), 1);
            columns.SetValue(Activator.CreateInstance(columnType,
                [Activator.CreateInstance(keyType, [basic, 4u])!, 160]), 2);
            var constructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(candidate => candidate.GetParameters().Length == 6);
            var viewState = constructor.Invoke([4u, 16, Array.CreateInstance(
                appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!, 0), null, true, columns]);

            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            host = (HwndHost)Activator.CreateInstance(hostType, identity, viewState)!;
            var grid = new Grid { Width = 700, Height = 360 };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs profile owner Shell host probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 700, Height = 360
            });
            source.RootVisual = grid;
            grid.Measure(new Size(700, 360));
            grid.Arrange(new Rect(0, 0, 700, 360));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The hidden profile-owner host parent became visible.");

            var navigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() => Equals(navigated.GetValue(host), true) && (nint)viewWindow.GetValue(host)! != 0,
                TimeSpan.FromSeconds(15), "ExplorerBrowserHost did not navigate through the production profile owner registration.");
            if (!string.Equals((string)hostType.GetProperty("CurrentPath")!.GetValue(host)!, identity,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ExplorerBrowserHost did not retain the profile provider parsing name.");

            var location = hostType.GetField("_location", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            if ((bool)location.GetType().GetProperty("IsFileSystemLocation")!.GetValue(location)!)
                throw new InvalidOperationException("The provider experiment location was incorrectly marked as a filesystem path.");
            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() =>
            {
                var counts = readCounts.Invoke(host, [false, false]);
                return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == 2;
            }, TimeSpan.FromSeconds(15), "The profile-owned provider view did not enumerate its two folders.");

            var valuesReader = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] values = [];
            WaitUntil(() =>
            {
                values = (string[])valuesReader.Invoke(null, [hostType, host, 2])!;
                return values.Length == 2 && values.Select(value => ulong.TryParse(value, out var bytes)
                        ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual([5UL, 29UL]);
            }, TimeSpan.FromSeconds(20), "The profile-owned provider did not asynchronously fill the recursive size values.");
            var byteValues = values.Select(ulong.Parse).Order().ToArray();
            if (!byteValues.SequenceEqual([5UL, 29UL]))
                throw new InvalidOperationException($"The recursive values were unexpected: [{string.Join('|', values)}].");

            var captured = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(host, null)!;
            var capturedColumns = ((System.Collections.IEnumerable)captured.GetType().GetProperty("DisplayColumns")!.GetValue(captured)!)
                .Cast<object>().ToArray();
            if (capturedColumns.Length != 3 || !capturedColumns.Any(column =>
                    column.GetType().GetProperty("Key")!.GetValue(column)!.ToString() ==
                    "{b725f130-47ef-101a-a5f1-02608c9eebac}:12"))
                throw new InvalidOperationException("The production host did not retain the experiment's three-column view state.");

            Console.WriteLine($"profile-owner-shell-host: volatile-owner=True; path-identity=True; qdr-path-roundtrip=True; filesystem-location=False; columns={capturedColumns.Length}; recursive-bytes=[{string.Join('|', byteValues)}]; parent-visible=False; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The profile-owned Shell host changed the foreground window.");
            return 0;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else if (host is IDisposable disposable) disposable.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            if (owner is IDisposable ownerLifetime) ownerLifetime.Dispose();
            var registrationRemains = Registry.CurrentUser.OpenSubKey(classKeyPath) is not null ||
                Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            HostProbeForegroundGuard.EnsureUnchanged(foreground, GetForegroundWindow(), "profile-owned Shell host probe");
            if (registrationRemains)
                throw new InvalidOperationException("The production profile owner left volatile Shell registration behind.");
        }
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

    private static bool IsWindowVisible(nint window) => window != 0 && IsWindowVisibleNative(window);

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        var method = typeof(ShellProviderLocationProbe).GetMethod("WaitUntil", BindingFlags.Static | BindingFlags.NonPublic)!;
        method.Invoke(null, [condition, timeout, message]);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool IsWindowVisibleNative(nint window);
}
