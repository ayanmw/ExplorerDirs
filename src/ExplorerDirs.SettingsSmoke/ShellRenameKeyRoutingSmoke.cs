using ExplorerDirs.App;

internal static class ShellRenameKeyRoutingSmoke
{
    internal static void Run()
    {
        foreach (var key in new[] { 0x25, 0x26, 0x27, 0x28 })
            Check(ShellRenameKeyRouting.ShouldForwardArrowKeyDown(0x0100, key),
                $"rename editor receives arrow keydown 0x{key:X2}");

        Check(!ShellRenameKeyRouting.ShouldForwardArrowKeyDown(0x0101, 0x25),
            "rename arrow keyup is not redispatched");
        Check(!ShellRenameKeyRouting.ShouldForwardArrowKeyDown(0x0104, 0x25),
            "system keydown is not treated as a rename arrow");
        Check(!ShellRenameKeyRouting.ShouldForwardArrowKeyDown(0x0100, 0x41),
            "non-arrow keydown remains on the normal accelerator path");
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException($"Shell rename key routing: {description}");
        Console.WriteLine($"PASS {description}");
    }
}
