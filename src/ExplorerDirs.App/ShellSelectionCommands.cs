using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal enum ShellSelectionCommand { All, None, Invert }

internal static class ShellSelectionCommands
{
    // view is a borrowed IFolderView2 pointer. Capture identities before changing
    // selection; never select by indices that directory updates can reorder.
    internal static bool Apply(nint view, ShellSelectionCommand command, Func<bool> stillCurrent, Func<bool> stillSameView)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        if (!stillCurrent()) return false;
        var countItems = At<Count>(view, 7);
        var scope = command == ShellSelectionCommand.None ? 1u : 2u; // SVGIO_SELECTION / ALLVIEW.
        Check(countItems(view, scope, out var count)); // No filesystem enumeration.
        if (count < 0) throw new InvalidOperationException("Shell returned an invalid item count.");
        if (count == 0) return stillCurrent();
        var owned = new List<nint>();
        nint enumeration = 0;
        try
        {
            var enumId = new Guid("000214F2-0000-0000-C000-000000000046");
            Check(At<Items>(view, 8)(view, scope, ref enumId, out enumeration));
            var next = At<Next>(enumeration, 3);
            var state = At<SelectionState>(view, 33);
            var targets = new List<nint>();
            var deselect = new List<nint>();
            for (;;)
            {
                var hr = next(enumeration, 1, out var pidl, out var fetched);
                if (pidl != 0) owned.Add(pidl);
                if (!stillCurrent()) return false;
                if (hr == 1 && fetched == 0 && pidl == 0) break; // S_FALSE: exhausted.
                Check(hr);
                if (pidl == 0 || fetched != 1 || owned.Count > count) return false;
                if (command == ShellSelectionCommand.All) targets.Add(pidl);
                else if (command == ShellSelectionCommand.None) deselect.Add(pidl);
                else
                {
                    Check(state(view, pidl, out var flags));
                    if ((flags & 1) == 0) targets.Add(pidl); // SVSI_SELECT, not check/focus flags.
                    else deselect.Add(pidl);
                }
            }
            Check(countItems(view, scope, out var afterCount));
            if (owned.Count != count || afterCount != count || !stillCurrent()) return false;
            var selectedItems = targets.ToArray();
            var deselectedItems = deselect.ToArray();
            if (!stillCurrent()) return false;
            var select = At<SelectMany>(view, 16);
            // Native probe: DESELECTOTHERS in a multi-item batch leaves only the
            // last item selected. Apply separate disjoint select/deselect batches.
            if (deselectedItems.Length != 0)
                Check(select(view, (uint)deselectedItems.Length, deselectedItems, 0, 0x40000000));
            // Our first batch changes the selection version. Between mutations
            // validate the view identity, not that old selection version.
            if (!stillSameView()) return false;
            if (selectedItems.Length != 0)
                Check(select(view, (uint)selectedItems.Length, selectedItems, 0, 0x40000001));
            return true;
        }
        finally
        {
            foreach (var pidl in owned) Marshal.FreeCoTaskMem(pidl);
            if (enumeration != 0) Marshal.Release(enumeration);
        }
    }

    private static void Check(int hr)
    {
        if (hr != 0) throw new COMException("Shell selection operation did not complete.", hr);
    }
    // SVSI_FOCUSED alone deselects the item; SELECT | FOCUSED selects it. Retain
    // the actual selected bit, and use its PIDL so a reorder cannot retarget it.
    internal static bool RestoreFocusedItem(nint view, bool takeFocus = true)
    {
        var focusedResult = At<Focused>(view, 10)(view, out var focusedIndex);
        var markedResult = At<Focused>(view, 9)(view, out var markedIndex);
        if (At<Count>(view, 7)(view, 0, out var itemCount) < 0) return false;
        var index = ShellFocusRestoreGate.ResolveFocusItemIndex(
            focusedResult >= 0 ? focusedIndex : -1,
            markedResult >= 0 ? markedIndex : -1,
            itemCount);
        if (index < 0) return false;
        nint pidl = 0, shell = 0;
        try
        {
            Check(At<Item>(view, 6)(view, index, out pidl));
            Check(At<SelectionState>(view, 33)(view, pidl, out var state));
            // A view-mode change normally keeps the focused PIDL. Re-selecting that
            // same item after UIActivate can emit an unnecessary selection/redraw cycle.
            if (focusedResult < 0 || focusedIndex != index)
            {
                var id = new Guid("000214E3-0000-0000-C000-000000000046");
                Check(Marshal.QueryInterface(view, in id, out shell));
                Check(At<SelectOne>(shell, 14)(shell, pidl,
                    0x10 | (state & 1) | (takeFocus ? 0u : 0x40000000u)));
            }
            return (state & 1) != 0;
        }
        finally
        {
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (shell != 0) Marshal.Release(shell);
        }
    }
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Items(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Next(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectionState(nint self, nint pidl, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Focused(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Item(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectOne(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectMany(nint self, uint count,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] items, nint positions, uint flags);
}
