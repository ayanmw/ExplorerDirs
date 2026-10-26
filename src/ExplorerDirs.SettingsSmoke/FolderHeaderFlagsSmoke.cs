namespace ExplorerDirs.SettingsSmoke;

internal static class FolderHeaderFlagsSmoke
{
    internal static void Run()
    {
        var cases = new (uint Input, uint Expected)[]
        {
            (0x41A00008, 0x40200008), // Both header suppressors plus unrelated Shell flags.
            (0x01000008, 0x00000008), // Header limited to Details only.
            (0x00800008, 0x00000008), // Header suppressed in all views.
            (0x40200008, 0x40200008)  // Existing unrelated flags remain unchanged.
        };

        foreach (var (input, expected) in cases)
        {
            var actual = ShellFolderViewOptions.EnableAllViewHeaders(input);
            if (actual != expected)
                throw new InvalidOperationException($"Header flag update expected 0x{expected:X8}, got 0x{actual:X8} from 0x{input:X8}.");
        }

        var optionCases = new (uint Input, uint Expected)[]
        {
            (0x00000000, 0x00000002),
            (0x00000041, 0x00000043),
            (0xFFFFFFFF, 0xFFFFFFFF)
        };

        foreach (var (input, expected) in optionCases)
        {
            var actual = ShellFolderViewOptions.EnableCustomPosition(input);
            if (actual != expected)
                throw new InvalidOperationException($"Custom-position option expected 0x{expected:X8}, got 0x{actual:X8} from 0x{input:X8}.");
        }

        Console.WriteLine("Folder header flags smoke: PASS");
    }
}
