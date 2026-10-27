using ExplorerDirs.App;

internal static class ShellRenameKeyRoutingSmoke
{
    internal static void Run()
    {
        foreach (var key in new[] { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28 })
        {
            Check(ShellRenameKeyRouting.ShouldForwardNavigationKeyDown(0x0100, key),
                $"rename editor receives navigation keydown 0x{key:X2}");
            Check(!ShellRenameKeyRouting.ShouldForwardNavigationKeyDown(0x0101, key) &&
                !ShellRenameKeyRouting.ShouldForwardNavigationKeyDown(0x0104, key),
                $"rename navigation key 0x{key:X2} does not forward keyup or Alt system messages");
        }

        Check(!ShellRenameKeyRouting.ShouldForwardNavigationKeyDown(0x0100, 0x41),
            "non-navigation keydown remains on the normal accelerator path");
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException($"Shell rename key routing: {description}");
        Console.WriteLine($"PASS {description}");
    }
}
