using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

// IShellView.Refresh can rebuild the native item list and clear its selection.
// Keep relative PIDLs, not indices or paths, so restoration targets the same
// Shell items even when the refreshed view is sorted or grouped differently.
internal sealed class ShellViewSelectionSnapshot : IDisposable
{
    private static readonly Guid EnumId = new("000214F2-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private const nint CompareCanonicalOnly = 0x10000000;
    private readonly List<nint> _selected;
    private readonly nint _focused;
    private readonly bool _focusedWasSelected;
    internal int SelectedCount => _selected.Count;
    internal bool HasFocusedItem => _focused != 0;
    internal bool FocusedItemWasSelected => _focusedWasSelected;

    private ShellViewSelectionSnapshot(List<nint> selected, nint focused, bool focusedWasSelected)
    {
        _selected = selected;
        _focused = focused;
        _focusedWasSelected = focusedWasSelected;
    }

    internal ShellViewSelectionSnapshot Clone()
    {
        var selected = new List<nint>(_selected.Count);
        nint focused = 0;
        try
        {
            foreach (var item in _selected)
            {
                var clone = ILClone(item);
                if (clone == 0) throw new COMException("Could not clone a selected Shell item identity.");
                selected.Add(clone);
            }
            if (_focused != 0)
            {
                focused = ILClone(_focused);
                if (focused == 0) throw new COMException("Could not clone the focused Shell item identity.");
            }
            return new ShellViewSelectionSnapshot(selected, focused, _focusedWasSelected);
        }
        catch
        {
            foreach (var item in selected) Marshal.FreeCoTaskMem(item);
            if (focused != 0) Marshal.FreeCoTaskMem(focused);
            throw;
        }
    }

    internal bool WasUnselectedFocusedItemAddedTo(ShellViewSelectionSnapshot other) =>
        _focused != 0 && !_focusedWasSelected && other._selected.Any(item => ILIsEqual(_focused, item));

    internal static ShellViewSelectionSnapshot? Capture(nint folderView)
    {
        var selected = new List<nint>();
        nint enumeration = 0;
        nint focused = 0;
        try
        {
            var enumId = EnumId;
            var itemsResult = At<Items>(folderView, 8)(folderView, 1, ref enumId, out enumeration);
            if (itemsResult >= 0)
            {
                if (enumeration == 0) throw new COMException("Shell returned no selection enumerator.");
                var next = At<Next>(enumeration, 3);
                for (;;)
                {
                    var result = next(enumeration, 1, out var item, out var fetched);
                    try
                    {
                        if (result == 1 && fetched == 0 && item == 0) break;
                        Marshal.ThrowExceptionForHR(result);
                        if (item == 0 || fetched != 1) throw new COMException("Shell returned an invalid selection item.");
                        var clone = ILClone(item);
                        if (clone == 0) throw new COMException("Could not retain a selected Shell item identity.");
                        selected.Add(clone);
                    }
                    finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
                }
            }
            else if (itemsResult != unchecked((int)0x80070490))
            {
                Marshal.ThrowExceptionForHR(itemsResult);
            }

            var focusedResult = At<FocusedItem>(folderView, 10)(folderView, out var index);
            if (focusedResult >= 0 && index >= 0)
            {
                Marshal.ThrowExceptionForHR(At<Item>(folderView, 6)(folderView, index, out var item));
                try
                {
                    focused = ILClone(item);
                    if (focused == 0) throw new COMException("Could not retain the focused Shell item identity.");
                    Marshal.ThrowExceptionForHR(At<SelectionState>(folderView, 33)(folderView, item, out var state));
                    return new ShellViewSelectionSnapshot(selected, focused, (state & 1) != 0);
                }
                finally { Marshal.FreeCoTaskMem(item); }
            }

            return new ShellViewSelectionSnapshot(selected, 0, false);
        }
        catch (COMException)
        {
            foreach (var item in selected) Marshal.FreeCoTaskMem(item);
            if (focused != 0) Marshal.FreeCoTaskMem(focused);
            return null;
        }
        finally { if (enumeration != 0) Marshal.Release(enumeration); }
    }

    // A successful position lookup proves this saved relative PIDL is currently in the view.
    // A failed lookup is ambiguous, so callers use it only as a positive readiness signal.
    // The caller may reuse positive results only until the Shell selection generation changes.
    internal string? ReadTargetReadiness(nint shellView, string? knownReady, out int queryCount)
    {
        queryCount = 0;
        if (Marshal.QueryInterface(shellView, in FolderViewId, out var folderView) < 0 || folderView == 0)
            return null;
        try
        {
            var getItemPosition = At<GetItemPosition>(folderView, 11);
            var readiness = new StringBuilder(_selected.Count);
            var canReuseKnownReady = knownReady?.Length == _selected.Count;
            for (var index = 0; index < _selected.Count; index++)
            {
                if (canReuseKnownReady && knownReady![index] == '1')
                {
                    readiness.Append('1');
                    continue;
                }
                queryCount++;
                readiness.Append(getItemPosition(folderView, _selected[index], out _) >= 0 ? '1' : '0');
            }
            return readiness.ToString();
        }
        catch (COMException)
        {
            return null;
        }
        finally { Marshal.Release(folderView); }
    }

    internal (bool SelectionRestored, bool FocusRestored) Restore(nint shellView, Action<string>? trace = null,
        bool allowSelectedFocusFallback = true, bool restoreSelection = true, bool restoreFocus = true)
    {
        if (Marshal.QueryInterface(shellView, in FolderViewId, out var folderView) < 0 || folderView == 0)
            return (false, false);
        var folderId = ShellFolderId;
        var folderResult = At<GetFolder>(folderView, 5)(folderView, ref folderId, out var shellFolder);
        if (folderResult < 0 || shellFolder == 0)
        {
            if (shellFolder != 0) Marshal.Release(shellFolder);
            Marshal.Release(folderView);
            return (false, false);
        }

        var selectItem = At<SelectItem>(shellView, 14);
        // Stable deferred focus retries can reuse the selection restored by the preceding Shell call.
        var hasSelection = _selected.Count > 0;
        if (hasSelection && restoreSelection)
        {
            var selectionCheckStartedAt = StartTiming(trace);
            var selectionMatches = CurrentSelectionMatches(folderView, shellFolder, trace);
            trace?.Invoke($"selection-current matched={selectionMatches},elapsed-ms={ElapsedMilliseconds(selectionCheckStartedAt):F1}");
            if (!selectionMatches)
            {
                hasSelection = TryRestoreSelectionBatch(folderView, shellView, shellFolder, out var batchHasSelection, trace) && batchHasSelection;
            }
        }
        trace?.Invoke($"selection-batch selected={_selected.Count},restored={hasSelection}");
        try
        {
            if (_selected.Count == 0)
            {
                hasSelection = true;
            }
            else if (!hasSelection)
            {
                foreach (var item in _selected)
                {
                    var flags = 0x1u | 0x40000000u; // select without taking OS focus
                    if (!hasSelection) flags |= 0x4; // deselect others on the first surviving item
                    var selectionItemStartedAt = StartTiming(trace);
                    var result = selectItem(shellView, item, flags);
                    var itemSelected = result >= 0 && IsSelectedItem(folderView, shellFolder, item);
                    trace?.Invoke($"selection-item-direct hr=0x{result:X8},selected={itemSelected},elapsed-ms={ElapsedMilliseconds(selectionItemStartedAt):F1}");
                    if (itemSelected)
                    {
                        hasSelection = true;
                    }
                }

                // One successful SelectItem does not prove the entire saved multi-selection returned.
                hasSelection = CurrentSelectionMatches(folderView, shellFolder, trace);
                trace?.Invoke($"selection-direct verified={hasSelection}");
            }

            var focusRestored = _focused == 0;
            // A stale selection member must not prevent restoring a different, surviving focused item.
            if (_focused != 0 && restoreFocus)
            {
                // Refresh can preserve item focus even when it rebuilds selection state.
                // Avoid sending a redundant native focus command during that repaint.
                var focusedItemCheckStartedAt = StartTiming(trace);
                focusRestored = IsFocused(folderView, shellFolder, _focused);
                trace?.Invoke($"focused-item-current matched={focusRestored},elapsed-ms={ElapsedMilliseconds(focusedItemCheckStartedAt):F1}");
                if (!focusRestored)
                {
                    // SelectItem validates the PIDL against this view; a separate membership scan
                    // enumerates every Shell item and stalls large directories before focus restoration.
                    trace?.Invoke("focused-item-lookup=direct-select");
                    var flags = 0x10u | 0x40000000u; // restore the item-level focus, not HWND focus
                    if (_focusedWasSelected) flags |= 0x1;
                    var focusedItemSelectStartedAt = StartTiming(trace);
                    var result = selectItem(shellView, _focused, flags);
                    var selectElapsed = ElapsedMilliseconds(focusedItemSelectStartedAt);
                    var focusedItemVerifyStartedAt = StartTiming(trace);
                    focusRestored = IsFocused(folderView, shellFolder, _focused);
                    trace?.Invoke($"focused-item-select hr=0x{result:X8},select-ms={selectElapsed:F1},verified={focusRestored},verify-ms={ElapsedMilliseconds(focusedItemVerifyStartedAt):F1}");
                }
            }
            else if (_focused != 0 && !restoreFocus)
            {
                trace?.Invoke("focused-item-restore-deferred");
            }
            // During asynchronous Shell enumeration, the saved focus PIDL may be temporarily absent;
            // defer this fallback so focus does not jump to a selected item before that PIDL returns.
            if (!focusRestored && allowSelectedFocusFallback && hasSelection)
            {
                foreach (var item in _selected)
                {
                    var selectedFocusStartedAt = StartTiming(trace);
                    var result = selectItem(shellView, item, 0x1u | 0x10u | 0x40000000u);
                    if (result >= 0)
                        focusRestored = IsFocused(folderView, shellFolder, item);
                    trace?.Invoke($"selected-item-focus-direct hr=0x{result:X8},verified={focusRestored},elapsed-ms={ElapsedMilliseconds(selectedFocusStartedAt):F1}");
                    if (focusRestored) break;
                }
            }
            trace?.Invoke($"restore-result selection={hasSelection},focus={focusRestored}");
            return (hasSelection, focusRestored);
        }
        finally
        {
            Marshal.Release(shellFolder);
            Marshal.Release(folderView);
        }
    }

    private bool TryRestoreSelectionBatch(nint folderView, nint shellView, nint shellFolder,
        out bool hasSelection, Action<string>? trace)
    {
        hasSelection = false;
        var selectItem = At<SelectItem>(shellView, 14);
        bool TryApplyBatch()
        {
            nint items = 0;
            try
            {
                // SVSI_DESELECTOTHERS is applied per PIDL and collapses a multi-item batch.
                // Add the snapshot first; if old extra selection remains, the verified fallback
                // seeds one saved item with DESELECTOTHERS and then adds the rest.
                items = Marshal.AllocHGlobal(checked(_selected.Count * nint.Size));
                for (var index = 0; index < _selected.Count; index++)
                    Marshal.WriteIntPtr(items, index * nint.Size, _selected[index]);
                var batchRestoreStartedAt = StartTiming(trace);
                var result = At<SelectAndPositionItems>(folderView, 16)(folderView, (uint)_selected.Count, items,
                    IntPtr.Zero, 0x1u | 0x40000000u) >= 0;
                trace?.Invoke($"selection-batch-api count={_selected.Count},succeeded={result},elapsed-ms={ElapsedMilliseconds(batchRestoreStartedAt):F1}");
                return result;
            }
            catch (COMException)
            {
                return false;
            }
            finally { if (items != 0) Marshal.FreeHGlobal(items); }
        }

        bool RestoreReplacementFallback()
        {
            var verified = ShellSelectionBatchRestore.TryRestoreReplacementFallback(
                () => ClearCurrentSelection(folderView, trace), TryApplyBatch,
                () => CurrentSelectionMatches(folderView, shellFolder, trace));
            trace?.Invoke($"selection-replacement-fallback verified={verified}");
            return verified;
        }

        hasSelection = ShellSelectionBatchRestore.TryRestore(TryApplyBatch,
            () => CurrentSelectionMatches(folderView, shellFolder, trace),
            RestoreReplacementFallback, trace);
        return hasSelection;
    }

    private static bool ClearCurrentSelection(nint folderView, Action<string>? trace)
    {
        nint enumeration = 0;
        var selected = new List<nint>();
        nint buffer = 0;
        try
        {
            var enumId = EnumId;
            var result = At<Items>(folderView, 8)(folderView, 1, ref enumId, out enumeration);
            if (result == 1 || result == unchecked((int)0x80070490))
            {
                trace?.Invoke("selection-clear complete count=0");
                return true;
            }
            if (result < 0 || enumeration == 0)
            {
                trace?.Invoke($"selection-clear enumerate failed hr=0x{result:X8},enumerator=0x{enumeration:X}");
                return false;
            }

            var next = At<Next>(enumeration, 3);
            for (;;)
            {
                result = next(enumeration, 1, out var item, out var fetched);
                if (result == 1 && fetched == 0 && item == 0) break;
                if (result < 0 || item == 0 || fetched != 1)
                {
                    if (item != 0) Marshal.FreeCoTaskMem(item);
                    trace?.Invoke($"selection-clear enumerate failed hr=0x{result:X8},fetched={fetched}");
                    return false;
                }
                selected.Add(item);
            }

            if (selected.Count == 0)
            {
                trace?.Invoke("selection-clear complete count=0");
                return true;
            }

            buffer = Marshal.AllocHGlobal(checked(selected.Count * nint.Size));
            for (var index = 0; index < selected.Count; index++)
                Marshal.WriteIntPtr(buffer, index * nint.Size, selected[index]);
            result = At<SelectAndPositionItems>(folderView, 16)(folderView, (uint)selected.Count, buffer,
                IntPtr.Zero, 0x40000000u); // SVSI_DESELECT (0) | SVSI_NOTAKEFOCUS
            trace?.Invoke($"selection-clear batch count={selected.Count},hr=0x{result:X8}");
            if (result < 0) return false;

            result = At<ItemCount>(folderView, 7)(folderView, 1, out var remaining);
            if (result < 0 || remaining != 0)
            {
                trace?.Invoke($"selection-clear failed verify-hr=0x{result:X8},remaining={remaining}");
                return false;
            }

            trace?.Invoke($"selection-clear complete count={selected.Count},strategy=batch-deselect");
            return true;
        }
        finally
        {
            if (enumeration != 0) Marshal.Release(enumeration);
            foreach (var item in selected) Marshal.FreeCoTaskMem(item);
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
        }
    }

    private bool CurrentSelectionMatches(nint folderView, nint shellFolder, Action<string>? trace)
    {
        nint enumeration = 0;
        try
        {
            var enumId = EnumId;
            var enumerationStartedAt = StartTiming(trace);
            var result = At<Items>(folderView, 8)(folderView, 1, ref enumId, out enumeration);
            trace?.Invoke($"selection-current-enumerate hr=0x{result:X8},elapsed-ms={ElapsedMilliseconds(enumerationStartedAt):F1}");
            if (result < 0 || enumeration == 0) return false;

            var index = 0;
            var next = At<Next>(enumeration, 3);
            for (;;)
            {
                var nextStartedAt = StartTiming(trace);
                result = next(enumeration, 1, out var item, out var fetched);
                trace?.Invoke($"selection-current-next index={index},hr=0x{result:X8},fetched={fetched},elapsed-ms={ElapsedMilliseconds(nextStartedAt):F1}");
                try
                {
                    if (result == 1 && fetched == 0 && item == 0)
                        return index == _selected.Count;
                    if (result < 0 || item == 0 || fetched != 1 || index >= _selected.Count)
                        return false;
                    var compareStartedAt = StartTiming(trace);
                    var matches = IsSameItem(shellFolder, item, _selected[index], out var identityMethod);
                    trace?.Invoke($"selection-current-identity index={index},matched={matches},method={identityMethod},elapsed-ms={ElapsedMilliseconds(compareStartedAt):F1}");
                    if (!matches) return false;
                    index++;
                }
                finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
            }
        }
        catch (COMException)
        {
            return false;
        }
        finally { if (enumeration != 0) Marshal.Release(enumeration); }
    }

    private static bool IsFocused(nint folderView, nint shellFolder, nint expected)
    {
        if (At<FocusedItem>(folderView, 10)(folderView, out var index) < 0 || index < 0 ||
            At<Item>(folderView, 6)(folderView, index, out var current) < 0 || current == 0)
            return false;
        try { return IsSameItem(shellFolder, current, expected); }
        finally { Marshal.FreeCoTaskMem(current); }
    }

    private static bool IsSelectedItem(nint folderView, nint shellFolder, nint expected)
    {
        var enumId = EnumId;
        if (At<Items>(folderView, 8)(folderView, 1, ref enumId, out var enumeration) < 0 || enumeration == 0)
            return false;
        try
        {
            var next = At<Next>(enumeration, 3);
            for (;;)
            {
                var result = next(enumeration, 1, out var item, out var fetched);
                try
                {
                    if (result == 1 && fetched == 0 && item == 0) return false;
                    if (result < 0 || item == 0 || fetched != 1) return false;
                    if (IsSameItem(shellFolder, item, expected)) return true;
                }
                finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
            }
        }
        finally { Marshal.Release(enumeration); }
    }

    private static bool IsSameItem(nint shellFolder, nint first, nint second) =>
        IsSameItem(shellFolder, first, second, out _);

    private static bool IsSameItem(nint shellFolder, nint first, nint second, out string method)
    {
        // Refresh snapshots usually contain the same relative PIDL bytes; short-circuit that
        // common case to avoid one synchronous CompareIDs COM call for each selected item.
        if (ILIsEqual(first, second))
        {
            method = "pidl-exact";
            return true;
        }

        var result = At<CompareIds>(shellFolder, 7)(shellFolder, CompareCanonicalOnly, first, second);
        method = "canonical-shell";
        return result >= 0 && (short)(result & 0xffff) == 0;
    }

    public void Dispose()
    {
        foreach (var item in _selected) Marshal.FreeCoTaskMem(item);
        if (_focused != 0) Marshal.FreeCoTaskMem(_focused);
    }

    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));

    private static long StartTiming(Action<string>? trace) => trace is null ? 0 : Stopwatch.GetTimestamp();

    private static double ElapsedMilliseconds(long startedAt) => startedAt == 0
        ? 0
        : Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Items(nint self, uint scope, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Next(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CompareIds(nint self, nint lParam, nint first, nint second);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Item(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectionState(nint self, nint item, out uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectAndPositionItems(nint self, uint count, nint items, nint positions, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, nint item, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPosition(nint self, nint item, out Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }

    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILIsEqual(nint first, nint second);
}
