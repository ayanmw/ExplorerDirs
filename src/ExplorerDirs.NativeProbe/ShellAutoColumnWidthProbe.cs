using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Checks the Explorer Ctrl+NumPad+ contract against a hidden stock IShellView.
// All files and input messages are confined to this probe's temporary fixture.
internal static class ShellAutoColumnWidthProbe
{
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid PropertyFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        nint parent = 0, pidl = 0, folder = 0, view = 0, viewWindow = 0, hostPointer = 0, folderView = 0, listView = 0;
        var created = false;
        var host = default(StreamViewHost);
        var keyboardBefore = new byte[256];
        var keyboardCaptured = false;
        try
        {
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "column-autosize-fixture-with-a-long-name.txt"), new string('x', 4096));
            File.WriteAllText(Path.Combine(fixture, "second-long-column-autosize-fixture.bin"), new string('y', 65536));

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated auto-column width probe",
                0x80000000, 0, 0, 800, 500, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out pidl, 0, out _));
            var folderId = ShellFolderId;
            Check(SHBindToObject(0, pidl, 0, in folderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            host = new StreamViewHost(parent, []);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var rect = new StreamProbeRect { Right = 760, Bottom = 460 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref rect, out viewWindow));
            created = true;
            Pump(250);

            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<SetViewMode>(folderView, 35)(folderView, 4, 16)); // Details.
            var columns = new DisplayColumnState[]
            {
                new(new(PropertyFormatId, 10), 110), // Name; the native Shell view clamps narrower widths to 110 px.
                new(new(PropertyFormatId, 4), 110),  // Type.
                new(new(PropertyFormatId, 12), 110)  // Size.
            };
            Check(ShellColumnManager.Apply(folderView, columns));
            Pump(200);
            Check(ShellColumnManager.Capture(folderView, out var before));
            if (before is null || !before.SequenceEqual(columns))
                throw new Exception($"The narrow column-width baseline did not read back exactly; requested={DescribeColumns(columns)}; actual={(before is null ? "<unavailable>" : DescribeColumns(before))}.");

            listView = FindChildByClass(viewWindow, "SysListView32");
            if (listView == 0) throw new Exception("The isolated Details view has no ListView child.");
            if (!GetKeyboardState(keyboardBefore)) throw new Exception("Could not capture the probe thread keyboard state.");
            keyboardCaptured = true;
            var simulatedKeyboard = keyboardBefore.ToArray();
            simulatedKeyboard[0x11] |= 0x80; // Ctrl held.
            simulatedKeyboard[0x90] |= 0x01; // Num Lock on.
            simulatedKeyboard[0x6B] |= 0x80; // VK_ADD held.
            if (!SetKeyboardState(simulatedKeyboard)) throw new Exception("Could not set the probe thread Ctrl+NumPad+ state.");

            var message = new NativeMessage
            {
                Window = listView,
                Id = 0x0100, // WM_KEYDOWN.
                WParam = 0x6B, // VK_ADD.
                LParam = (nint)(1 | (0x4E << 16))
            };
            var translateResult = At<TranslateAccelerator>(view, 5)(view, ref message);
            if (translateResult == 1)
                SendMessageW(listView, message.Id, message.WParam, message.LParam);
            else if (translateResult < 0)
                Check(translateResult);
            SendMessageW(listView, 0x0101, 0x6B, (nint)(unchecked((int)0xC04E0001))); // WM_KEYUP.
            Pump(250);
            if (!SetKeyboardState(keyboardBefore)) throw new Exception("Could not restore the probe thread keyboard state.");
            keyboardCaptured = false;

            Check(ShellColumnManager.Capture(folderView, out var after));
            if (after is null) throw new Exception("The Shell did not return post-shortcut column widths.");
            var widened = after.Where(column => column.Width > 110).ToArray();
            Console.WriteLine($"auto-column-width: translate-accelerator=0x{translateResult:X8}; before={DescribeColumns(before)}; after={DescribeColumns(after)}; widened={widened.Length}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (widened.Length == 0)
                throw new Exception("Ctrl+NumPad+ did not widen any Details columns in the hidden Shell view.");
            if (GetForegroundWindow() != foreground || IsWindowVisible(parent))
                throw new Exception("The auto-column probe changed foreground or showed its parent window.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("auto-column-width failure: " + ex.Message);
            return 1;
        }
        finally
        {
            if (keyboardCaptured) SetKeyboardState(keyboardBefore);
            if (folderView != 0) Marshal.Release(folderView);
            if (created && view != 0)
            {
                try { Check(At<DestroyViewWindow>(view, 10)(view)); }
                catch (Exception ex) { Console.WriteLine("auto-column-width cleanup: " + ex.Message); }
            }
            if (view != 0) Marshal.Release(view);
            if (folder != 0) Marshal.Release(folder);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (host is not null) { host.View = 0; host.Dispose(); GC.KeepAlive(host); }
            if (parent != 0) DestroyWindow(parent);
            var fullFixture = Path.GetFullPath(fixture);
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFixture))
                Directory.Delete(fullFixture, recursive: true);
            if (GetForegroundWindow() != foreground)
                Console.WriteLine("auto-column-width cleanup: foreground changed.");
        }
    }

    private static string DescribeColumns(IReadOnlyList<DisplayColumnState> columns) =>
        string.Join(',', columns.Select(column => $"{column.Key.PropertyId}:{column.Width}"));

    private static nint FindChildByClass(nint parent, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) > 0 && name.ToString() == className)
            {
                found = window;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static void Pump(int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
    }
    private delegate bool EnumChildProc(nint window, nint parameter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous,
        ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewMode(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TranslateAccelerator(nint self, ref NativeMessage message);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState([In] byte[] state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
