using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

// Actual product hosts on the caller's private Desktop. No DoDragDrop, input or file operations.
internal static class DropRegistrationHostProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Run(string adapter)
    {
        var assembly = Assembly.Load("ExplorerDirs.App");
        var hostType = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var stateType = assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var ownerType = assembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", true)!;
        var codecType = assembly.GetType("ExplorerDirs.App.QdrShellPathCodec", true)!;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-DropRegistration-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folder = Path.Combine(root, "files");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "start.bat"), "@rem never executed\r\n");
        var classId = Guid.NewGuid();
        var owner = ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [classId, profile, Path.GetFullPath(adapter)])!;
        try
        {
            foreach (var projected in new[] { false, true })
            {
                var identity = projected
                    ? (string)codecType.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [folder, classId])! : folder;
                var state = stateType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [4u, 16])!;
                using var host = (HwndHost)Activator.CreateInstance(hostType, identity, state)!;
                if (projected)
                {
                    hostType.GetMethod("ConfigureFolderSizeLocations", Members)!.Invoke(host, [classId, true]);
                    var factoryType = assembly.GetType("ExplorerDirs.App.FolderSizeSortContextFactory", true)!;
                    var factory = Delegate.CreateDelegate(factoryType, owner, ownerType.GetMethod("TryCreateSortContext", Members)!);
                    hostType.GetMethod("ConfigureFolderSizeSortContextFactory", Members)!.Invoke(host, [factory]);
                }
                using var source = new HwndSource(new HwndSourceParameters("Private Shell OLE registration probe")
                    { WindowStyle = unchecked((int)0x80000000), Width = 720, Height = 480 });
                try
                {
                    source.RootVisual = host;
                    var viewField = hostType.GetField("_viewWindow", Members)!;
                    var snapshots = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var watch = Stopwatch.StartNew();
                    nint view = 0;
                    var targets = new List<string>();
                    while (watch.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        var frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                        view = (nint)viewField.GetValue(host)!;
                        if (view != 0)
                        {
                            var rows = ((int Count, string[] Names))snapshots.Invoke(null, [view])!;
                            if (rows.Count == 1 && rows.Names.Contains("start.bat"))
                            {
                                targets.Clear();
                                Inspect(view);
                                EnumChildWindows(view, (child, _) => { Inspect(child); return true; }, 0);
                                if (targets.Count != 0) break;
                            }
                        }
                        Thread.Sleep(20);
                    }
                    Console.WriteLine($"product-drop-registration: projected={projected}; targets={string.Join('|', targets)}");
                    if (targets.Count == 0) throw new InvalidOperationException($"No OLE drop target on actual product view subtree; projected={projected}.");
                    void Inspect(nint hwnd)
                    {
                        // Borrowed property values: never QueryInterface or Release them.
                        if (GetPropW(hwnd, "OleDropTargetInterface") == 0) return;
                        var name = new StringBuilder(128);
                        GetClassNameW(hwnd, name, name.Capacity);
                        targets.Add(name.ToString());
                    }
                }
                finally { source.RootVisual = null; }
            }
            Console.WriteLine("product-drop-registration: PASS physical/projected product hosts; no manual registration, DoDragDrop or input");
            return 0;
        }
        finally
        {
            ((IDisposable)owner).Dispose();
            Directory.Delete(root, true);
        }
    }
    private delegate bool EnumWindow(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetPropW(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder name, int count);
}
