namespace ExplorerDirs.App;

internal enum ShellFileCommand { Cut, Copy, Paste, Delete, Rename, Properties, Undo, Redo, NewFolder }
internal enum ShellFileKeyboardAction { None, SelectAll, Copy, Paste, Cut, Undo, Redo }

internal static class ShellRenameKeyRouting
{
    internal static bool ShouldForwardNavigationKeyDown(int message, int key) =>
        message == 0x0100 && ShellNavigationKeyRouting.IsNavigationKey(key);
}

internal static class ShellNavigationKeyRouting
{
    // VK_PRIOR through VK_DOWN include paging, Home/End and the four arrows.
    internal static bool IsNavigationKey(int key) => key is >= 0x21 and <= 0x28;

    internal static bool ShouldForward(int message, int key, int shellResult,
        bool fileContentFocused, bool columnHeaderFocused, bool editFocused) =>
        message == 0x0100 && IsNavigationKey(key) && shellResult == 1 &&
        fileContentFocused && !columnHeaderFocused && !editFocused;
}

internal static class ShellFileKeyboardActions
{
    internal static ShellFileKeyboardAction ResolveFallback(int message, int key, int modifiers, int shellResult) =>
        message == 0x0100 && shellResult == 1 ? Resolve(key, modifiers) : ShellFileKeyboardAction.None;

    internal static ShellFileKeyboardAction Resolve(int key, int modifiers) => (key, modifiers) switch
    {
        (0x41, 2) => ShellFileKeyboardAction.SelectAll, // Ctrl+A
        (0x43, 2) or (0x2D, 2) => ShellFileKeyboardAction.Copy, // Ctrl+C / Ctrl+Insert
        (0x56, 2) or (0x2D, 4) => ShellFileKeyboardAction.Paste, // Ctrl+V / Shift+Insert
        (0x58, 2) => ShellFileKeyboardAction.Cut, // Ctrl+X
        (0x5A, 2) => ShellFileKeyboardAction.Undo, // Ctrl+Z
        (0x59, 2) => ShellFileKeyboardAction.Redo, // Ctrl+Y
        _ => ShellFileKeyboardAction.None
    };
}

internal sealed record ShellFileCommandInfo(ShellFileCommand Command, string Label, string Verb,
    string Shortcut, bool Background);

internal interface IShellCommandMenu : IDisposable
{
    bool IsEnabled(string verb);
    int Invoke(string verb);
}

internal static class ShellFileCommands
{
    internal static IReadOnlyList<ShellFileCommandInfo> All { get; } =
    [
        new(ShellFileCommand.Cut, "剪切", "cut", "Ctrl+X", false),
        new(ShellFileCommand.Copy, "复制", "copy", "Ctrl+C", false),
        new(ShellFileCommand.Paste, "粘贴", "paste", "Ctrl+V", true),
        new(ShellFileCommand.Delete, "删除", "delete", "Delete", false),
        new(ShellFileCommand.Rename, "重命名", "rename", "F2", false),
        new(ShellFileCommand.Properties, "属性", "properties", "Alt+Enter", false),
        new(ShellFileCommand.Undo, "撤销", "undo", "Ctrl+Z", true),
        new(ShellFileCommand.Redo, "重做", "redo", "Ctrl+Y", true),
        new(ShellFileCommand.NewFolder, "新建文件夹", "NewFolder", "Ctrl+Shift+N", true)
    ];

    internal static Dictionary<ShellFileCommand, bool> Query(Func<bool, IShellCommandMenu?> open,
        Func<bool>? stillCurrent = null)
    {
        var states = All.ToDictionary(info => info.Command, _ => false);
        Dictionary<ShellFileCommand, bool> DisabledStates() => All.ToDictionary(info => info.Command, _ => false);
        foreach (var background in new[] { false, true })
        {
            if (stillCurrent?.Invoke() == false) return DisabledStates();
            using var menu = open(background);
            if (stillCurrent?.Invoke() == false) return DisabledStates();
            if (menu is null) continue;
            foreach (var info in All.Where(info => info.Background == background))
            {
                states[info.Command] = menu.IsEnabled(info.Verb);
                if (stillCurrent?.Invoke() == false) return DisabledStates();
            }
        }
        return states;
    }

    // null = unavailable; HRESULT only means Shell accepted/rejected invocation,
    // never that an asynchronous file operation has completed.
    internal static int? Invoke(ShellFileCommand command, Func<bool, IShellCommandMenu?> open,
        Func<bool>? stillCurrent = null)
    {
        if (stillCurrent?.Invoke() == false) return null;
        var info = All.Single(info => info.Command == command);
        using var menu = open(info.Background);
        if (stillCurrent?.Invoke() == false || menu?.IsEnabled(info.Verb) != true ||
            stillCurrent?.Invoke() == false) return null;
        return menu.Invoke(info.Verb);
    }
}
