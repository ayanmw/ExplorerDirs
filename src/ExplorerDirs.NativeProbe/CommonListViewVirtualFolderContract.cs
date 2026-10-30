using ExplorerDirs.NativeProbe;
using System.Runtime.InteropServices;

// Isolated architecture probe: determine whether virtual Shell children can be
// represented by stable PIDL-backed rows in an app-owned ListView.
internal static class CommonListViewVirtualFolderContract
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint LvsReport = 0x0001;
    private const uint LvsShowSelectionAlways = 0x0008;
    private const uint ShellFolder = 0x20000000;
    private const uint ShellFileSystem = 0x40000000;
    private const int LvmInsertColumnW = 0x1061;
    private const int LvmInsertItemW = 0x104D;
    private const int LvmGetItemW = 0x104B;
    private const int LvmSetItemW = 0x104C;
    private const int LvmGetItemCount = 0x1004;
    private const int LvmSetImageList = 0x1003;
    private const int LvmGetImageList = 0x1002;
    private const int LvsilSmall = 1;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ControlPanelId = new("26EE0668-A00A-44D7-9371-BEB064C98683");

    internal static int Run()
    {
        var foregroundBefore = GetForegroundWindow();
        var rootText = $"::{{{ControlPanelId:D}}}";
        nint parent = 0, listView = 0, rootPidl = 0, shellFolder = 0, systemImageList = 0;
        var rows = new List<ShellListViewRow>();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(rootText, 0, out rootPidl,
                ShellFolder | ShellFileSystem, out var rootAttributes));
            if ((rootAttributes & ShellFolder) == 0)
                throw new InvalidOperationException("Control Panel must resolve as a Shell folder.");

            var rootParsingName = ShellListViewRowIdentity.GetAbsoluteParsingName(rootPidl);
            if (string.IsNullOrWhiteSpace(rootParsingName))
                throw new InvalidOperationException("Control Panel did not expose an absolute parsing name.");

            Marshal.ThrowExceptionForHR(SHBindToObject(0, rootPidl, 0, in ShellFolderId, out shellFolder));
            var enumeration = ShellListViewEnumerator.Enumerate(rootPidl, shellFolder);
            rows.AddRange(enumeration.Rows);
            systemImageList = enumeration.SystemImageList;
            if (rows.Count == 0) throw new InvalidOperationException("Control Panel returned no enumerable Shell children.");

            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 1 };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ListView initialization failed.");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs virtual Shell ListView probe", 0x00CF0000,
                0, 0, 420, 220, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Probe parent creation failed.");
            listView = CreateWindowExW(0, "SysListView32", string.Empty,
                WsChild | WsVisible | LvsReport | LvsShowSelectionAlways, 0, 0, 400, 200,
                parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ListView creation failed.");
            SendMessageW(listView, LvmSetImageList, LvsilSmall, systemImageList);
            var attachedImageList = SendMessageW(listView, LvmGetImageList, LvsilSmall, 0);
            AddColumn(listView, "Name", 200);
            AddColumn(listView, "Shell parsing name", 500);

            foreach (var row in rows)
            {
                InsertRow(listView, row);
            }

            var identityAndRowsMatch = (int)SendMessageW(listView, LvmGetItemCount, 0, 0) == rows.Count;
            for (var index = 0; index < rows.Count && identityAndRowsMatch; index++)
            {
                var item = ReadRow(listView, index);
                identityAndRowsMatch = item.RowId == rows[index].Id && item.Image == rows[index].SystemImageIndex &&
                    item.DisplayName == rows[index].DisplayName && item.ParsingName == rows[index].ParsingName &&
                    rows[index].AbsolutePidl != 0 && rows[index].RelativePidl != 0;
            }

            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            var hidden = !IsWindowVisible(parent) && !IsWindowVisible(listView);
            var hasVirtualRootIdentity = (rootAttributes & ShellFileSystem) == 0 || rootParsingName.StartsWith("::{", StringComparison.Ordinal);
            var virtualChildRows = rows.Count(row => (row.Attributes & ShellFileSystem) == 0);
            var virtualFolderRows = rows.Where(row =>
                (row.Attributes & ShellFolder) != 0 && (row.Attributes & ShellFileSystem) == 0).ToArray();
            var boundVirtualFolders = 0;
            var nestedVirtualRows = 0;
            foreach (var row in virtualFolderRows)
            {
                Marshal.ThrowExceptionForHR(SHBindToObject(0, row.AbsolutePidl, 0,
                    in ShellFolderId, out var childFolder));
                if (childFolder == 0) throw new InvalidOperationException("Shell returned a null virtual child folder.");
                try
                {
                    var childEnumeration = ShellListViewEnumerator.Enumerate(row.AbsolutePidl, childFolder);
                    boundVirtualFolders++;
                    nestedVirtualRows += childEnumeration.Rows.Count;
                    ShellListViewEnumerator.FreeRows(childEnumeration.Rows);
                }
                finally { Marshal.Release(childFolder); }
            }
            var imageListAttached = attachedImageList == systemImageList;
            var sharedRowModelCorrect = rows.All(row => row.FromShellEnumeration &&
                row.ParsingName.Length > 0 && row.SystemImageIndex >= 0 &&
                (((row.Attributes & ShellFileSystem) != 0) == (row.FileSystemPath is not null)) &&
                row.RecursiveBytes is null);
            var success = identityAndRowsMatch && imageListAttached && foregroundUnchanged && hidden &&
                hasVirtualRootIdentity && virtualChildRows > 0 && virtualFolderRows.Length > 0 &&
                boundVirtualFolders == virtualFolderRows.Length && sharedRowModelCorrect;
            Console.WriteLine($"common-listview-virtual-folder-contract: root-is-virtual={hasVirtualRootIdentity};" +
                $"children={rows.Count};filesystem-children={rows.Count(row => (row.Attributes & ShellFileSystem) != 0)};" +
                $"virtual-children={virtualChildRows};virtual-folders={virtualFolderRows.Length};" +
                $"bound-virtual-folders={boundVirtualFolders};nested-virtual-children={nestedVirtualRows};" +
                $"shell-image-list-attached={imageListAttached};" +
                $"pidl-row-identity={identityAndRowsMatch};shared-row-model={sharedRowModelCorrect};" +
                $"hidden={hidden};foreground-unchanged={foregroundUnchanged};passed={success}");
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"common-listview-virtual-folder-contract: {ex.GetType().Name};hr=0x{ex.HResult:X8};detail={ex.Message}");
            return 1;
        }
        finally
        {
            if (listView != 0 && IsWindow(listView)) DestroyWindow(listView);
            if (parent != 0 && IsWindow(parent)) DestroyWindow(parent);
            ShellListViewEnumerator.FreeRows(rows);
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
        }
    }

    private static void AddColumn(nint listView, string text, int width)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var column = new ListViewColumn { Mask = 0x0002 | 0x0004, Width = width,
            Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewColumn>());
        try
        {
            Marshal.StructureToPtr(column, memory, false);
            if (SendMessageW(listView, LvmInsertColumnW, 0, memory) == -1)
                throw new InvalidOperationException("ListView column insertion failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void InsertRow(nint listView, ShellListViewRow row)
    {
        var displayPointer = Marshal.StringToHGlobalUni(row.DisplayName);
        var item = new ListViewItem { Mask = 0x0001 | 0x0002 | 0x0004,
            Item = (int)SendMessageW(listView, LvmGetItemCount, 0, 0),
            Text = displayPointer, TextMax = row.DisplayName.Length, Image = row.SystemImageIndex, Param = (nint)row.Id };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            var index = (int)SendMessageW(listView, LvmInsertItemW, 0, memory);
            if (index < 0) throw new InvalidOperationException("ListView did not accept a Shell row.");
            SetSubItem(listView, index, 1, row.ParsingName);
        }
        finally
        {
            Marshal.FreeHGlobal(displayPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void SetSubItem(nint listView, int index, int subItem, string text)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 0x0001, Item = index, SubItem = subItem,
            Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmSetItemW, 0, memory) == 0)
                throw new InvalidOperationException("ListView did not accept a Shell parsing name.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static (long RowId, string DisplayName, string ParsingName, int Image) ReadRow(nint listView, int index)
    {
        var displayBuffer = Marshal.AllocHGlobal(4096);
        var parsingBuffer = Marshal.AllocHGlobal(32768);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            var display = new ListViewItem { Mask = 0x0001 | 0x0002 | 0x0004, Item = index,
                Text = displayBuffer, TextMax = 2048 };
            Marshal.StructureToPtr(display, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new InvalidOperationException("ListView could not read back a Shell row.");
            var readDisplay = Marshal.PtrToStructure<ListViewItem>(memory);
            var displayName = Marshal.PtrToStringUni(displayBuffer) ?? string.Empty;
            var parsing = new ListViewItem { Mask = 0x0001, Item = index, SubItem = 1,
                Text = parsingBuffer, TextMax = 16384 };
            Marshal.StructureToPtr(parsing, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new InvalidOperationException("ListView could not read back a virtual parsing name.");
            var parsingName = Marshal.PtrToStringUni(parsingBuffer) ?? string.Empty;
            return (readDisplay.Param.ToInt64(), displayName, parsingName, readDisplay.Image);
        }
        finally
        {
            Marshal.FreeHGlobal(displayBuffer);
            Marshal.FreeHGlobal(parsingBuffer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)] private struct InitCommonControls { public uint Size; public uint Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewColumn
    {
        public uint Mask; public int Format, Width; public nint Text; public int TextMax, SubItem, Image, Order;
        public int MinWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewItem
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask; public nint Text;
        public int TextMax, Image; public nint Param; public int Indent, GroupId, Columns;
        public nint ColumnIndices, ColumnFormat, Group;
    }
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
}
