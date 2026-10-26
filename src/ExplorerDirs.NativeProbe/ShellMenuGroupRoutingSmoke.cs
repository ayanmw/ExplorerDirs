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
        var groups = new nint[6];
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
            if (!AppendMenuW(groups[0], 0, 0x8125, "Refresh File") ||
                !AppendMenuW(groups[0], 0, 0x8128, "Unique File") ||
                !AppendMenuW(groups[2], 0, 0x8126, "Details View") ||
                !AppendMenuW(groups[3], 0, 0x8125, "Object Command") ||
                !AppendMenuW(groups[4], 0, 0x8127, "Window Command"))
                throw new InvalidOperationException("Could not create unique synthetic Shell menu commands.");

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
            if (ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8123) is not null ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8124) != 1 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8125) is not null ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8126) != 2 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8127) != 4 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8128) != 0 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8129) is not null)
                throw new InvalidOperationException("The frame must route commands only when their File/Edit/View menu group is unique.");
            if (ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8123, fileChild) != 0 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8123, groups[2]) != 2 ||
                ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(composite, 0x8125, groups[3]) != 3)
                throw new InvalidOperationException("The selected menu handle must disambiguate duplicate IDs and retain non-view OLE group identity.");
            VerifyMenuSelectionContext(composite, groups, fileChild);
            ShellViewColorBrowserHostSmoke.VerifyCoordinatorCommandMessagePath();
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

            VerifyMergedCommandDelivery(composite, fileChild, groups);

            Console.WriteLine("shell-menu-group-routing-smoke: PASS File/Edit/View routing, reentrant active-view guard, and WM_COMMAND delivery; no Shell view created");
            return 0;
        }
        finally
        {
            _ = DestroyMenu(composite);
        }
    }

    private static void VerifyMergedCommandDelivery(nint composite, nint fileChild, IReadOnlyList<nint> groups)
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
            if (!ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8128) || _observedCommandId != 0x8128)
                throw new InvalidOperationException("A merged File command did not reach the Shell view HWND with its original command ID.");
            _observedCommandId = 0;
            if (!ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8124) || _observedCommandId != 0x8124)
                throw new InvalidOperationException("A merged Edit command did not reach the Shell view HWND with its original command ID.");
            _observedCommandId = 0;
            if (!ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8126) || _observedCommandId != 0x8126)
                throw new InvalidOperationException("A merged View command did not reach the Shell view HWND with its original command ID.");
            _observedCommandId = 0;
            if (ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8123) ||
                ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8125) || _observedCommandId != 0)
                throw new InvalidOperationException("A command shared by multiple OLE menu groups must not be dispatched to an arbitrary Shell view route.");
            if (!ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8123, selectedMenu: fileChild) ||
                !owner.MenuTrace[^1].Contains("group=0", StringComparison.Ordinal) ||
                !ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8123, selectedMenu: groups[2]) ||
                !owner.MenuTrace[^1].Contains("group=2", StringComparison.Ordinal) ||
                ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8125, selectedMenu: groups[3]))
                throw new InvalidOperationException("A selected menu source must route duplicate File/View IDs to their own group and reject Object commands.");

            _observedCommandId = 0;
            var activeChecks = 0;
            if (!ShellViewColorSpike.TryInvokeMergedMenuCommand(owner, 0x8128, isStillActive: () =>
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

    private static void VerifyMenuSelectionContext(nint composite, IReadOnlyList<nint> groups, nint fileChild)
    {
        var selection = new MergedShellMenuSelection();
        selection.Observe(MenuSelectWParam(0, 0x10), composite, composite);
        selection.Observe(MenuSelectWParam(0, 0x10), groups[0], composite);
        selection.Observe(MenuSelectWParam(0x8123, 0), composite, composite);
        if (!selection.TryGetSource(0x8123, out var fileSource) || fileSource != fileChild)
            throw new InvalidOperationException("Nested WM_MENUSELECT events must retain the selected File submenu as the command source.");

        selection.Observe(MenuSelectWParam(2, 0x10), composite, composite);
        selection.Observe(MenuSelectWParam(0x8123, 0), composite, composite);
        if (!selection.TryGetSource(0x8123, out var viewSource) || viewSource != groups[2])
            throw new InvalidOperationException("WM_MENUSELECT must replace a prior File source with the selected View group.");

        selection.Observe(MenuSelectWParam(3, 0x10), composite, composite);
        selection.Observe(MenuSelectWParam(0x8125, 0), composite, composite);
        if (!selection.TryGetSource(0x8125, out var objectSource) || objectSource != groups[3])
            throw new InvalidOperationException("WM_MENUSELECT must preserve non-view OLE group source identity for the router to reject safely.");

        selection.Reset();
        if (selection.TryGetSource(0x8125, out _))
            throw new InvalidOperationException("Exiting the menu loop must clear its selected command source.");
    }

    private static nuint MenuSelectWParam(uint item, uint flags) => item | ((nuint)flags << 16);

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
