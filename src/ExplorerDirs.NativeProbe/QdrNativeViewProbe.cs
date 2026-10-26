using System.Runtime.InteropServices;
using ExplorerDirs.App;

// A hidden IShellView consumes the exported stream through IShellBrowser.
// No application decoder/setters, visible window, input, activation or registry API.
internal static class QdrNativeViewProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden view-state stream probe",
            0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
        if (parent == 0) throw new Exception("Hidden parent creation failed.");
        nint pidl = 0, folder = 0;
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        string? fixturePath = null;
        var result = 0;
        try
        {
            fixturePath = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixturePath);
            File.WriteAllText(Path.Combine(fixturePath, "one.txt"), "one");
            File.WriteAllText(Path.Combine(fixturePath, "two.bin"), "two");
            Check(SHParseDisplayName(fixturePath, 0, out pidl, 0, out _));
            var folderId = new Guid("000214E6-0000-0000-C000-000000000046");
            Check(SHBindToObject(0, pidl, 0, in folderId, out folder));
            var format = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var name = new PropertyKeyState(format, 10);
            var type = new PropertyKeyState(format, 4);
            var columns = new DisplayColumnState[]
            { new(name, 271), new(new(format, 12), 137), new(type, 193) };
            foreach (var grouped in new[] { false, true })
            foreach (var style in ViewStyle.All)
            {
                var size = style.IconSize > 0 ? style.IconSize : style.Mode switch { 6 => 48, 8 => 32, _ => 16 };
                var expected = new ViewState(style.Mode, size, [new(name, -1)],
                    grouped ? type : null, false, columns);
                var text = QdrViewWriter.Write(expected)!;
                var streamBytes = Convert.FromHexString(text.Split('|')[0].Replace("%", ""));
                RunView(parent, folder, streamBytes, expected);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Hidden/foreground boundary changed.");
            }
        }
        catch (Exception ex) { Console.WriteLine("qdr-native-stream failure: " + ex.Message); result = 1; }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (fixturePath is not null)
            {
                var fullFixturePath = Path.GetFullPath(fixturePath);
                var fixturePrefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (fullFixturePath.StartsWith(fixturePrefix, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(fullFixturePath))
                {
                    try { Directory.Delete(fullFixturePath, recursive: true); }
                    catch (Exception ex)
                    {
                        Console.WriteLine("qdr-native-stream failure: fixture cleanup failed: " + ex.GetType().Name);
                        result = 1;
                    }
                }
            }
            if (!DestroyWindow(parent) || IsWindow(parent))
            {
                Console.WriteLine("qdr-native-stream failure: hidden parent cleanup failed.");
                result = 1;
            }
        }
        if (GetForegroundWindow() != foreground)
        {
            Console.WriteLine("qdr-native-stream failure: foreground changed during cleanup.");
            result = 1;
        }
        if (result == 0)
            Console.WriteLine("qdr-native-stream: 16 cases passed; parent-visible=False; foreground-unchanged=True");
        return result;
    }

    private static void RunView(nint parent, nint folder, byte[] bytes, ViewState expected)
    {
        var viewId = new Guid("000214E3-0000-0000-C000-000000000046");
        nint view = 0, hostPointer = 0, folderView = 0, viewWindow = 0;
        var created = false;
        var host = new StreamViewHost(parent, bytes);
        try
        {
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out view));
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var rect = new StreamProbeRect { Right = 900, Bottom = 600 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref rect, out viewWindow));
            created = true;
            var until = Environment.TickCount64 + 1000;
            while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
            var fvId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            Check(Marshal.QueryInterface(view, in fvId, out folderView));
            Check(At<GetMode>(folderView, 36)(folderView, out var mode, out var size));
            Check(At<GetSortCount>(folderView, 26)(folderView, out var count));
            if (count is < 0 or > 16) throw new Exception("Unexpected sort count.");
            var sorts = new StreamProbeSort[count];
            Check(At<GetSorts>(folderView, 28)(folderView, sorts, count));
            var groupResult = At<GetGroup>(folderView, 18)(folderView, out var group, out var ascending);
            if (groupResult != 1) Check(groupResult);
            Check(ShellColumnManager.Capture(folderView, out var columns));
            var sortMatches = sorts.Select(s => new SortColumnState(new(s.Key.Id, s.Key.Property), s.Direction))
                .SequenceEqual(expected.SortColumns);
            var groupMatches = expected.GroupKey is null ? groupResult == 1 :
                groupResult == 0 && new PropertyKeyState(group.Id, group.Property) == expected.GroupKey &&
                (ascending != 0) == expected.GroupAscending;
            var columnMatches = columns is not null && columns.SequenceEqual(expected.DisplayColumns!);
            Console.WriteLine($"qdr-native-stream: requested={expected.Mode}/{expected.IconSize}; grouped={expected.GroupKey is not null}; bytes={bytes.Length}; " +
                $"stream-reads={host.Reads}; mode={mode}/{size}; sort={sortMatches}; group={groupMatches}; columns={columnMatches}; " +
                $"child-visible={IsWindowVisible(viewWindow)}");
            if (IsWindowVisible(viewWindow) || host.Reads == 0 || mode != expected.Mode || size != expected.IconSize ||
                !sortMatches || !groupMatches || !columnMatches)
            {
                // Diagnose the native persistence contract from its own output.
                // No application decoder participates in the restore check above.
                Check(At<SetMode>(folderView, 35)(folderView, expected.Mode, expected.IconSize));
                Check(At<GetMode>(folderView, 36)(folderView, out var setMode, out var setSize));
                Check(At<Simple>(view, 13)(view));
                var nativeBytes = host.ReadSavedBytes();
                Console.WriteLine($"qdr-native-stream persistence: applied={setMode}/{setSize}; writes={host.Writes}; saved-bytes={nativeBytes.Length}");
                if (nativeBytes.Length > 0)
                {
                    var encoded = string.Concat(nativeBytes.Select(b => "%" + b.ToString("X2"))) +
                        $"|CODEMODE1|{QdrViewSettings.ComputeChecksum(nativeBytes)}|{nativeBytes.Length}";
                    var known = QdrViewSettings.TryRead(encoded, out var stored);
                    Console.WriteLine($"qdr-native-stream native-saved-known={known}; saved-mode={stored.Mode}/{stored.IconSize}");
                    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics"));
                    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics", "native-view.bin"), nativeBytes);
                    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "stream-diagnostics", "exported-view.bin"), bytes);
                }
                throw new Exception("Native Shell did not restore the exported stream exactly.");
            }
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (created)
                {
                    Check(At<Simple>(view, 10)(view));
                    if (IsWindow(viewWindow)) throw new Exception("Native view cleanup left a live window.");
                }
            }
            finally
            {
                host.View = 0;
                if (view != 0) Marshal.Release(view);
                if (hostPointer != 0) Marshal.Release(hostPointer);
                host.Dispose();
                GC.KeepAlive(host);
            }
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeKey { public Guid Id; public uint Property; }
    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeSort { public StreamProbeKey Key; public int Direction; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings,
        nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetMode(nint self, out uint mode, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSorts(nint self, [Out] StreamProbeSort[] sorts, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroup(nint self, out StreamProbeKey key, out int ascending);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}

[StructLayout(LayoutKind.Sequential)] internal struct StreamProbeSettings { public uint Mode, Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct StreamProbeRect { public int Left, Top, Right, Bottom; }

// Kept inheritable so opt-in probes can add Shell callbacks without changing the shared host behavior.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public class StreamViewHost : IStreamProbeBrowser, IShellServiceProvider, IDisposable
{
    private readonly nint _parent;
    private readonly byte[] _bytes;
    internal nint View;
    internal ShellBrowserSite? BrowserSite;
    internal int Reads;
    internal int Writes;
    private nint _savedStream;
    internal byte[] ReadSavedBytes()
    {
        if (_savedStream == 0) return [];
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(_savedStream);
        var count = Marshal.AllocCoTaskMem(4);
        try
        {
            stream.Stat(out var stat, 1);
            if (stat.cbSize is < 0 or > 65536) throw new Exception("Native saved stream exceeds probe limit.");
            var data = new byte[(int)stat.cbSize];
            stream.Seek(0, 0, 0);
            Marshal.WriteInt32(count, 0);
            stream.Read(data, data.Length, count);
            if (Marshal.ReadInt32(count) != data.Length) throw new Exception("Incomplete native saved stream.");
            return data;
        }
        finally { Marshal.FreeCoTaskMem(count); Marshal.ReleaseComObject(stream); }
    }
    public void Dispose()
    {
        if (_savedStream != 0) { Marshal.Release(_savedStream); _savedStream = 0; }
    }
    public int QueryService(ref Guid service, ref Guid iid, out nint pointer)
    {
        pointer = 0;
        if (BrowserSite is not null && service == ShellBrowserSite.BrowserFrameServiceId)
            return BrowserSite.QueryService(ref service, ref iid, out pointer);
        var browserId = typeof(IStreamProbeBrowser).GUID;
        if (iid == browserId && (service == browserId ||
            service == new Guid("4c96be40-915c-11cf-99d3-00aa004ae837")))
        {
            pointer = Marshal.GetComInterfaceForObject(this, typeof(IStreamProbeBrowser));
            return 0;
        }
        return unchecked((int)0x80004002);
    }
    // Imported view bytes can be shared by callers; snapshot them so later tab/source edits cannot mutate this host's fallback state.
    internal StreamViewHost(nint parent, byte[] bytes) { _parent = parent; _bytes = (byte[])bytes.Clone(); }
    public int GetWindow(out nint window) { window = _parent; return 0; }
    public int ContextSensitiveHelp(int enter) => unchecked((int)0x80004001);
    public int InsertMenusSB(nint menu, nint widths) => 0;
    public int SetMenuSB(nint menu, nint oleMenu, nint active) => 0;
    public int RemoveMenusSB(nint menu) => 0;
    public int SetStatusTextSB(nint text) => 0;
    public int EnableModelessSB(int enable) => 0;
    public int TranslateAcceleratorSB(nint msg, ushort id) => 1;
    public virtual int BrowseObject(nint pidl, uint flags) => unchecked((int)0x80004001);
    public int GetViewStateStream(uint mode, out nint stream)
    {
        var read = (mode & 3) == 0;
        if (read) Reads++;
        // The initial import is a one-time fallback; later view creation on this host restores its last Shell-written state.
        var bytes = read ? (_savedStream == 0 ? _bytes : ReadSavedBytes()) : [];
        stream = SHCreateMemStream(bytes, (uint)bytes.Length);
        if (!read && stream != 0)
        {
            Writes++;
            if (_savedStream != 0) Marshal.Release(_savedStream);
            _savedStream = stream;
            Marshal.AddRef(stream);
        }
        return stream == 0 ? unchecked((int)0x8007000E) : 0;
    }
    public int GetControlWindow(uint id, out nint window) { window = 0; return unchecked((int)0x80004005); }
    public int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result)
    { result = 0; return unchecked((int)0x80004001); }
    public int QueryActiveShellView(out nint view)
    { view = View; if (view != 0) Marshal.AddRef(view); return view == 0 ? unchecked((int)0x80004005) : 0; }
    public int OnViewWindowActive(nint view) => 0;
    public int SetToolbarItems(nint buttons, uint count, uint flags) => 0;
    [DllImport("shlwapi.dll")] private static extern nint SHCreateMemStream(byte[] bytes, uint count);
}

// Exact IShellBrowser layout, including its IOleWindow methods (slots 3/4).
[ComVisible(true), Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStreamProbeBrowser
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp(int enter);
    [PreserveSig] int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint active);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB(nint text);
    [PreserveSig] int EnableModelessSB(int enable);
    [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] int GetControlWindow(uint id, out nint window);
    [PreserveSig] int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result);
    [PreserveSig] int QueryActiveShellView(out nint view);
    [PreserveSig] int OnViewWindowActive(nint view);
    [PreserveSig] int SetToolbarItems(nint buttons, uint count, uint flags);
}
