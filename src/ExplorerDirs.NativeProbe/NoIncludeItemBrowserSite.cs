using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Diagnostic-only clone of the production browser site with GetViewFlags=0.
// It isolates CDB2GVF_NOINCLUDEITEM without changing the shipping implementation.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class NoIncludeItemBrowserSite : IShellServiceProvider, IComDialogBrowser, IComDialogBrowser2, IComDialogBrowser3
{
    internal int ViewFlagRequests { get; private set; }

    public int QueryService(ref Guid service, ref Guid iid, out nint result)
    {
        result = 0;
        if (service != ShellBrowserSite.BrowserFrameServiceId)
            return unchecked((int)0x80004002);
        var unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, in iid, out result); }
        finally { Marshal.Release(unknown); }
    }

    public int OnDefaultCommand(nint shellView) => 1;
    public int OnStateChange(nint shellView, uint change) => 0;
    public int IncludeObject(nint shellView, nint childPidl) => 0;
    public int Notify(nint shellView, uint notification) => 0;
    public int GetDefaultMenuText(nint shellView, nint text, int capacity) => 1;
    public int GetViewFlags(out uint flags) { ViewFlagRequests++; flags = 0; return 0; }
    public int OnColumnClicked(nint shellView, int column) => 0;
    public int GetCurrentFilter(nint text, int capacity)
    {
        if (text == 0 || capacity <= 0) return unchecked((int)0x80070057);
        Marshal.WriteInt16(text, 0);
        return 0;
    }
    public int OnPreViewCreated(nint shellView) => 0;
}
