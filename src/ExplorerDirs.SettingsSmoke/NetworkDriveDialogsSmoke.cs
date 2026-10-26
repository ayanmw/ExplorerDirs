using ExplorerDirs.App;
using System.Runtime.InteropServices;

namespace ExplorerDirs.SettingsSmoke;

internal static class NetworkDriveDialogsSmoke
{
    public static void Run()
    {
        var connectOwner = (nint)123;
        var disconnectOwner = (nint)456;
        nint observedConnectOwner = 0;
        nint observedDisconnectOwner = 0;
        var connectResult = 0;
        var disconnectResult = 0;
        var refreshNotifications = 0;
        var dialogs = new NetworkDriveDialogs(
            owner => { observedConnectOwner = owner; return connectResult; },
            owner => { observedDisconnectOwner = owner; return disconnectResult; },
            () => refreshNotifications++);

        Check(dialogs.Connect(connectOwner) == 0 && observedConnectOwner == connectOwner,
            "Connect dispatch uses the supplied owner and returns the native result.");
        Check(refreshNotifications == 1,
            "successful network drive mapping notifies the Shell once to refresh This PC.");
        connectResult = -1; // Native dialog cancel.
        Check(dialogs.Connect(connectOwner) == -1 && refreshNotifications == 1,
            "cancelling network drive mapping does not notify the Shell.");
        disconnectResult = 0;
        Check(dialogs.Disconnect(disconnectOwner) == 0 && observedDisconnectOwner == disconnectOwner &&
              refreshNotifications == 2,
            "successful network drive disconnection notifies the Shell once to refresh This PC.");
        disconnectResult = 5;
        Check(dialogs.Disconnect(disconnectOwner) == 5 && observedDisconnectOwner == disconnectOwner &&
              refreshNotifications == 2,
            "Disconnect dispatch uses the supplied owner and preserves the native error code.");

        var extendedErrorRead = false;
        var extendedText = NetworkDriveDialogs.FormatError(1208, () =>
        {
            extendedErrorRead = true;
            return new NetworkDriveExtendedError(1234, "Test provider", "Share is unavailable.");
        });
        Check(extendedErrorRead && extendedText.Contains("Test provider", StringComparison.Ordinal) &&
              extendedText.Contains("Share is unavailable.", StringComparison.Ordinal),
            "Extended WNet failures include the provider's descriptive error.");

        var ordinaryErrorRead = false;
        var ordinaryText = NetworkDriveDialogs.FormatError(5, () =>
        {
            ordinaryErrorRead = true;
            return null;
        });
        Check(!ordinaryErrorRead && !string.IsNullOrWhiteSpace(ordinaryText),
            "Ordinary WNet failures use the system message without querying extended error state.");

        var mpr = NativeLibrary.Load("mpr.dll");
        try
        {
            Check(NativeLibrary.TryGetExport(mpr, "WNetConnectionDialog", out _) &&
                  NativeLibrary.TryGetExport(mpr, "WNetDisconnectDialog", out _) &&
                  NativeLibrary.TryGetExport(mpr, "WNetGetLastErrorW", out _),
                "The configured WNet entry points are exported by the Windows MPR library.");
        }
        finally { NativeLibrary.Free(mpr); }

        var shell = NativeLibrary.Load("shell32.dll");
        try
        {
            Check(NativeLibrary.TryGetExport(shell, "SHGetKnownFolderIDList", out _) &&
                  NativeLibrary.TryGetExport(shell, "SHChangeNotify", out _),
                "The This PC refresh APIs are exported by the Windows Shell library.");
        }
        finally { NativeLibrary.Free(shell); }

        Guid requestedFolder = default;
        uint notifiedEvent = 0;
        uint notificationFlags = 0;
        nint notifiedItem = 0;
        nint releasedPidl = 0;
        ShellDriveRefreshNotifier.DispatchThisPcChanged(
            folder => { requestedFolder = folder; return (nint)789; },
            (eventId, flags, item1, item2) =>
            {
                notifiedEvent = eventId;
                notificationFlags = flags;
                notifiedItem = item1;
                Check(item2 == 0, "This PC refresh notification does not supply a second PIDL.");
            },
            pidl => releasedPidl = pidl);
        Check(requestedFolder == new Guid("0AC0837C-BBF8-452A-850D-79D08E667CA7") &&
              notifiedEvent == 0x00001000 && notificationFlags == 0x00002000 &&
              notifiedItem == 789 && releasedPidl == 789,
            "This PC refresh uses the computer-folder PIDL, SHCNE_UPDATEDIR and nonblocking ID-list flags, then releases the PIDL.");

        var nullPidlNotified = false;
        var nullPidlReleased = false;
        ShellDriveRefreshNotifier.DispatchThisPcChanged(_ => 0,
            (_, _, _, _) => nullPidlNotified = true,
            _ => nullPidlReleased = true);
        Check(!nullPidlNotified && !nullPidlReleased,
            "a missing This PC PIDL skips Shell notification and release.");
        Console.WriteLine("Network drive dialog smoke: PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
