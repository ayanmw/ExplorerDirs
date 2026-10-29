using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class AddressTreeShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x53, 1) == ExplorerShortcut.Address,
            "Q-Dir Alt+S focuses address path editing");
        Check(KeyboardShortcuts.Resolve(0x53, 2) == ExplorerShortcut.ProgramStarter,
            "Ctrl+S remains the Program Starter shortcut");
        Check(KeyboardShortcuts.Resolve(0x73, 0) == ExplorerShortcut.AddressTree,
            "F4 routes to the address tree transition rather than opening address history");
        Check(AddressTreeShortcutState.Next(classicAddressBar: false, treeOpen: false) ==
              AddressTreeShortcutAction.OpenTree,
            "Modern first F4 opens the address tree");
        Check(AddressTreeShortcutState.Next(classicAddressBar: false, treeOpen: true) ==
              AddressTreeShortcutAction.FocusAddressEdit,
            "Modern second F4 focuses path editing");
        Check(AddressTreeShortcutState.Next(classicAddressBar: true, treeOpen: false) ==
              AddressTreeShortcutAction.FocusAddressEdit,
            "Classic F4 focuses path editing directly");
        Check(AddressTreeShortcutState.Next(classicAddressBar: true, treeOpen: true) ==
              AddressTreeShortcutAction.FocusAddressEdit,
            "switching to Classic while tree is open leaves the tree transition");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
