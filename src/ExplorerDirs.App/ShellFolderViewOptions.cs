using System.Runtime.InteropServices;

internal static class ShellFolderViewOptions
{
    internal const uint CustomPosition = 0x00000002;
    internal const uint HeaderSuppressionMask = 0x01800000;
    internal const uint CheckSelect = 0x00040000;
    internal const uint FullRowSelect = 0x00200000;
    internal const uint NoGrouping = 0x00100000;

    private static readonly Guid FolderViewOptionsId = new("3CC974D2-B302-4D36-AD3E-06D93F695D3F");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private const int EFail = unchecked((int)0x80004005);

    internal static uint EnableCustomPosition(uint options) => options | CustomPosition;

    internal static uint EnableAllViewHeaders(uint folderFlags) => folderFlags & ~HeaderSuppressionMask;

    internal static uint SetCheckSelect(uint folderFlags, bool enabled) =>
        enabled ? folderFlags | CheckSelect : folderFlags & ~CheckSelect;

    internal static uint SetFullRowSelect(uint folderFlags, bool enabled) =>
        enabled ? folderFlags | FullRowSelect : folderFlags & ~FullRowSelect;

    internal static uint SetNoGrouping(uint folderFlags, bool disabled) =>
        disabled ? folderFlags | NoGrouping : folderFlags & ~NoGrouping;

    internal static int EnableCustomPosition(IntPtr browserUnknown) =>
        EnableCustomPosition(browserUnknown, out _, out _);

    internal static int EnableCustomPosition(IntPtr browserUnknown, out uint originalOptions, out uint updatedOptions)
    {
        originalOptions = 0;
        updatedOptions = 0;
        var result = Marshal.QueryInterface(browserUnknown, in FolderViewOptionsId, out var options);
        if (result < 0) return result;
        if (options == IntPtr.Zero) return EFail;
        try
        {
            var get = Method<GetFolderViewOptions>(options, 4);
            var set = Method<SetFolderViewOptions>(options, 3);
            result = get(options, out originalOptions);
            if (result < 0) return result;
            result = set(options, CustomPosition, EnableCustomPosition(originalOptions));
            if (result < 0) return result;
            result = get(options, out updatedOptions);
            if (result < 0) return result;
            return (updatedOptions & CustomPosition) != 0 ? 0 : EFail;
        }
        finally { Marshal.Release(options); }
    }

    internal static int EnableAllViewHeaders(IntPtr shellView) =>
        EnableAllViewHeaders(shellView, out _, out _);

    internal static int EnableAllViewHeaders(IntPtr shellView, out uint originalFolderFlags, out uint updatedFolderFlags)
    {
        originalFolderFlags = 0;
        updatedFolderFlags = 0;
        var result = Marshal.QueryInterface(shellView, in FolderView2Id, out var folderView);
        if (result < 0) return result;
        if (folderView == IntPtr.Zero) return EFail;
        try
        {
            var get = Method<GetCurrentFolderFlags>(folderView, 25);
            var set = Method<SetCurrentFolderFlags>(folderView, 24);
            result = get(folderView, out originalFolderFlags);
            if (result < 0) return result;

            var updated = EnableAllViewHeaders(originalFolderFlags);
            if (updated == originalFolderFlags)
            {
                updatedFolderFlags = originalFolderFlags;
                return 0;
            }
            result = set(folderView, HeaderSuppressionMask, updated);
            if (result < 0) return result;
            result = get(folderView, out updatedFolderFlags);
            if (result < 0) return result;
            var headersEnabled = (updatedFolderFlags & HeaderSuppressionMask) == 0;
            var unrelatedFlagsPreserved = ((originalFolderFlags ^ updatedFolderFlags) & ~HeaderSuppressionMask) == 0;
            return headersEnabled && unrelatedFlagsPreserved ? 0 : EFail;
        }
        finally { Marshal.Release(folderView); }
    }

    internal static int SetCheckSelect(IntPtr shellView, bool enabled)
        => SetFolderFlag(shellView, CheckSelect, enabled);

    internal static int SetFullRowSelect(IntPtr shellView, bool enabled)
        => SetFolderFlag(shellView, FullRowSelect, enabled);

    internal static int SetNoGrouping(IntPtr shellView, bool disabled)
        => SetFolderFlag(shellView, NoGrouping, disabled);

    private static int SetFolderFlag(IntPtr shellView, uint flag, bool enabled)
    {
        var result = Marshal.QueryInterface(shellView, in FolderView2Id, out var folderView);
        if (result < 0) return result;
        if (folderView == IntPtr.Zero) return EFail;
        try
        {
            var get = Method<GetCurrentFolderFlags>(folderView, 25);
            var set = Method<SetCurrentFolderFlags>(folderView, 24);
            result = get(folderView, out var original);
            if (result < 0) return result;
            var updated = enabled ? original | flag : original & ~flag;
            if (updated == original) return 0;
            result = set(folderView, flag, updated);
            if (result < 0) return result;
            result = get(folderView, out var actual);
            if (result < 0) return result;
            return (actual & flag) == (updated & flag) &&
                   ((original ^ actual) & ~flag) == 0 ? 0 : EFail;
        }
        finally { Marshal.Release(folderView); }
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolderViewOptions(IntPtr self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderViewOptions(IntPtr self, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentFolderFlags(IntPtr self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetCurrentFolderFlags(IntPtr self, uint mask, uint flags);
}
