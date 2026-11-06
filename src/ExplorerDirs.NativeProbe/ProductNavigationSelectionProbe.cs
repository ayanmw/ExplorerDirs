using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    private static void VerifyNavigationSelection(nint shell, nint list)
    {
        Check(Marshal.QueryInterface(shell, in FolderView2Id, out var view));
        try
        {
            var count = At<NavigationSelectionCount>(view, 7);
            int Selected() { Check(count(view, 1, out var selected)); return selected; }
            void Reset()
            {
                SetListViewState(list, -1, 0, LvisSelected | LvisFocused);
                if (Selected() != 0) throw new InvalidOperationException("Selection fixture did not clear.");
            }
            Reset();
            ShellSelectionCommands.RestoreFocusedItem(view);
            if (Selected() != 0) throw new InvalidOperationException("Ordinary focus repair changed explicit deselection.");
            Console.WriteLine("navigation-selection-baseline: ordinary focus repair leaves empty selection (reported symptom)");
            if (!ShellSelectionCommands.EnsureNavigationSelection(view, () => true) || Selected() != 1 ||
                (unchecked((uint)SendMessageW(list, LvmGetItemState, 0, (nint)(LvisSelected | LvisFocused))) & 3) != 3)
                throw new InvalidOperationException("Navigation did not select/focus the first displayed item.");

            Reset();
            SetListViewState(list, 2, LvisFocused, LvisFocused);
            if (!ShellSelectionCommands.EnsureNavigationSelection(view, () => true) || Selected() != 1 ||
                (unchecked((uint)SendMessageW(list, LvmGetItemState, 2, (nint)(LvisSelected | LvisFocused))) & 3) != 3)
                throw new InvalidOperationException("Navigation ignored the Shell-restored focused item.");

            SetListViewState(list, 1, LvisSelected, LvisSelected);
            if (!ShellSelectionCommands.EnsureNavigationSelection(view, () => true) || Selected() != 2)
                throw new InvalidOperationException("Navigation replaced an existing history multi-selection.");
            Reset();
            if (ShellSelectionCommands.EnsureNavigationSelection(view, () => false) || Selected() != 0)
                throw new InvalidOperationException("Retired request changed selection.");
            var reads = 0;
            if (ShellSelectionCommands.EnsureNavigationSelection(view, () => ++reads < 4) || Selected() != 0)
                throw new InvalidOperationException("Request retired during native queries changed selection.");
            Console.WriteLine("navigation-selection: PASS first row; restored focus; existing multi-selection; explicit deselection; retired/reentrant guard");
        }
        finally { Marshal.Release(view); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NavigationSelectionCount(nint self, uint flags, out int count);
}
