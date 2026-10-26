using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellFolderFilterLayoutSmoke
{
    internal static void Run()
    {
        var expected = IntPtr.Size == 8 ? 272 : 264;
        if (ShellFolderFilter.StrRetBufferSize != expected)
            throw new Exception($"STRRET buffer size mismatch: expected {expected}, got {ShellFolderFilter.StrRetBufferSize}.");
        Console.WriteLine($"PASS Shell STRRET native buffer size is {expected} bytes for {IntPtr.Size * 8}-bit process");

        var filter = new ShellFolderFilter(QuickFilterSettings.Empty);
        uint flags = 0x10080; // The COM contract marks pgrfFlags as [out], so input bits are not policy.
        if (ShellFolderFilter.GetEnumerationFlags(showHiddenItems: false) != 0x60 ||
            ShellFolderFilter.GetEnumerationFlags(showHiddenItems: true) != 0xE0)
            throw new Exception("Shell enumeration must include ordinary hidden items only when the Shell Show All Files setting is enabled.");
        var expectedFlags = ShellFolderFilter.GetEnumerationFlags(ShellFolderFilter.ReadShowAllObjectsSetting());
        if (filter.GetEnumFlags(0, 0, out _, ref flags) < 0 || flags != expectedFlags)
            throw new Exception($"Shell folder filter flags 0x{flags:X} do not match the current Shell Show All Files setting (expected 0x{expectedFlags:X}).");
        Console.WriteLine($"PASS Shell folder filtering maps the Shell Show All Files setting to enumeration flags 0x{flags:X}");
    }
}
