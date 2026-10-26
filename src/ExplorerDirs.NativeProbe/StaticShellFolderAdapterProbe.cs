using System.Runtime.InteropServices;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Hidden one-folder prototype. It uses the native COM shim because SHCreateShellFolderView
// rejects the equivalent managed CCW; no ExplorerBrowser or user files are involved.
internal static class StaticShellFolderAdapterProbe
{
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid NameKeyFormat = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private const uint SvgioAllView = 2;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        nint parent = 0, folderPidl = 0, folder = 0, adapter = 0, view = 0, folderView = 0, hostPointer = 0;
        nint nativeLibrary = 0;
        var createdView = false;
        StreamViewHost? host = null;
        var result = 0;
        try
        {
            Directory.CreateDirectory(fixture);
            foreach (var name in new[] { "two.bin", "ten.bin", "hundred.bin", "ten-copy.bin" })
                File.WriteAllText(Path.Combine(fixture, name), "static-column fixture");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs static Shell-column probe",
                0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            CheckBoundary(parent, foreground, "hidden parent creation");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolder2Id, out folder));

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, "CreateStaticShellFolderAdapter"));
            Check(createAdapter(folder, out adapter));

            var property = StaticShellColumnContract.PropertyKey;
            var nativeKey = new ShellColumnManager.NativeKey { FormatId = property.FormatId, PropertyId = property.PropertyId };
            var keyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var variant = Marshal.AllocCoTaskMem(24);
            try
            {
                Marshal.StructureToPtr(nativeKey, keyPointer, false);
                var details = Marshal.AllocCoTaskMem(272);
                try
                {
                    Marshal.Copy(new byte[272], 0, details, 272);
                    var customColumn = GetColumnCount(folder);
                    var titleHr = At<GetDetailsOf>(adapter, 18)(adapter, 0, (uint)customColumn, details);
                    if (titleHr < 0) throw new COMException($"Custom column title query failed at index {customColumn}.", titleHr);
                    if (ReadDetailsText(details) != "Static numeric test") throw new Exception("Custom column title mismatch.");
                }
                finally { Marshal.FreeCoTaskMem(details); }

                var columnIndex = GetColumnCount(folder);
                Check(At<GetDefaultColumnState>(adapter, 16)(adapter, (uint)columnIndex, out var columnState));
                if (columnState != 0x12) throw new Exception($"Unexpected custom column state 0x{columnState:X}.");
                Check(At<MapColumn>(adapter, 19)(adapter, (uint)columnIndex, keyPointer));
                var mapped = Marshal.PtrToStructure<ShellColumnManager.NativeKey>(keyPointer);
                if (mapped.FormatId != nativeKey.FormatId || mapped.PropertyId != nativeKey.PropertyId)
                    throw new Exception("Custom column did not map to its property key.");

                var child = ParseChild(adapter, "two.bin");
                try
                {
                    Marshal.GetNativeVariantForObject(0UL, variant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, child, keyPointer, variant));
                    var value = Marshal.GetObjectForNativeVariant(variant);
                    if (value is not ulong number || number != 2) throw new Exception($"Typed value mismatch: {value}.");
                }
                finally { ILFree(child); }
            }
            finally { Marshal.FreeCoTaskMem(keyPointer); Marshal.FreeCoTaskMem(variant); }

            Check(At<CreateViewObject>(adapter, 8)(adapter, parent, in ShellViewId, out view));
            host = new StreamViewHost(parent, []) { View = view };
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var createSettings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref createSettings, hostPointer, ref bounds, out _));
            createdView = true;
            Pump(800);
            CheckBoundary(parent, foreground, "DefView creation");
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<SetMode>(folderView, 35)(folderView, 4, 16)); // Details view.

            var columnIndexForView = GetColumnCount(folder);
            var columns = new DisplayColumnState[]
            {
                new(new(NameKeyFormat, 10), 220),
                new(new(property.FormatId, property.PropertyId), 150)
            };
            Check(ShellColumnManager.Apply(folderView, columns));
            Check(ShellColumnManager.Capture(folderView, out var captured));
            if (captured is null || !captured.SequenceEqual(columns)) throw new Exception("Custom column visibility/order did not persist.");
            var values = ReadSortedValues(folderView, adapter, nativeKey, 1);
            if (!values.SequenceEqual(new ulong[] { 2, 10, 10, 100 })) throw new Exception("Ascending sort was not numeric: " + string.Join(',', values));
            values = ReadSortedValues(folderView, adapter, nativeKey, -1);
            if (!values.SequenceEqual(new ulong[] { 100, 10, 10, 2 })) throw new Exception("Descending sort was not numeric: " + string.Join(',', values));

            Console.WriteLine($"native-static-shell-column: base-columns={columnIndexForView}; custom-column-visible=True; " +
                "typed-value=UInt64; ascending=[2,10,10,100]; descending=[100,10,10,2]; parent-visible=False; foreground-unchanged=True");
            CheckBoundary(parent, foreground, "static column sorting");
        }
        catch (Exception ex)
        {
            Console.WriteLine("native-static-shell-column failure: " + ex);
            result = 1;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (createdView && view != 0) Check(At<DestroyViewWindow>(view, 10)(view));
            }
            catch (Exception ex) { Console.WriteLine("native-static-shell-column cleanup: view: " + ex.Message); result = 1; }
            host?.Dispose();
            if (view != 0) Marshal.Release(view);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (adapter != 0) Marshal.Release(adapter);
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent))) result = 1;
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(fixture).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixture))
            {
                try { Directory.Delete(fixture, recursive: true); }
                catch (Exception ex) { Console.WriteLine("native-static-shell-column cleanup: fixture: " + ex.Message); result = 1; }
            }
            GC.KeepAlive(host);
        }
        if (GetForegroundWindow() != foreground) { Console.WriteLine("native-static-shell-column failure: foreground changed."); result = 1; }
        return result;
    }

    private static int GetColumnCount(nint adapter)
    {
        var details = Marshal.AllocCoTaskMem(272);
        try
        {
            var count = 0;
            for (uint index = 0; index < 64; index++)
            {
                Marshal.Copy(new byte[272], 0, details, 272);
                if (At<GetDetailsOf>(adapter, 18)(adapter, 0, index, details) < 0) break;
                if (ReadDetailsText(details).Length == 0) break;
                count++;
            }
            if (count == 0 || count == 64) throw new Exception("Could not bound native Shell columns.");
            return count;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static ulong[] ReadSortedValues(nint folderView, nint adapter, ShellColumnManager.NativeKey key, int direction)
    {
        var sortPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
        var setSortHr = 0;
        try
        {
            Marshal.StructureToPtr(new NativeSortColumn { Key = key, Direction = direction }, sortPointer, false);
            setSortHr = At<SetSortColumns>(folderView, 27)(folderView, sortPointer, 1);
            Check(setSortHr);
        }
        finally { Marshal.FreeCoTaskMem(sortPointer); }
        Pump(300);
        var sortCountHr = At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCount);
        Console.WriteLine($"native-static-shell-column: SetSortColumns hr=0x{setSortHr:X8}; GetSortColumnCount hr=0x{sortCountHr:X8}; count={sortCount}");
        if (sortCountHr >= 0 && sortCount > 0)
        {
            var currentSort = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>() * sortCount);
            try
            {
                var readSort = At<GetSortColumns>(folderView, 28)(folderView, currentSort, sortCount);
                if (readSort >= 0)
                {
                    var value = Marshal.PtrToStructure<NativeSortColumn>(currentSort);
                    Console.WriteLine($"native-static-shell-column: sort-readback={value.Key.FormatId}/{value.Key.PropertyId}; direction={value.Direction}");
                }
            }
            finally { Marshal.FreeCoTaskMem(currentSort); }
        }
        Console.WriteLine($"native-static-shell-column: CompareIDs total/custom/last-index={ReadNativeComparisonStats()}");
        Check(At<ItemCount>(folderView, 7)(folderView, SvgioAllView, out var count));
        if (count != 4) throw new Exception($"Expected four fixture items, got {count}.");
        var variant = Marshal.AllocCoTaskMem(24);
        var keyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
        try
        {
            Marshal.StructureToPtr(key, keyPointer, false);
            var values = new List<ulong>(count);
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItem>(folderView, 6)(folderView, index, out var pidl));
                try
                {
                    Marshal.GetNativeVariantForObject(0UL, variant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, pidl, keyPointer, variant));
                    var value = Marshal.GetObjectForNativeVariant(variant);
                    if (value is not ulong number) throw new Exception("DefView returned a non-UInt64 sort value.");
                    values.Add(number);
                }
                finally { ILFree(pidl); }
            }
            return values.ToArray();
        }
        finally { Marshal.FreeCoTaskMem(keyPointer); Marshal.FreeCoTaskMem(variant); }
    }

    private static nint ParseChild(nint adapter, string name)
    {
        var text = Marshal.StringToCoTaskMemUni(name);
        try { Check(At<ParseDisplayName>(adapter, 3)(adapter, 0, 0, text, 0, out var pidl, out _)); return pidl; }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private static string ReadDetailsText(nint details)
    {
        if (Marshal.ReadInt32(details, 8) != 0) throw new Exception("Expected STRRET_WSTR.");
        var pointer = Marshal.ReadIntPtr(details, 8 + (IntPtr.Size == 8 ? 8 : 4));
        try { return pointer == 0 ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { if (pointer != 0) Marshal.FreeCoTaskMem(pointer); }
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private static string ReadNativeComparisonStats()
    {
        var module = NativeLibrary.Load(FindAdapterLibrary());
        try
        {
            var custom = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellColumnComparisonCount"))();
            var total = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetAllShellColumnComparisonCount"))();
            var index = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetLastShellColumnComparisonIndex"))();
            return $"{total}/{custom}/{index}";
        }
        finally { NativeLibrary.Free(module); }
    }

    private static void CheckBoundary(nint parent, nint foreground, string stage)
    { if (IsWindowVisible(parent) || GetForegroundWindow() != foreground) throw new Exception($"{stage} crossed the hidden-window/foreground boundary."); }
    private static void Pump(int milliseconds)
    { var until = Environment.TickCount64 + milliseconds; while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); } }
    private static T At<T>(nint value, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct NativeSortColumn { public ShellColumnManager.NativeKey Key; public int Direction; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateAdapter(nint folder, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCompareCount();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDefaultColumnState(nint self, uint column, out uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsEx(nint self, nint item, nint key, nint variant);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MapColumn(nint self, uint column, nint key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ParseDisplayName(nint self, nint hwnd, nint bind, nint name, nint eaten, out nint item, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
