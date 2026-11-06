using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace ExplorerDirs.App;

internal static class ShellGroupedViewReadiness
{
    // GroupView enabled alone is insufficient: DefView can expose rows with
    // I_GROUPIDNONE (-2) before the saved grouping has actually materialized.
    internal static async Task<int[]?> ReadMaterializedGroupsAsync(nint window, int expectedCount, Func<bool> current, Action<string>? trace = null)
    {
        if (expectedCount <= 0 || !current()) { trace?.Invoke("request-retired"); return null; }
        // Use the host's retained HWND. IShellView.GetWindow can reject call-outs
        // from a Shell synchronous-input recovery callback (RPC_E_CANTCALLOUT_ININPUTSYNCCALL).
        if (window == 0 || !IsWindow(window)) return null;
        nint item = 0;
        try
        {
            nint list = 0;
            EnumChildWindows(window, (child, _) =>
            {
                var name = new StringBuilder(32);
                GetClassNameW(child, name, name.Capacity);
                if (name.ToString() != "SysListView32") return true;
                list = child;
                return false;
            }, 0);
            bool SameList() => current() && list != 0 && IsWindow(list) && IsChild(window, list);
            if (!SameList()) { trace?.Invoke("list-unavailable"); return null; }
            var grouped = SendMessageW(list, 0x10AF, 0, 0);
            var actualCount = SendMessageW(list, 0x1004, 0, 0);
            trace?.Invoke($"group-enabled={grouped};count={actualCount};expected={expectedCount}");
            if (grouped != 1 || actualCount != expectedCount) return null;
            item = Marshal.AllocHGlobal(Marshal.SizeOf<ListItem>());
            var groups = new int[expectedCount];
            for (var index = 0; index < expectedCount; index++)
            {
                if (!SameList()) return null;
                Marshal.StructureToPtr(new ListItem { Mask = 0x100, Item = index, GroupId = -2 }, item, false);
                if (SendMessageW(list, 0x104B, 0, item) == 0 || !SameList()) return null;
                groups[index] = Marshal.PtrToStructure<ListItem>(item).GroupId;
                if (groups[index] < 0) { trace?.Invoke($"unassigned-row={index};group={groups[index]}"); return null; }
                if ((index + 1) % ShellFileViewExportCapture.ReadinessBatchSize == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
            return SameList() && SendMessageW(list, 0x10AF, 0, 0) == 1 &&
                SendMessageW(list, 0x1004, 0, 0) == expectedCount ? groups : null;
        }
        finally
        {
            if (item != 0) Marshal.FreeHGlobal(item);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ListItem
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask;
        public nint Text; public int TextCapacity, Image; public nint Parameter;
        public int Indent, GroupId; public uint Columns; public nint ColumnIndices, ColumnFormats; public int Group;
    }
    private delegate bool EnumWindow(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
