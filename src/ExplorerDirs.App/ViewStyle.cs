namespace ExplorerDirs.App;

internal sealed record ViewStyle(string Name, uint Mode, int IconSize)
{
    // Explorer's Ctrl+Shift+1..8 order. -1 asks Shell for the mode's default size.
    public static IReadOnlyList<ViewStyle> All { get; } = Array.AsReadOnly(new[]
    {
        new ViewStyle("超大图标", 1, 256),
        new ViewStyle("大图标", 1, 96),
        new ViewStyle("中等图标", 1, 48),
        new ViewStyle("小图标", 2, -1),
        new ViewStyle("列表", 3, -1),
        new ViewStyle("详细信息", 4, -1),
        new ViewStyle("平铺", 6, -1),
        new ViewStyle("内容", 8, -1)
    });
}
