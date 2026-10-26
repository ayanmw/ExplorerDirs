using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

internal static class ShellMenuGroupRoutingSmoke
{
    public static int Run()
    {
        var composite = CreatePopupMenu();
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

            Console.WriteLine("shell-menu-group-routing-smoke: PASS menu group ownership and reentrant active-view guard; no Shell view created");
            return 0;
        }
        finally
        {
            _ = DestroyMenu(composite);
        }
    }

    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string text);
}
