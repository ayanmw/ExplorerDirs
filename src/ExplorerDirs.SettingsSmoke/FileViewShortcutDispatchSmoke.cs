using ExplorerDirs.App;

internal static class FileViewShortcutDispatchSmoke
{
    internal static void Run()
    {
        var invoked = 0;
        Require(FileViewShortcutDispatch.TryOpenSelectionInNewTab(true, () =>
                {
                    invoked++;
                    return true;
                }) && invoked == 1,
            "Shift+Enter is handled when the focused file view opens its single selected folder in a new tab");

        invoked = 0;
        Require(!FileViewShortcutDispatch.TryOpenSelectionInNewTab(false, () =>
                {
                    invoked++;
                    return true;
                }) && invoked == 0,
            "Shift+Enter is not consumed when focus is outside the file view");

        Require(!FileViewShortcutDispatch.TryOpenSelectionInNewTab(true, () => false),
            "Shift+Enter remains unhandled when there is no unique ordinary filesystem folder selection");
        Console.WriteLine("File-view shortcut dispatch smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
