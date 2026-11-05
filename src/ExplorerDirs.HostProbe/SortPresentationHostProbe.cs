using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;

internal static class SortPresentationHostProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static int Run(string adapter)
    {
        var assembly = Assembly.Load("ExplorerDirs.App");
        var hostType = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var stateType = assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var ownerType = assembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", true)!;
        var keyType = assembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
        var columnType = assembly.GetType("ExplorerDirs.App.DisplayColumnState", true)!;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SortPaint-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile"); var folder = Path.Combine(root, "files");
        Directory.CreateDirectory(profile); Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in new[] { ("folder-small", 5), ("folder-large", 29) })
        { Directory.CreateDirectory(Path.Combine(folder, name)); File.WriteAllBytes(Path.Combine(folder, name, "data"), new byte[bytes]); }
        File.WriteAllBytes(Path.Combine(folder, "file-middle.bin"), new byte[17]);
        var id = Guid.NewGuid();
        var owner = ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [id, profile, Path.GetFullPath(adapter)])!;
        ownerType.GetMethod("SetFolderSizeSource", Instance)!.Invoke(owner,
            [Enum.Parse(assembly.GetType("ExplorerDirs.App.FolderSizeSource", true)!, "BuiltIn"), true]);
        HwndSource? source = null; HwndHost? host = null;
        try
        {
            var codec = assembly.GetType("ExplorerDirs.App.QdrShellPathCodec", true)!;
            var identity = codec.GetMethod("ToProviderIdentity", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [folder, id])!;
            var state = stateType.GetMethod("Default", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [4u, 16])!;
            var columns = Array.CreateInstance(columnType, 2);
            var format = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac");
            for (var index = 0; index < 2; index++)
                columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType, [format, index == 0 ? 10u : 12u])!, 220]), index);
            stateType.GetProperty("DisplayColumns")!.SetValue(state, columns);
            host = (HwndHost)Activator.CreateInstance(hostType, identity, state)!;
            hostType.GetMethod("ConfigureFolderSizeLocations", Instance)!.Invoke(host, [id, true]);
            var factoryType = assembly.GetType("ExplorerDirs.App.FolderSizeSortContextFactory", true)!;
            var factory = Delegate.CreateDelegate(factoryType, owner, ownerType.GetMethod("TryCreateSortContext", Instance)!);
            hostType.GetMethod("ConfigureFolderSizeSortContextFactory", Instance)!.Invoke(host, [factory]);
            hostType.GetMethod("ConfigureGlobalMixedSort", Instance)!.Invoke(host, [false]);
            source = new HwndSource(new HwndSourceParameters("Private sort presentation probe")
                { WindowStyle = unchecked((int)0x80000000), Width = 720, Height = 480 });
            source.RootVisual = host;
            var viewField = hostType.GetField("_viewWindow", Instance)!;
            var helpers = typeof(ShellProviderLocationProbe);
            var readRows = helpers.GetMethod("ReadNativeListViewSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
            (int Count, string[] Names) Rows() => ((int, string[]))readRows.Invoke(null, [(nint)viewField.GetValue(host)!])!;
            Wait(() => (nint)viewField.GetValue(host)! != 0 && Rows().Count == 3, "initial rows");
            Wait(() => !(bool)hostType.GetField("_globalMixedSortPending", Instance)!.GetValue(host)!, "initial global policy");
            var readSizes = helpers.GetMethod("ReadRecursiveValues", BindingFlags.Static | BindingFlags.NonPublic)!;
            Wait(() => ((string[])readSizes.Invoke(null, [hostType, host, 3])!).All(value => ulong.TryParse(value, out _)), "known folder totals");
            var header = (nint)helpers.GetMethod("FindSortHeader", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [(nint)viewField.GetValue(host)!])!;
            var click = helpers.GetMethod("ClickHeaderColumn", BindingFlags.Static | BindingFlags.NonPublic)!;
            var lease = hostType.GetField("_sortPaintLease", Instance)!;
            var nativeView = (nint)hostType.GetMethod("GetFolderView2Pointer", Instance)!.Invoke(host, null)!;
            try
            {
                var select = Marshal.GetDelegateForFunctionPointer<SelectRow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(nativeView), 15 * IntPtr.Size));
                Marshal.ThrowExceptionForHR(select(nativeView, Array.IndexOf(Rows().Names, "file-middle.bin"), 0x15));
            }
            finally { Marshal.Release(nativeView); }
            var selectionReader = helpers.GetMethod("ReadNativeListViewSelectionState", BindingFlags.Static | BindingFlags.NonPublic)!;
            void CheckSelection()
            {
                var selection = ((string[], string?))selectionReader.Invoke(null, [(nint)viewField.GetValue(host)!])!;
                if (!selection.Item1.SequenceEqual(new[] { "file-middle.bin" }) || selection.Item2 != "file-middle.bin")
                    throw new Exception("Column sorting changed the selected/focused file.");
            }

            for (var column = 0; column < 2; column++)
            {
                var observed = new HashSet<string>();
                for (var iteration = 0; iteration < 6; iteration++)
                {
                    var before = (long)hostType.GetProperty("SortPresentationCount", Instance)!.GetValue(host)!;
                    click.Invoke(null, [header, column]);
                    var after = (long)hostType.GetProperty("SortPresentationCount", Instance)!.GetValue(host)!;
                    if (after <= before) throw new Exception($"Header sorting did not acquire a presentation lease: column={column};iteration={iteration};status={hostType.GetProperty("LastSortPresentationStatus", Instance)!.GetValue(host)}");
                    Wait(() => lease.GetValue(host) is null, "sort frame release");
                    if (!Equals(hostType.GetProperty("LastSortPresentationStatus", Instance)!.GetValue(host), "ready")) throw new Exception("Sort frame did not finish with ready rows.");
                    Wait(() => Rows().Count == 3, "sorted row count");
                    CheckSelection();
                    observed.Add(string.Join('|', Rows().Names));
                }
                if (observed.Count != 2) throw new Exception($"Expected two sort orders for column {column}, got {observed.Count}.");
                Console.WriteLine($"Sort presentation: column={column}; clicks=6; stable-orders=2; leases-released=True");
            }
            var sizeRead = helpers.GetMethod("ReadRecursiveValues", BindingFlags.Static | BindingFlags.NonPublic)!;
            Console.WriteLine("Sort presentation before mixed: " + string.Join('|', Rows().Names) + ";values=" +
                string.Join('|', (string[])sizeRead.Invoke(null, [hostType, host, 3])!));
            hostType.GetMethod("ConfigureGlobalMixedSort", Instance)!.Invoke(host, [true]);
            Wait(() => !(bool)hostType.GetField("_globalMixedSortPending", Instance)!.GetValue(host)!, "loaded global policy");
            Wait(() => Rows().Names.ElementAtOrDefault(1) == "file-middle.bin", "mixed size row order");
            Console.WriteLine("Sort presentation loaded global policy: PASS (file is between the two sized directories).");
            Wait(() => lease.GetValue(host) is null, "global presentation release");
            CheckSelection();
            click.Invoke(null, [header, 0]);
            source.RootVisual = null; host.Dispose(); host = null;
            Console.WriteLine("Sort presentation: PASS (actual product HwndHost, Name/Size clicks, bounded last-frame leases, loaded global update, disposal during sort; no physical input).");
            return 0;
        }
        finally { if (source is not null) { source.RootVisual = null; source.Dispose(); } host?.Dispose(); ((IDisposable)owner).Dispose(); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SelectRow(nint self, int row, uint flags);

    private static void Wait(Func<bool> ready, string stage)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(20);
        }
        if (!ready()) throw new Exception("Sort presentation timed out: " + stage);
    }
}
