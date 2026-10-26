using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

internal sealed class NetworkDriveDialogs(Func<nint, int> connect, Func<nint, int> disconnect,
    Action notifyShellOfDriveChanges)
{
    public int Connect(nint owner) => RunAndNotify(connect, owner);
    public int Disconnect(nint owner) => RunAndNotify(disconnect, owner);

    public static NetworkDriveDialogs CreateSystem() => new(
        owner => WNetConnectionDialog(owner, ResourceTypeDisk),
        owner => WNetDisconnectDialog(owner, ResourceTypeDisk),
        ShellDriveRefreshNotifier.NotifyThisPcChanged);

    private int RunAndNotify(Func<nint, int> showDialog, nint owner)
    {
        var result = showDialog(owner);
        if (result == 0) notifyShellOfDriveChanges();
        return result;
    }

    public static string GetErrorMessage(int errorCode) => FormatError(errorCode, ReadExtendedError);

    internal static string FormatError(int errorCode, Func<NetworkDriveExtendedError?> getExtendedError)
    {
        if (errorCode == ErrorExtendedError && getExtendedError() is { } extended)
        {
            var provider = string.IsNullOrWhiteSpace(extended.Provider) ? "网络提供程序" : extended.Provider;
            return $"{provider}：{extended.Description}（错误码 {extended.Code}）";
        }
        return new Win32Exception(errorCode).Message;
    }

    private static NetworkDriveExtendedError? ReadExtendedError()
    {
        var description = new StringBuilder(512);
        var provider = new StringBuilder(256);
        var result = WNetGetLastErrorW(out var providerError, description, (uint)description.Capacity,
            provider, (uint)provider.Capacity);
        return result == 0
            ? new NetworkDriveExtendedError(providerError, provider.ToString(), description.ToString())
            : null;
    }

    private const int ResourceTypeDisk = 1;
    private const int ErrorExtendedError = 1208;

    [DllImport("mpr.dll", ExactSpelling = true)]
    private static extern int WNetConnectionDialog(nint owner, int resourceType);

    [DllImport("mpr.dll", ExactSpelling = true)]
    private static extern int WNetDisconnectDialog(nint owner, int resourceType);

    [DllImport("mpr.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WNetGetLastErrorW(out uint error, StringBuilder errorBuffer, uint errorBufferSize,
        StringBuilder nameBuffer, uint nameBufferSize);
}

internal readonly record struct NetworkDriveExtendedError(uint Code, string Provider, string Description);

internal static class ShellDriveRefreshNotifier
{
    private static readonly Guid ComputerFolderId = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");
    private const uint ShellContentsChanged = 0x00001000; // SHCNE_UPDATEDIR.
    private const uint IdListAndFlushNowait = 0x00002000; // SHCNF_IDLIST | SHCNF_FLUSHNOWAIT.

    internal static void NotifyThisPcChanged()
    {
        DispatchThisPcChanged(folderId =>
        {
            var requestedFolder = folderId;
            return SHGetKnownFolderIDList(ref requestedFolder, 0, 0, out var computerPidl) == 0
                ? computerPidl : 0;
        }, SHChangeNotify, Marshal.FreeCoTaskMem);
    }

    internal static void DispatchThisPcChanged(Func<Guid, nint> getPidl,
        Action<uint, uint, nint, nint> notify, Action<nint> releasePidl)
    {
        var computerPidl = getPidl(ComputerFolderId);
        if (computerPidl == 0) return;
        try { notify(ShellContentsChanged, IdListAndFlushNowait, computerPidl, 0); }
        finally { releasePidl(computerPidl); }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderIDList(ref Guid folderId, uint flags, nint token, out nint itemIdList);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
