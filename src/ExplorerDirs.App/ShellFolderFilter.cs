using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

[ComVisible(true), Guid("9CC22886-DC8E-11D2-B1D0-00C04F8EEB3E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellFolderFilter
{
    [PreserveSig] int ShouldShow(nint shellFolder, nint folderPidl, nint itemPidl);
    [PreserveSig] int GetEnumFlags(nint shellFolder, nint folderPidl, out nint hwnd, ref uint flags);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellFolderFilter(QuickFilterSettings settings,
    HiddenItemsPreference hiddenItemsPreference = HiddenItemsPreference.FollowWindows) : IShellFolderFilter
{
    private const uint FolderAttribute = 0x20000000;
    private const uint DisplayNameFlags = 0x8001; // SHGDN_INFOLDER | SHGDN_FORPARSING
    private const uint ShellShowAllObjectsMask = 0x00000001; // SSF_SHOWALLOBJECTS
    private const uint IncludeHiddenItemsFlag = 0x00000080; // SHCONTF_INCLUDEHIDDEN
    // Windows STRRET layout: 272 bytes on x64 (pointer-aligned union), 264 on x86.
    internal static int StrRetBufferSize => IntPtr.Size == 8 ? 272 : 264;
    private readonly QuickFilterSettings _settings = settings;
    private readonly HiddenItemsPreference _hiddenItemsPreference = hiddenItemsPreference;
    private int _shouldShowCalls;
    internal int ShouldShowCalls => Volatile.Read(ref _shouldShowCalls);

    public int ShouldShow(nint shellFolder, nint folderPidl, nint itemPidl)
    {
        // Hidden-item preferences can attach this callback without an active quick filter.
        // In that case visibility is controlled by GetEnumFlags, so avoid per-item Shell lookups.
        if (!_settings.HasAnyFilter) return 0;
        Interlocked.Increment(ref _shouldShowCalls);
        try
        {
            if (shellFolder == 0 || itemPidl == 0) return 0; // S_OK: unsupported Shell items stay visible.
            var isFolder = false;
            if (!_settings.FilterFolders)
            {
                if (!TryGetFolderState(shellFolder, itemPidl, out isFolder)) return 0;
                if (isFolder) return 0;
            }
            if (_settings.HasPattern)
            {
                if (!TryGetName(shellFolder, itemPidl, out var name)) return 0;
                if (!_settings.ShouldShow(name, isFolder)) return 1;
            }
            if (_settings.HasColumnFilters &&
                !ShellColumnValueFilterReader.MatchesFilters(shellFolder, itemPidl, _settings.ColumnFilters))
                return 1;
            return 0; // S_OK shows; S_FALSE filters.
        }
        catch
        {
            return 0; // A filter callback must not hide an item because metadata lookup failed.
        }
    }

    public int GetEnumFlags(nint shellFolder, nint folderPidl, out nint hwnd, ref uint flags)
    {
        hwnd = 0;
        flags = GetEnumerationFlags(_hiddenItemsPreference, ReadShowAllObjectsSetting());
        return 0;
    }

    internal static uint GetEnumerationFlags(bool showHiddenItems) =>
        0x60u | (showHiddenItems ? IncludeHiddenItemsFlag : 0); // SHCONTF_FOLDERS | SHCONTF_NONFOLDERS

    internal static uint GetEnumerationFlags(HiddenItemsPreference preference, bool windowsShowsHiddenItems) =>
        GetEnumerationFlags(preference switch
        {
            HiddenItemsPreference.FollowWindows => windowsShowsHiddenItems,
            HiddenItemsPreference.Show => true,
            HiddenItemsPreference.Hide => false,
            _ => windowsShowsHiddenItems
        });

    internal static bool ShouldAttachFilter(bool hasFilter, bool searchActive,
        HiddenItemsPreference hiddenItemsPreference) =>
        (!searchActive && hasFilter) || hiddenItemsPreference != HiddenItemsPreference.FollowWindows;

    internal static bool ShouldApplyQuickFilter(bool searchActive, bool hasFilter) =>
        !searchActive && hasFilter;

    internal static bool ReadShowAllObjectsSetting()
    {
        uint shellSettings = 0;
        SHGetSettings(ref shellSettings, ShellShowAllObjectsMask);
        return (shellSettings & ShellShowAllObjectsMask) != 0;
    }

    private static bool TryGetFolderState(nint shellFolder, nint itemPidl, out bool isFolder)
    {
        isFolder = false;
        // GetAttributesOf needs a PIDL pointer and a UINT mask; keep their per-item buffers together.
        var buffer = Marshal.AllocHGlobal(nint.Size + sizeof(uint));
        var attributes = IntPtr.Add(buffer, nint.Size);
        try
        {
            Marshal.WriteIntPtr(buffer, itemPidl);
            Marshal.WriteInt32(attributes, unchecked((int)FolderAttribute));
            var hr = GetMethod<GetAttributesOf>(shellFolder, 9)(shellFolder, 1, buffer, attributes);
            if (hr < 0) return false;
            isFolder = (unchecked((uint)Marshal.ReadInt32(attributes)) & FolderAttribute) != 0;
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool TryGetName(nint shellFolder, nint itemPidl, out string name)
    {
        name = string.Empty;
        var strretSize = StrRetBufferSize;
        var strret = Marshal.AllocHGlobal(strretSize);
        try
        {
            Marshal.Copy(new byte[strretSize], 0, strret, strretSize);
            var hr = GetMethod<GetDisplayNameOf>(shellFolder, 11)(shellFolder, itemPidl, DisplayNameFlags, strret);
            if (hr < 0) return false;
            var text = new StringBuilder(32768);
            if (StrRetToBuf(strret, itemPidl, text, (uint)text.Capacity) < 0) return false;
            name = text.ToString();
            return name.Length > 0;
        }
        finally { Marshal.FreeHGlobal(strret); }
    }

    private static T GetMethod<T>(nint instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAttributesOf(nint self, uint count, nint childPidls, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDisplayNameOf(nint self, nint childPidl, uint flags, nint result);

    [DllImport("shlwapi.dll", EntryPoint = "StrRetToBufW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrRetToBuf(nint strret, nint childPidl, StringBuilder buffer, uint capacity);

    [DllImport("shell32.dll", EntryPoint = "SHGetSettings", ExactSpelling = true)]
    private static extern void SHGetSettings(ref uint shellSettings, uint mask);
}
