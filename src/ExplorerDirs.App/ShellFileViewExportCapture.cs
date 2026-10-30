using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal interface IColumnValueCaptureSource
{
    bool IsCurrentColumnValueCapture(ShellFileViewExportCapture.ColumnValueCapture capture);
}

// Reads the current Shell view's displayed item order and visible columns.
// This deliberately consumes the native view snapshot instead of re-enumerating the folder.
internal static class ShellFileViewExportCapture
{
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private static readonly Guid ShellItem2Id = new("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid ShellItemImageFactoryId = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    private const uint AllViewInViewOrder = 0x80000002;
    private const uint AllViewItems = 2;
    private const uint FastPropertiesOnly = 8;
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int NotFound = unchecked((int)0x80070490);

    internal static FileViewExportSnapshot Capture(nint folderView, bool quickFilterActive = false)
    {
        var snapshot = CaptureCore(folderView, quickFilterActive, null);
        return new FileViewExportSnapshot(snapshot.Columns, snapshot.Rows);
    }

    internal static FileViewExportSnapshot CaptureIndexedView(nint folderView)
    {
        var visibleColumnsResult = ShellColumnManager.CaptureForExport(folderView, out var visibleColumns);
        Marshal.ThrowExceptionForHR(visibleColumnsResult);
        if (visibleColumns is null || visibleColumns.Count == 0)
            throw new InvalidDataException("当前文件视图没有可导出的可见列。");
        var snapshot = CaptureFromFolderViewIndices(folderView, visibleColumns, null);
        return new FileViewExportSnapshot(snapshot.Columns, snapshot.Rows);
    }

    internal static ColumnValueCapture BeginColumnValueCapture(nint folderView,
        PropertyKeyState propertyKey, bool quickFilterActive)
    {
        var visibleColumnsResult = ShellColumnManager.CaptureForExport(folderView, out var visibleColumns);
        Marshal.ThrowExceptionForHR(visibleColumnsResult);
        if (visibleColumns is null) throw new InvalidDataException("当前文件视图的列信息不可读取。");
        var columnIndex = -1;
        for (var index = 0; index < visibleColumns.Count; index++)
        {
            if (visibleColumns[index].Key != propertyKey) continue;
            columnIndex = index;
            break;
        }
        if (columnIndex < 0) throw new InvalidOperationException("所选列已不在当前文件视图中。");

        var column = visibleColumns[columnIndex];
        Marshal.AddRef(folderView);
        nint items = 0;
        nint folder = 0;
        try
        {
            var iid = ShellItemArrayId;
            var result = At<GetItems>(folderView, 8)(folderView, AllViewInViewOrder, ref iid, out items);
            if (result < 0)
            {
                if (items != 0)
                {
                    Marshal.Release(items);
                    items = 0;
                }
                if (!quickFilterActive || result is not (InvalidArgument or NotFound))
                    Marshal.ThrowExceptionForHR(result);

                var folderId = ShellFolderId;
                Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref folderId, out folder));
                if (folder == 0) throw new COMException("Shell view returned no parent folder.");
                Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, AllViewItems, out var indexCount));
                if (indexCount < 0) throw new InvalidDataException("当前文件视图返回了无效项目数。");
                return new ColumnValueCapture(folderView, 0, folder, indexCount, column);
            }

            if (items == 0) throw new COMException("Shell returned no item array for the current view.");
            Marshal.ThrowExceptionForHR(At<GetCount>(items, 7)(items, out var count));
            if (count > int.MaxValue) throw new InvalidDataException("当前文件视图项目数过多，无法筛选。");
            return new ColumnValueCapture(folderView, items, 0, (int)count, column);
        }
        catch
        {
            if (folder != 0) Marshal.Release(folder);
            if (items != 0) Marshal.Release(items);
            Marshal.Release(folderView);
            throw;
        }
    }

    internal sealed class ColumnValueCapture : IDisposable
    {
        private readonly nint _folderView;
        private readonly nint _items;
        private readonly nint _folder;
        private readonly ShellExportColumn _column;
        private readonly int _count;
        private HashSet<string>? _values = new(StringComparer.OrdinalIgnoreCase);
        private int _index;
        private bool _disposed;

        internal ColumnValueCapture(nint folderView, nint items, nint folder, int count,
            ShellExportColumn column)
        {
            _folderView = folderView;
            _items = items;
            _folder = folder;
            _count = count;
            _column = column;
        }

        internal IColumnValueCaptureSource? Source { get; set; }
        internal long Generation { get; set; }
        // Keep incremental Shell reads in one unsorted set. The UI detaches it before
        // sorting so this capture object does not retain the full set during menu creation.
        internal HashSet<string> TakeValues()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Interlocked.Exchange(ref _values, null) ??
                throw new InvalidOperationException("列值已从捕获对象移出。");
        }

        // Process a bounded number of Shell items per Dispatcher turn so a large
        // checklist does not monopolize the window's input and paint loop.
        internal bool ReadBatch(int maximumItems)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var values = _values ?? throw new InvalidOperationException("列值已从捕获对象移出。");
            var end = Math.Min(_count, _index + Math.Max(1, maximumItems));
            var oneColumn = new[] { _column };
            while (_index < end)
            {
                if (_items != 0)
                {
                    Marshal.ThrowExceptionForHR(At<GetItemAt>(_items, 8)(_items, (uint)_index, out var shellItem));
                    try { values.Add(ReadRow(shellItem, oneColumn)[0]); }
                    finally { Marshal.Release(shellItem); }
                }
                else
                {
                    Marshal.ThrowExceptionForHR(At<GetItem>(_folderView, 6)(_folderView, _index, out var childPidl));
                    if (childPidl == 0) throw new COMException("Shell view returned no item PIDL.");
                    try
                    {
                        var itemId = ShellItem2Id;
                        Marshal.ThrowExceptionForHR(SHCreateItemWithParent(IntPtr.Zero, _folder, childPidl,
                            ref itemId, out var shellItem));
                        if (shellItem == 0) throw new COMException("Shell returned no item for the current view index.");
                        try { values.Add(ReadRow(shellItem, oneColumn)[0]); }
                        finally { Marshal.Release(shellItem); }
                    }
                    finally { Marshal.FreeCoTaskMem(childPidl); }
                }
                _index++;
            }
            return _index >= _count;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _values = null;
            if (_folder != 0) Marshal.Release(_folder);
            if (_items != 0) Marshal.Release(_items);
            Marshal.Release(_folderView);
        }
    }

    internal static FileViewPrintSnapshot CaptureForPrint(nint folderView, bool quickFilterActive, uint mode, int iconSize)
    {
        var kind = FileViewPrintLayout.ForShellView(mode, iconSize);
        return CaptureCore(folderView, quickFilterActive, kind);
    }

    private static FileViewPrintSnapshot CaptureCore(nint folderView, bool quickFilterActive, FileViewPrintKind? printKind)
    {
        var visibleColumnsResult = ShellColumnManager.CaptureForExport(folderView, out var visibleColumns);
        Marshal.ThrowExceptionForHR(visibleColumnsResult);
        if (visibleColumns is null || visibleColumns.Count == 0)
            throw new InvalidDataException("当前文件视图没有可导出的可见列。");

        var iid = ShellItemArrayId;
        var getItemsResult = At<GetItems>(folderView, 8)(folderView, AllViewInViewOrder, ref iid, out var items);
        if (getItemsResult < 0)
        {
            if (items != 0) Marshal.Release(items);
            if (quickFilterActive && getItemsResult is InvalidArgument or NotFound)
                return CaptureFromFolderViewIndices(folderView, visibleColumns, printKind);
        }
        Marshal.ThrowExceptionForHR(getItemsResult);
        if (items == 0) throw new COMException("Shell returned no item array for the current view.");
        try
        {
            Marshal.ThrowExceptionForHR(At<GetCount>(items, 7)(items, out var count));
            if (count > int.MaxValue) throw new InvalidDataException("当前文件视图项目数过多，无法导出。");
            var rows = new List<IReadOnlyList<string>>((int)count);
            var images = printKind is not null && printKind != FileViewPrintKind.DetailsTable
                ? new List<BitmapSource?>((int)count)
                : null;
            for (uint index = 0; index < count; index++)
            {
                Marshal.ThrowExceptionForHR(At<GetItemAt>(items, 8)(items, index, out var shellItem));
                try
                {
                    rows.Add(ReadRow(shellItem, visibleColumns));
                    images?.Add(ReadImage(shellItem, printKind switch
                    {
                        FileViewPrintKind.ThumbnailGrid => 160,
                        FileViewPrintKind.TileGrid => 48,
                        _ => 24
                    }));
                }
                finally { Marshal.Release(shellItem); }
            }
            var columns = visibleColumns.Select(column => column.Name).ToArray();
            return printKind is { } kind
                ? new FileViewPrintSnapshot(columns, rows, kind, images ?? [])
                : new FileViewPrintSnapshot(columns, rows, FileViewPrintKind.DetailsTable, []);
        }
        finally { Marshal.Release(items); }
    }

    private static FileViewPrintSnapshot CaptureFromFolderViewIndices(nint folderView,
        IReadOnlyList<ShellExportColumn> visibleColumns, FileViewPrintKind? printKind)
    {
        var folderId = ShellFolderId;
        Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref folderId, out var folder));
        if (folder == 0) throw new COMException("Shell view returned no parent folder.");
        try
        {
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, AllViewItems, out var count));
            if (count < 0) throw new InvalidDataException("当前文件视图返回了无效项目数。");
            var rows = new List<IReadOnlyList<string>>(count);
            var images = printKind is not null && printKind != FileViewPrintKind.DetailsTable
                ? new List<BitmapSource?>(count)
                : null;
            for (var index = 0; index < count; index++)
            {
                Marshal.ThrowExceptionForHR(At<GetItem>(folderView, 6)(folderView, index, out var childPidl));
                if (childPidl == 0) throw new COMException("Shell view returned no item PIDL.");
                try
                {
                    var itemId = ShellItem2Id;
                    Marshal.ThrowExceptionForHR(SHCreateItemWithParent(IntPtr.Zero, folder, childPidl,
                        ref itemId, out var shellItem));
                    if (shellItem == 0) throw new COMException("Shell returned no item for the current view index.");
                    try
                    {
                        rows.Add(ReadRow(shellItem, visibleColumns));
                        images?.Add(ReadImage(shellItem, printKind switch
                        {
                            FileViewPrintKind.ThumbnailGrid => 160,
                            FileViewPrintKind.TileGrid => 48,
                            _ => 24
                        }));
                    }
                    finally { Marshal.Release(shellItem); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            var columns = visibleColumns.Select(column => column.Name).ToArray();
            return printKind is { } kind
                ? new FileViewPrintSnapshot(columns, rows, kind, images ?? [])
                : new FileViewPrintSnapshot(columns, rows, FileViewPrintKind.DetailsTable, []);
        }
        finally { Marshal.Release(folder); }
    }

    private static BitmapSource? ReadImage(nint shellItem, int size)
    {
        var iid = ShellItemImageFactoryId;
        if (Marshal.QueryInterface(shellItem, in iid, out var factory) < 0 || factory == 0) return null;
        nint bitmap = 0;
        try
        {
            var nativeSize = new NativeSize { Width = size, Height = size };
            if (At<GetImage>(factory, 3)(factory, nativeSize, 0, out bitmap) < 0 || bitmap == 0) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            Marshal.Release(factory);
        }
    }

    private static IReadOnlyList<string> ReadRow(nint shellItem, IReadOnlyList<ShellExportColumn> columns)
    {
        var iid = ShellItem2Id;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellItem, in iid, out var shellItem2));
        if (shellItem2 == 0) throw new COMException("Shell item does not expose IShellItem2.");
        try
        {
            var storeId = PropertyStoreId;
            Marshal.ThrowExceptionForHR(At<GetPropertyStore>(shellItem2, 8)(shellItem2, FastPropertiesOnly, ref storeId, out var store));
            if (store == 0) throw new COMException("Shell item returned no property store.");
            try
            {
                var values = new string[columns.Count];
                for (var index = 0; index < columns.Count; index++)
                    values[index] = ReadDisplayValue(store, columns[index].Key);
                return values;
            }
            finally { Marshal.Release(store); }
        }
        finally { Marshal.Release(shellItem2); }
    }

    private static string ReadDisplayValue(nint store, PropertyKeyState propertyKey)
    {
        var key = new NativePropertyKey { FormatId = propertyKey.FormatId, PropertyId = propertyKey.PropertyId };
        var result = At<GetValue>(store, 5)(store, ref key, out var value);
        nint display = 0;
        try
        {
            if (result < 0) return string.Empty;
            result = PSFormatForDisplayAlloc(in key, ref value, 0, out display);
            return result >= 0 && display != 0 ? Marshal.PtrToStringUni(display) ?? string.Empty : string.Empty;
        }
        finally
        {
            PropVariantClear(ref value);
            if (display != 0) Marshal.FreeCoTaskMem(display);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        private ushort Reserved1, Reserved2, Reserved3;
        private PropVariantData Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantData { public nint First; public nint Second; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItem(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetPropertyStore(nint self, uint flags, ref Guid iid, out nint store);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetValue(nint self, ref NativePropertyKey key, out PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetImage(nint self, NativeSize size, uint flags, out nint bitmap);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PSFormatForDisplayAlloc(in NativePropertyKey key, ref PropVariant value,
        uint formatFlags, out nint display);
    [DllImport("shell32.dll")]
    private static extern int SHCreateItemWithParent(nint parentPidl, nint parentFolder, nint childPidl,
        ref Guid iid, out nint item);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}
