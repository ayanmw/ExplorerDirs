using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class SelectionCommandProbe
{
    internal static void Run(nint browser)
    {
        var iid = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
        Check(At<GetView>(browser, 17)(browser, ref iid, out var view));
        try
        {
            foreach (var grouped in new[] { false, true })
            foreach (var mode in new uint[] { 4, 3, 1 }) // Details, List, Icons.
            {
                var group = grouped ? new PropertyKey { Id = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), Property = 4 } : default;
                Check(At<SetGroup>(view, 17)(view, ref group, 1));
                Check(At<SetMode>(view, 35)(view, mode, mode == 1 ? 48 : -1));
                Pump(); // Let native grouping/reordering settle before capturing identities.
                var before = ViewStyleProbe.ReadState(view);
                Check(At<Select>(view, 15)(view, 0, 0x40000015));
                var original = Selection(view);
                if (original.Count != 1) throw new Exception("Could not establish single selection.");
                Apply(ShellSelectionCommand.All);
                var all = Selection(view);
                if (all.Count != 3) throw new Exception("Select All did not select the complete view.");
                Apply(ShellSelectionCommand.Invert);
                if (Selection(view).Count != 0) throw new Exception("Invert all did not clear selection.");
                Apply(ShellSelectionCommand.Invert);
                if (!Selection(view).SetEquals(all)) throw new Exception("Invert none did not select all.");
                Apply(ShellSelectionCommand.None);
                if (Selection(view).Count != 0) throw new Exception("Select None retained items.");
                Check(At<Select>(view, 15)(view, 0, 0x40000005));
                original = Selection(view); // Index zero can denote a different item after grouping settles.
                Apply(ShellSelectionCommand.Invert);
                var inverse = Selection(view);
                if (inverse.Count != 2 || inverse.Overlaps(original) || !all.SetEquals(original.Concat(inverse)))
                    throw new Exception("Invert did not select the identity complement.");
                Apply(ShellSelectionCommand.Invert);
                if (!Selection(view).SetEquals(original)) throw new Exception("Double invert did not restore selection.");
                var checks = 0;
                if (ShellSelectionCommands.Apply(view, ShellSelectionCommand.All, () => ++checks < 2, () => true))
                    throw new Exception("Stale command was accepted.");
                if (!Selection(view).SetEquals(original)) throw new Exception("Stale preparation changed selection.");
                if (ShellSelectionCommands.Apply(view, ShellSelectionCommand.Invert, () => true, () => false))
                    throw new Exception("Changed view between selection batches was accepted.");
                if (Selection(view).Count != 0) throw new Exception("Second selection batch ran after view changed.");
                Check(At<GetMode>(view, 36)(view, out var actualMode, out _));
                if (actualMode != mode) throw new Exception("Selection changed view mode.");
                if (ViewStyleProbe.ReadState(view) != before) throw new Exception("Selection changed sort/group settings.");
                Console.WriteLine($"selection-mode={mode}; grouped={grouped}: all/none/invert/identity/stale/focus-flags/sort-group PASS");
            }
            void Apply(ShellSelectionCommand command)
            {
                if (!ShellSelectionCommands.Apply(view, command, () => true, () => true))
                    throw new Exception($"Selection command not implemented/accepted: {command}");
                Pump();
                var beforeFocus = Selection(view);
                // Production focus repair, with NOTAKEFOCUS so this probe never activates a window.
                ShellSelectionCommands.RestoreFocusedItem(view, takeFocus: false);
                if (!Selection(view).SetEquals(beforeFocus)) throw new Exception("Focus flags changed selection.");
            }
        }
        finally { Marshal.Release(view); }
    }

    private static void Pump()
    {
        var until = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
    }

    internal static void CheckEmpty(nint browser)
    {
        var iid = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
        Check(At<GetView>(browser, 17)(browser, ref iid, out var view));
        try
        {
            Check(At<Count>(view, 7)(view, 2, out var count));
            if (count != 0) throw new Exception("Empty fixture is not empty.");
            foreach (var command in Enum.GetValues<ShellSelectionCommand>())
                if (!ShellSelectionCommands.Apply(view, command, () => true, () => true) || Selection(view).Count != 0)
                    throw new Exception("Empty selection command failed.");
            Console.WriteLine("empty-view: all/none/invert PASS");
        }
        finally { Marshal.Release(view); }
    }

    private static HashSet<string> Selection(nint view)
    {
        Check(At<Count>(view, 7)(view, 1, out var count));
        return count == 0 ? new(StringComparer.OrdinalIgnoreCase) : ViewStyleProbe.ReadSelection(view);
    }
    private static void Check(int hr) { if (hr != 0) throw new COMException("Selection probe failed.", hr); }
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetView(nint self, ref Guid id, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Select(nint self, int item, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetMode(nint self, out uint mode, out int size);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Id; public uint Property; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroup(nint self, ref PropertyKey key, int ascending);
}
