using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellCreatedItemNotification
{
    private const int MaxPath = 260;
    private const uint ShellItemCreated = 0x00000002;
    private const uint PathAndFlushNoWait = 0x00002005;
    private const uint PidlAndFlushNoWait = 0x00002000;

    internal static bool ShouldNotifyPath(string path, bool isDirectory) =>
        !isDirectory && Path.IsPathFullyQualified(path) && path.Length < MaxPath;

    internal static bool NotifyCreatedFile(string path)
    {
        if (!ShouldNotifyPath(path, isDirectory: false)) return false;

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        // This is called from the ExplorerBrowser STA; do not wait there for every Shell subscriber.
        SHChangeNotify(ShellItemCreated, PathAndFlushNoWait, path, IntPtr.Zero);
        return true;
    }

    internal static bool NotifyCreatedFile(string path, IntPtr absolutePidl)
    {
        if (absolutePidl == IntPtr.Zero) return NotifyCreatedFile(path);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        // PIDL notification also covers paths that exceed the legacy MAX_PATH string form.
        SHChangeNotifyPidl(ShellItemCreated, PidlAndFlushNoWait, absolutePidl, IntPtr.Zero);
        return true;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    private static extern void SHChangeNotifyPidl(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
