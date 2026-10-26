using System.Runtime.InteropServices;

internal static class FolderHeaderOptionsProbe
{
    internal static void EnableCustomPosition(nint browser)
    {
        var result = ShellFolderViewOptions.EnableCustomPosition(browser, out var before, out var after);
        Marshal.ThrowExceptionForHR(result);
        Console.WriteLine($"folder-header-options-hosted: options-before-initialize=0x{before:X8}; readback=0x{after:X8}");
    }

    internal static bool InspectView(nint view)
    {
        var result = ShellFolderViewOptions.EnableAllViewHeaders(view, out var before, out var after);
        Console.WriteLine($"folder-header-options-hosted: before=0x{before:X8}; set-and-readback=0x{result:X8}; after=0x{after:X8}");
        return result >= 0;
    }

}
