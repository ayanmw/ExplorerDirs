using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// DefView asks the ExplorerBrowser frame for ICommDlgBrowser2. Without the
// NOINCLUDEITEM flag, search-folder filtering may run on the UI thread.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellBrowserSite : IShellServiceProvider, IComDialogBrowser, IComDialogBrowser2
{
    internal static readonly Guid BrowserFrameServiceId = new("000214F1-0000-0000-C000-000000000046");
    internal static readonly Guid DialogBrowser2Id = new("10339516-2894-11D2-9039-00C04F8EEB3E");
    internal const uint NoIncludeItemFlag = 0x00000010;

    public int QueryService(ref Guid service, ref Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (service != BrowserFrameServiceId) return unchecked((int)0x80004002); // E_NOINTERFACE
        var unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, in iid, out result); }
        finally { Marshal.Release(unknown); }
    }

    // S_FALSE leaves file opening and context-menu text to the native view.
    public int OnDefaultCommand(IntPtr shellView) => 1;
    public int OnStateChange(IntPtr shellView, uint change) => 0;
    public int IncludeObject(IntPtr shellView, IntPtr childPidl) => 0;
    public int Notify(IntPtr shellView, uint notification) => 0;
    public int GetDefaultMenuText(IntPtr shellView, IntPtr text, int capacity) => 1;
    public int GetViewFlags(out uint flags) { flags = NoIncludeItemFlag; return 0; }

    internal static void SetSite(object browser, ShellBrowserSite? site)
    {
        var hr = IUnknown_SetSite(browser, site);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("shlwapi.dll", PreserveSig = true)]
    private static extern int IUnknown_SetSite(
        [MarshalAs(UnmanagedType.IUnknown)] object browser,
        [MarshalAs(UnmanagedType.IUnknown)] object? site);
}

[ComVisible(true), Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
}

[ComVisible(true), Guid("000214F1-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComDialogBrowser
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView, uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView, IntPtr childPidl);
}

[ComVisible(true), Guid("10339516-2894-11D2-9039-00C04F8EEB3E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComDialogBrowser2
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView, uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView, IntPtr childPidl);
    [PreserveSig] int Notify(IntPtr shellView, uint notification);
    [PreserveSig] int GetDefaultMenuText(IntPtr shellView, IntPtr text, int capacity);
    [PreserveSig] int GetViewFlags(out uint flags);
}
