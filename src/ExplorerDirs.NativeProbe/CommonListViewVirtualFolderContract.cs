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
    private const uint SigDnDesktopAbsoluteParsing = 0x80028000;
    private const uint ShgfiSysIconIndex = 0x00004000;
    private const uint ShgfiSmallIcon = 0x00000001;
    private const uint ShgfiPidl = 0x00000008;
    private const uint ShcontfFolders = 0x00000020;
    private const uint ShcontfNonFolders = 0x00000040;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ControlPanelId = new("26EE0668-A00A-44D7-9371-BEB064C98683");

    internal static int Run()
    {
        var foregroundBefore = GetForegroundWindow();
        var rootText = $"::{{{ControlPanelId:D}}}";
        nint parent = 0, listView = 0, rootPidl = 0, shellFolder = 0, enumerator = 0, systemImageList = 0;
        var rows = new List<ShellRow>();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(rootText, 0, out rootPidl,
                ShellFolder | ShellFileSystem, out var rootAttributes));
            if ((rootAttributes & ShellFolder) == 0)
                throw new InvalidOperationException("Control Panel must resolve as a Shell folder.");

            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(rootPidl, SigDnDesktopAbsoluteParsing, out var rootNamePointer));
            string rootParsingName;
            try { rootParsingName = Marshal.PtrToStringUni(rootNamePointer) ?? string.Empty; }
            finally { Marshal.FreeCoTaskMem(rootNamePointer); }
            if (string.IsNullOrWhiteSpace(rootParsingName))
                throw new InvalidOperationException("Control Panel did not expose an absolute parsing name.");

            Marshal.ThrowExceptionForHR(SHBindToObject(0, rootPidl, 0, in ShellFolderId, out shellFolder));
            Marshal.ThrowExceptionForHR(At<EnumObjects>(shellFolder, 4)(shellFolder, 0,
                ShcontfFolders | ShcontfNonFolders, out enumerator));

            while (true)
            {
                var hr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var relativePidl, out var fetched);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (fetched == 0) break;
                if (relativePidl == 0) throw new InvalidOperationException("Shell enumeration returned a null PIDL.");
                nint absolutePidl = 0;
                try
                {
                    absolutePidl = ILCombine(rootPidl, relativePidl);
                    if (absolutePidl == 0) throw new InvalidOperationException("Could not combine a virtual parent and child PIDL.");
                    Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl, 0, out var displayPointer));
                    string displayName;
                    try { displayName = Marshal.PtrToStringUni(displayPointer) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(displayPointer); }
                    Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl,
                        SigDnDesktopAbsoluteParsing, out var parsingPointer));
                    string parsingName;
                    try { parsingName = Marshal.PtrToStringUni(parsingPointer) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(parsingPointer); }
                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(parsingName))
                        throw new InvalidOperationException("A virtual Shell child has no display or parsing name.");

                    var childArray = Marshal.AllocHGlobal(IntPtr.Size);
                    uint attributes = ShellFolder | ShellFileSystem;
                    try
                    {
                        Marshal.WriteIntPtr(childArray, relativePidl);
                        Marshal.ThrowExceptionForHR(At<GetAttributesOf>(shellFolder, 9)(shellFolder, 1,
                            childArray, ref attributes));
                    }
                    finally { Marshal.FreeHGlobal(childArray); }

                    var iconList = SHGetFileInfo(absolutePidl, 0, out var fileInfo,
                        (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiPidl | ShgfiSysIconIndex | ShgfiSmallIcon);
                    if (iconList == 0 || fileInfo.ImageIndex < 0)
                        throw new InvalidOperationException("Shell did not return a system icon for an enumerated virtual child.");
                    if (systemImageList == 0) systemImageList = iconList;
                    else if (systemImageList != iconList)
                        throw new InvalidOperationException("Virtual rows did not share one Shell system image list.");

                    var retainedAbsolute = ILClone(absolutePidl);
                    var retainedRelative = ILClone(relativePidl);
                    if (retainedAbsolute == 0 || retainedRelative == 0)
                    {
                        if (retainedAbsolute != 0) ILFree(retainedAbsolute);
                        if (retainedRelative != 0) ILFree(retainedRelative);
                        throw new InvalidOperationException("Could not retain Shell row PIDLs.");
                    }
                    rows.Add(new ShellRow(rows.Count + 1, retainedAbsolute, retainedRelative, displayName,
                        parsingName, attributes, fileInfo.ImageIndex));
                }
                finally
                {
                    if (absolutePidl != 0) ILFree(absolutePidl);
                    ILFree(relativePidl);
                }
            }
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
                identityAndRowsMatch = item.RowId == rows[index].Id && item.Image == rows[index].ImageIndex &&
                    item.DisplayName == rows[index].DisplayName && item.ParsingName == rows[index].ParsingName &&
                    rows[index].AbsolutePidl != 0 && rows[index].RelativePidl != 0;
            }

            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            var hidden = !IsWindowVisible(parent) && !IsWindowVisible(listView);
            var hasVirtualRootIdentity = (rootAttributes & ShellFileSystem) == 0 || rootParsingName.StartsWith("::{", StringComparison.Ordinal);
            var virtualChildRows = rows.Count(row => (row.Attributes & ShellFileSystem) == 0);
            var imageListAttached = attachedImageList == systemImageList;
            var success = identityAndRowsMatch && imageListAttached && foregroundUnchanged && hidden &&
                hasVirtualRootIdentity && virtualChildRows > 0;
            Console.WriteLine($"common-listview-virtual-folder-contract: root-is-virtual={hasVirtualRootIdentity};" +
                $"children={rows.Count};filesystem-children={rows.Count(row => (row.Attributes & ShellFileSystem) != 0)};" +
                $"virtual-children={virtualChildRows};shell-image-list-attached={imageListAttached};" +
                $"pidl-row-identity={identityAndRowsMatch};" +
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
            foreach (var row in rows)
            {
                if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
                if (row.RelativePidl != 0) ILFree(row.RelativePidl);
            }
            if (enumerator != 0) Marshal.Release(enumerator);
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

    private static void InsertRow(nint listView, ShellRow row)
    {
        var displayPointer = Marshal.StringToHGlobalUni(row.DisplayName);
        var item = new ListViewItem { Mask = 0x0001 | 0x0002 | 0x0004,
            Item = (int)SendMessageW(listView, LvmGetItemCount, 0, 0),
            Text = displayPointer, TextMax = row.DisplayName.Length, Image = row.ImageIndex, Param = (nint)row.Id };
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumNext(nint self, uint count, out nint pidl, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint children, ref uint attributes);
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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon; public int ImageIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
    }
    private sealed record ShellRow(long Id, nint AbsolutePidl, nint RelativePidl, string DisplayName,
        string ParsingName, uint Attributes, int ImageIndex);

    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint SHGetFileInfo(nint item, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
}
