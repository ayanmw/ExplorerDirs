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

        var checkSelectCases = new (uint Input, bool Enabled, uint Expected)[]
        {
            (0x40200008, true, 0x40240008),
            (0x40240008, false, 0x40200008),
            (0xFFFFFFFF, true, 0xFFFFFFFF)
        };
        foreach (var (input, enabled, expected) in checkSelectCases)
        {
            var actual = ShellFolderViewOptions.SetCheckSelect(input, enabled);
            if (actual != expected)
                throw new InvalidOperationException($"Check-select flag update expected 0x{expected:X8}, got 0x{actual:X8} from 0x{input:X8} (enabled={enabled}).");
        }

        var fullRowSelectCases = new (uint Input, bool Enabled, uint Expected)[]
        {
            (0x40040008, true, 0x40240008),
            (0x40240008, false, 0x40040008),
            (0xFFFFFFFF, false, 0xFFDFFFFF)
        };
        foreach (var (input, enabled, expected) in fullRowSelectCases)
        {
            var actual = ShellFolderViewOptions.SetFullRowSelect(input, enabled);
            if (actual != expected)
                throw new InvalidOperationException($"Full-row-select flag update expected 0x{expected:X8}, got 0x{actual:X8} from 0x{input:X8} (enabled={enabled}).");
        }

        Console.WriteLine("Folder header flags smoke: PASS");
    }
}
