using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

internal static class ShellMenuGroupRoutingSmoke
{
    private static readonly SubclassProc WindowSubclassProcedure = ObserveCommand;
    private static uint _observedCommandId;

    public static int Run()
    {
        var composite = CreateMenu();
        if (composite == 0) throw new InvalidOperationException("Could not create a synthetic composite menu.");
        var groups = new nint[3];
        try
        {
            for (var index = 0; index < groups.Length; index++)
            {
                groups[index] = CreatePopupMenu();
                if (groups[index] == 0 || !AppendMenuW(composite, 0x10, (nuint)groups[index], $"Group {index}"))
                    throw new InvalidOperationException("Could not create synthetic merged Shell menu groups.");
            }

            var fileChild = CreatePopupMenu();
            if (fileChild == 0 || !AppendMenuW(fileChild, 0, 0x8123, "New Item") ||
                !AppendMenuW(groups[0], 0x10, (nuint)fileChild, "New"))
                throw new InvalidOperationException("Could not create the synthetic File menu command.");
            if (!AppendMenuW(groups[1], 0, 0x8123, "Select All"))
                throw new InvalidOperationException("Could not create the synthetic Edit menu command.");
            if (!AppendMenuW(groups[1], 0, 0x8124, "Copy"))
                throw new InvalidOperationException("Could not create the synthetic Edit copy command.");
            if (!AppendMenuW(groups[2], 0, 0x8123, "Details"))
                throw new InvalidOperationException("Could not create the synthetic View menu command.");

            var path = ShellViewColorSpike.FindMergedMenuCommandPath(composite, 0, 0x8123);
            if (path is null || !path.SequenceEqual(["New", "New Item"]))
                throw new InvalidOperationException("A command present only in the merged File group was not resolved to its menu path.");
            if (ShellViewColorSpike.FindMergedMenuCommandPath(composite, 1, 0x8123) is not IReadOnlyList<string> editPath ||
                !editPath.SequenceEqual(["Select All"]))
                throw new InvalidOperationException("An Edit group command was not kept in its own group.");
            if (ShellViewColorSpike.FindMergedMenuCommandPath(composite, 2, 0x8123) is not IReadOnlyList<string> viewPath ||
                !viewPath.SequenceEqual(["Details"]))
                throw new InvalidOperationException("A View group command was not kept in its own group.");
            if (ShellViewColorSpike.FindMergedMenuCommandPath(composite, 3, 0x8123) is not null)
                throw new InvalidOperationException("A command lookup outside the six standard groups must not match.");
            if (ShellViewColorSpike.ResolveMergedEditMenuCommandRoute(composite, 0x8123) != MergedEditMenuCommandRoute.SelectAll ||
                ShellViewColorSpike.ResolveMergedEditMenuCommandRoute(composite, 0x8124) != MergedEditMenuCommandRoute.ShellView ||
                ShellViewColorSpike.ResolveMergedEditMenuCommandRoute(composite, 0x8125) != MergedEditMenuCommandRoute.NotFound)
                throw new InvalidOperationException("Merged Edit commands must distinguish Select All, native Shell commands and unrelated command IDs.");
            using var owner = new StreamViewHost(0, []) { View = (nint)0x1234 };
            if (!ShellViewColorSpike.IsCurrentShellMenuOwner(owner, (nint)0x1234, () => true) ||
                ShellViewColorSpike.IsCurrentShellMenuOwner(owner, (nint)0x1234,
                    () => { owner.View = (nint)0x5678; return true; }) ||
                ShellViewColorSpike.IsCurrentShellMenuOwner(owner, (nint)0x5678, () => false))
                throw new InvalidOperationException("A Shell menu command must be cancelled when its view or pane changes during a reentrant callback.");

            VerifyMergedCommandDelivery(composite);

            Console.WriteLine("shell-menu-group-routing-smoke: PASS File/Edit/View routing, reentrant active-view guard, and WM_COMMAND delivery; no Shell view created");
            return 0;
        }
        finally
        {
            _ = DestroyMenu(composite);
        }
    }

    private static void VerifyMergedCommandDelivery(nint composite)
    {
        var frame = CreateWindowExW(0, "STATIC", "synthetic menu frame", 0, 0, 0, 200, 120, 0, 0, 0, 0);
        if (frame == 0) throw new InvalidOperationException("Could not create a hidden window for synthetic WM_COMMAND delivery.");
        var installedMenu = false;
        var subclassed = false;
        nint viewPointer = 0;
        try
        {
            if (!SetMenu(frame, composite)) throw new InvalidOperationException($"Could not attach the synthetic merged menu to its hidden frame: win32={Marshal.GetLastWin32Error()}.");
            installedMenu = true;
            if (!SetWindowSubclass(frame, WindowSubclassProcedure, 41, 0))
                throw new InvalidOperationException("Could not observe WM_COMMAND at the fake Shell view HWND.");
            subclassed = true;
            using var owner = new StreamViewHost(frame, [], menuFrameWindow: frame);
            viewPointer = Marshal.GetComInterfaceForObject(owner, typeof(IStreamProbeOleInPlaceFrame));
            owner.View = viewPointer;

            _observedCommandId = 0;
            if (!ShellViewColorSpike.TryInvokeMergedFileMenuCommand(owner, 0x8123) || _observedCommandId != 0x8123)
                throw new InvalidOperationException("A merged File command did not reach the Shell view HWND with its original command ID.");
            _observedCommandId = 0;
            if (!ShellViewColorSpike.TryInvokeMergedEditMenuCommand(owner, 0x8124) || _observedCommandId != 0x8124)
                throw new InvalidOperationException("A merged Edit command did not reach the Shell view HWND with its original command ID.");
            _observedCommandId = 0;
            if (!ShellViewColorSpike.TryInvokeMergedViewMenuCommand(owner, 0x8123) || _observedCommandId != 0x8123)
                throw new InvalidOperationException("A merged View command did not reach the Shell view HWND with its original command ID.");

            _observedCommandId = 0;
            var activeChecks = 0;
            if (!ShellViewColorSpike.TryInvokeMergedFileMenuCommand(owner, 0x8123, isStillActive: () =>
                {
                    if (++activeChecks == 2) owner.View = (nint)0x5678;
                    return true;
                }) || _observedCommandId != 0)
                throw new InvalidOperationException("A Shell command was sent after its view changed reentrantly during GetWindow.");
            owner.View = viewPointer;
        }
        finally
        {
            if (viewPointer != 0) Marshal.Release(viewPointer);
            if (subclassed) _ = RemoveWindowSubclass(frame, WindowSubclassProcedure, 41);
            if (installedMenu) _ = SetMenu(frame, 0);
            if (IsWindow(frame)) _ = DestroyWindow(frame);
        }
    }

    private static nint ObserveCommand(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0111) // WM_COMMAND
        {
            _observedCommandId = (uint)(wParam & 0xFFFF);
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern nint CreateMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetMenu(nint window, nint menu);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
}
