using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal enum ShellDeletedItemNotificationResult
{
    InvalidPath,
    ItemsUnavailable,
    ItemUnavailable,
    ItemNotFound,
    ShellFailure,
    Notified
}

internal static class ShellDeletedItemNotification
{
    private const int MaxPath = 260;
    private const uint ShellFolderRemoved = 0x00000010;
    private const uint ShellFileRemoved = 0x00000004;
    private const uint PidlAndFlushNoWait = 0x00002000;
    private const uint FolderAttribute = 0x20000000;
    // A hidden 50k-item Shell view took about 260 ms to find one deleted path by scanning rows.
    private const int MaxIdentityScanViewItems = 512;
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    internal static bool ShouldNotifyPath(string path) =>
        Path.IsPathFullyQualified(path) && path.Length < MaxPath;

    internal static bool CanScanViewForDeletedFolder(int? viewItemCount, int? expectedViewItemCount, bool softRefresh = false) =>
        viewItemCount is > 0 and <= MaxIdentityScanViewItems &&
        (softRefresh || expectedViewItemCount is > 0 and <= MaxIdentityScanViewItems);

    internal static ShellDeletedItemNotificationResult NotifyDeletedItem(nint folderView, string path,
        Action<string>? trace = null, Guid? providerClassId = null)
    {
        if (folderView == 0 || !ShouldNotifyPath(path)) return ShellDeletedItemNotificationResult.InvalidPath;

        nint items = 0;
        try
        {
            var itemArrayId = ShellItemArrayId;
            var getItemsResult = Measure("get-items", trace,
                () => At<GetItems>(folderView, 8)(folderView, 2, ref itemArrayId, out items));
            if (getItemsResult < 0 || items == 0)
                return ShellDeletedItemNotificationResult.ItemsUnavailable;
            uint count = 0;
            if (Measure("array-count", trace, () => At<ArrayCount>(items, 7)(items, out count)) < 0)
                return ShellDeletedItemNotificationResult.ItemsUnavailable;

            for (uint index = 0; index < count; index++)
            {
                nint item = 0;
                var itemResult = Measure($"item-at index={index}", trace,
                    () => At<ItemAt>(items, 8)(items, index, out item));
                if (itemResult < 0 || item == 0)
                {
                    if (item != 0) Marshal.Release(item);
                    return ShellDeletedItemNotificationResult.ItemUnavailable;
                }
                try
                {
                    nint name = 0;
                    var nameResult = Measure($"display-name index={index}", trace,
                        () => At<DisplayName>(item, 5)(item, 0x80058000, out name));
                    if ((nameResult < 0 || name == 0) && providerClassId is not null)
                    {
                        if (name != 0) Marshal.FreeCoTaskMem(name);
                        name = 0;
                        nameResult = Measure($"provider-name index={index}", trace,
                            () => At<DisplayName>(item, 5)(item, 0x80028000, out name));
                    }
                    if (nameResult < 0 || name == 0)
                    {
                        if (name != 0) Marshal.FreeCoTaskMem(name);
                        return ShellDeletedItemNotificationResult.ItemUnavailable;
                    }
                    string itemPath;
                    try { itemPath = Marshal.PtrToStringUni(name) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(name); }
                    if (QdrShellPathCodec.TryGetProviderPath(itemPath, providerClassId, out var physicalPath))
                        itemPath = physicalPath;
                    if (!string.Equals(Path.GetFullPath(itemPath), path, StringComparison.OrdinalIgnoreCase)) continue;

                    uint attributes = 0;
                    var attributesResult = Measure($"attributes index={index}", trace,
                        () => At<Attributes>(item, 6)(item, FolderAttribute, out attributes));
                    if (attributesResult < 0)
                        return ShellDeletedItemNotificationResult.ItemUnavailable;
                    var removedEvent = (attributes & FolderAttribute) != 0 ? ShellFolderRemoved : ShellFileRemoved;
                    // Use the retained namespace identity; a deleted physical path
                    // cannot reconstruct its per-tab provider root and item metadata.
                    nint pidl = 0;
                    try
                    {
                        if (SHGetIDListFromObject(item, out pidl) < 0 || pidl == 0)
                            return ShellDeletedItemNotificationResult.ItemUnavailable;
                        SHChangeNotify(removedEvent, PidlAndFlushNoWait, pidl, IntPtr.Zero);
                        return ShellDeletedItemNotificationResult.Notified;
                    }
                    finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
                }
                finally { Marshal.Release(item); }
            }
            return ShellDeletedItemNotificationResult.ItemNotFound;
        }
        catch (COMException) { return ShellDeletedItemNotificationResult.ShellFailure; }
        catch (ArgumentException) { return ShellDeletedItemNotificationResult.InvalidPath; }
        catch (IOException) { return ShellDeletedItemNotificationResult.ShellFailure; }
        catch (NotSupportedException) { return ShellDeletedItemNotificationResult.InvalidPath; }
        finally
        {
            if (items != 0) Marshal.Release(items);
        }
    }

    private static int Measure(string stage, Action<string>? trace, Func<int> action)
    {
        if (trace is null) return action();
        var startedAt = Stopwatch.GetTimestamp();
        var result = action();
        trace($"{stage} hr=0x{result:X8} elapsed-ms={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}");
        return result;
    }

    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint scope, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ArrayCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int DisplayName(nint self, uint nameType, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Attributes(nint self, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHGetIDListFromObject(nint item, out nint pidl);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
