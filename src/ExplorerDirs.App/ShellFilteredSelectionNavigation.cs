using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellFilteredSelectionNavigation
{
    private const int WmKeyDown = 0x0100;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const uint SvgioSelection = 1;
    private const uint SvgioFlagViewOrder = 0x80000000;
    private const uint SvsiSelect = 0x0001;
    private const uint SvsiEnsureVisible = 0x0008;
    private const uint SvsiFocused = 0x0010;
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid EnumId = new("000214F2-0000-0000-C000-000000000046");

    internal static bool ShouldJump(int message, int key, int modifiers, bool filterActive,
        bool searchActive, bool fileContentFocused) =>
        message == WmKeyDown && modifiers == 0 && (key is VkUp or VkDown) &&
        filterActive && !searchActive && fileContentFocused;

    internal static int? ResolveAdjacentSelectedOrdinal(int selectedCount, int focusedOrdinal, int direction)
    {
        if (selectedCount < 2 || focusedOrdinal < 0 || focusedOrdinal >= selectedCount ||
            direction is not (-1 or 1)) return null;
        var target = focusedOrdinal + direction;
        return target >= 0 && target < selectedCount ? target : null;
    }

    internal static bool TryFocusAdjacentSelectedItem(object shellView, bool down, Func<bool> stillCurrent)
    {
        ArgumentNullException.ThrowIfNull(shellView);
        ArgumentNullException.ThrowIfNull(stillCurrent);
        nint unknown = 0, folderView = 0, shellViewPointer = 0, enumerator = 0, focusedItem = 0;
        var selectedItems = new List<nint>();
        try
        {
            if (!stillCurrent()) return false;
            unknown = Marshal.GetIUnknownForObject(shellView);
            if (Marshal.QueryInterface(unknown, in FolderViewId, out folderView) < 0 || folderView == 0 ||
                !stillCurrent()) return false;
            if (Marshal.QueryInterface(unknown, in ShellViewId, out shellViewPointer) < 0 || shellViewPointer == 0 ||
                !stillCurrent()) return false;

            var countResult = At<CountItems>(folderView, 7)(folderView, SvgioSelection, out var selectedCount);
            if (countResult < 0 || selectedCount < 2 || !stillCurrent()) return false;
            var focusResult = At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex);
            if (focusResult < 0 || focusedIndex < 0 || !stillCurrent()) return false;
            if (At<GetItem>(folderView, 6)(folderView, focusedIndex, out focusedItem) < 0 ||
                focusedItem == 0 || !stillCurrent()) return false;

            var enumId = EnumId;
            var itemsResult = At<Items>(folderView, 8)(folderView,
                SvgioSelection | SvgioFlagViewOrder, ref enumId, out enumerator);
            if (itemsResult < 0 || enumerator == 0 || !stillCurrent()) return false;

            var next = At<Next>(enumerator, 3);
            var focusedOrdinal = -1;
            for (;;)
            {
                var result = next(enumerator, 1, out var item, out var fetched);
                if (item != 0) selectedItems.Add(item);
                if (!stillCurrent()) return false;
                if (result == 1 && fetched == 0 && item == 0) break;
                Marshal.ThrowExceptionForHR(result);
                if (item == 0 || fetched != 1 || selectedItems.Count > selectedCount) return false;
                if (ILIsEqual(focusedItem, item)) focusedOrdinal = selectedItems.Count - 1;
            }

            if (selectedItems.Count != selectedCount || !stillCurrent()) return false;
            var targetOrdinal = ResolveAdjacentSelectedOrdinal(selectedItems.Count, focusedOrdinal, down ? 1 : -1);
            if (targetOrdinal is not { } target || !stillCurrent()) return false;

            var selectItem = Marshal.GetDelegateForFunctionPointer<SelectItem>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(shellViewPointer), 14 * IntPtr.Size));
            var selectResult = selectItem(shellViewPointer, selectedItems[target],
                SvsiSelect | SvsiEnsureVisible | SvsiFocused);
            return selectResult >= 0 && stillCurrent();
        }
        catch (COMException) { return false; }
        catch (InvalidComObjectException) { return false; }
        finally
        {
            if (focusedItem != 0) Marshal.FreeCoTaskMem(focusedItem);
            foreach (var item in selectedItems) Marshal.FreeCoTaskMem(item);
            if (enumerator != 0) Marshal.Release(enumerator);
            if (shellViewPointer != 0) Marshal.Release(shellViewPointer);
            if (folderView != 0) Marshal.Release(folderView);
            if (unknown != 0) Marshal.Release(unknown);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CountItems(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Items(nint self, uint scope, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Next(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, nint item, uint flags);
    [DllImport("shell32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ILIsEqual(nint first, nint second);
}
