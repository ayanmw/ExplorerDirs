using ExplorerDirs.App;

internal static class ShellCreatedItemNotificationSmoke
{
    public static void Run()
    {
        Check(ShellCreatedItemNotification.ShouldNotifyPath(@"C:\files\created.txt", isDirectory: false),
            "short created file paths can use a Shell create notification");
        Check(!ShellCreatedItemNotification.ShouldNotifyPath(@"C:\files\created", isDirectory: true),
            "created folders are excluded from the nonfolder Shell create event");
        Check(!ShellCreatedItemNotification.ShouldNotifyPath("C:\\" + new string('x', 259), isDirectory: false),
            "paths at or beyond MAX_PATH are excluded from SHCNF_PATH notifications");
        Check(!ShellCreatedItemNotification.ShouldNotifyPath("relative.txt", isDirectory: false),
            "relative paths are excluded from Shell create notifications");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
