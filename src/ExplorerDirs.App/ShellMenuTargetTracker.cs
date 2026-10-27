namespace ExplorerDirs.App;

internal enum ShellMenuTarget { Unknown, Background, Item }

internal readonly record struct ShellMenuPoint(int X, int Y);

// The isolated native Shell-view prototype still uses this precise legacy-list hit rule.
// The ExplorerBrowser product no longer injects commands into file-area context menus.
internal static class ShellMenuTargetTracker
{
    private const uint LvhtExOnContents = 0x04000000; // Vista: item icon/text content, excluding background.

    internal static bool IsNativeListItemHit(int itemIndex, uint hitFlags) =>
        itemIndex >= 0 && (hitFlags & (0x000E | LvhtExOnContents)) != 0;

    internal static ShellMenuTarget ResolveNativeListViewHitTest(bool available, int itemIndex, uint hitFlags)
    {
        if (!available) return ShellMenuTarget.Unknown;
        return IsNativeListItemHit(itemIndex, hitFlags) ? ShellMenuTarget.Item : ShellMenuTarget.Background;
    }

    internal static ShellMenuPoint DecodeContextPoint(nint lParam)
    {
        var packed = lParam.ToInt64();
        return new ShellMenuPoint(unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }
}
