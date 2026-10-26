namespace ExplorerDirs.App;

internal enum AddressTreeShortcutAction
{
    OpenTree,
    FocusAddressEdit
}

internal static class AddressTreeShortcutState
{
    internal static AddressTreeShortcutAction Next(bool classicAddressBar, bool treeOpen) =>
        !classicAddressBar && !treeOpen
            ? AddressTreeShortcutAction.OpenTree
            : AddressTreeShortcutAction.FocusAddressEdit;
}
