using System.Runtime.InteropServices;
using System.Windows;
using ComTypesDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ExplorerDirs.App;

internal static class ShellTabDropTarget
{
    private const uint FolderAttribute = 0x20000000;
    private const uint DesktopAbsoluteParsing = 0x80028000;
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    internal static bool HasShellLocationData(IDataObject data)
    {
        try
        {
            return data.GetDataPresent("Shell IDList Array", autoConvert: false) ||
                data.GetDataPresent(DataFormats.FileDrop, autoConvert: false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryGetDroppedFolder(IDataObject data, out string location)
    {
        location = string.Empty;
        bool fileDrop;
        bool shellItems;
        try
        {
            fileDrop = data.GetDataPresent(DataFormats.FileDrop, autoConvert: false);
            shellItems = data.GetDataPresent("Shell IDList Array", autoConvert: false);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
        if (fileDrop)
            return TryGetFileSystemFolder(data, out location);
        return shellItems && data is ComTypesDataObject nativeData &&
            TryGetShellItemFolder(nativeData, out location);
    }

    internal static bool TryGetShellItemFolder(ComTypesDataObject nativeData, out string location)
    {
        location = string.Empty;
        IShellItemArray? items = null;
        try
        {
            var interfaceId = ShellItemArrayId;
            if (SHCreateShellItemArrayFromDataObject(nativeData, ref interfaceId, out items) < 0 ||
                items is null || items.GetCount(out var count) < 0 || count != 1 ||
                items.GetItemAt(0, out var item) < 0 || item is null)
                return false;

            try
            {
                if (item.GetAttributes(FolderAttribute, out var attributes) < 0) return false;
                var nameResult = item.GetDisplayName(DesktopAbsoluteParsing, out var namePointer);
                if (nameResult < 0 || namePointer == 0)
                {
                    if (namePointer != 0) Marshal.FreeCoTaskMem(namePointer);
                    return false;
                }
                try
                {
                    var name = Marshal.PtrToStringUni(namePointer);
                    return name is not null && TryAcceptLocation(count, attributes, name, out location);
                }
                finally { Marshal.FreeCoTaskMem(namePointer); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
        }
    }

    private static bool TryGetFileSystemFolder(IDataObject data, out string location)
    {
        location = string.Empty;
        try
        {
            if (data.GetData(DataFormats.FileDrop, autoConvert: false) is not string[] { Length: 1 } paths ||
                !ShellLocationIdentity.TryNormalize(paths[0], out var path, out _) ||
                !ShellLocationIdentity.TryGetFolderPidl(path, out var pidl))
                return false;
            try { return ShellLocationIdentity.TryGetPersistedIdentity(pidl, out location, out _); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool TryAcceptLocation(uint itemCount, uint attributes, string parsingName,
        out string location)
    {
        location = string.Empty;
        if (itemCount != 1 || (attributes & FolderAttribute) == 0 ||
            !ShellLocationIdentity.TryNormalize(parsingName, out var normalized, out _))
            return false;
        location = normalized;
        return true;
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromDataObject(
        [MarshalAs(UnmanagedType.Interface)] ComTypesDataObject dataObject,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArray? shellItems);

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(nint bindContext, ref Guid handler, ref Guid iid, out nint value);
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid iid, out nint store);
        [PreserveSig] int GetPropertyDescriptionList(nint key, ref Guid iid, out nint list);
        [PreserveSig] int GetAttributes(uint flags, uint mask, out uint attributes);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
        [PreserveSig] int EnumItems(out nint enumerator);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint bindContext, ref Guid handler, ref Guid iid, out nint value);
        [PreserveSig] int GetParent(out nint parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
        [PreserveSig] int GetAttributes(uint requested, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
}
