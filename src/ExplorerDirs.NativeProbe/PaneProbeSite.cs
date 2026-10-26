using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Experimental host only. Native callbacks/child HWNDs establish capability,
// not a usable App tree/preview UI or keyboard traversal.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class PaneProbeSite : IShellServiceProvider, IProbePaneVisibility
{
    private readonly ShellBrowserSite _baseSite = new();
    private static readonly Guid PaneService = typeof(IProbePaneVisibility).GUID;
    private static readonly Guid NavigationPane = new("cb316b22-25f7-42b8-8a09-540d23a43c2f");
    private static readonly Guid PreviewPane = new("893c63d1-45c8-4d17-be19-223be71be365");
    private static readonly HashSet<Guid> OtherPanes =
    [
        new("d9745868-ca5f-4a76-91cd-f5a129fbb076"), // Commands
        new("72e81700-e3ec-4660-bf24-3c3b7b648806"),
        new("21f7c32d-eeaa-439b-bb51-37b96fd6a943"),
        new("43abf98b-89b8-472d-b9ce-e69b8229f019"), // Details
        new("65bcde4f-4f07-4f27-83a7-1afca4df7ddd"),
        new("b4e9db8b-34ba-4c39-b5cc-16a1bd2c411c"),
        new("65fe56ce-5cfe-4bc4-ad8a-7ae3fe7e8f7c"),
        new("d27524a8-c9f2-4834-a106-df8889fd4f37")
    ];
    internal bool Enabled = true;
    internal int NavigationRequests, PreviewRequests;

    public int QueryService(ref Guid service, ref Guid iid, out nint result)
    {
        if (service != PaneService) return _baseSite.QueryService(ref service, ref iid, out result);
        var unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, in iid, out result); }
        finally { Marshal.Release(unknown); }
    }

    public int GetPaneState(ref Guid pane, out uint state)
    {
        state = 0; // EPS_DONTCARE for unknown pane GUIDs.
        if (pane == NavigationPane || pane == PreviewPane)
        {
            if (pane == NavigationPane) NavigationRequests++;
            else PreviewRequests++;
            state = 0x20000u | (Enabled ? 1u : 2u); // FORCE + ON/OFF, ignore persisted defaults.
        }
        else if (OtherPanes.Contains(pane)) state = 0x20002;
        return 0;
    }

    internal void Report(string stage) =>
        Console.WriteLine($"{stage}: requested-on={Enabled}; nav-callbacks={NavigationRequests}; preview-callbacks={PreviewRequests}");

    [DllImport("shlwapi.dll", PreserveSig = true)]
    internal static extern int IUnknown_SetSite(nint browser,
        [MarshalAs(UnmanagedType.IUnknown)] object? site);
}

[ComVisible(true), Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbePaneVisibility
{
    [PreserveSig] int GetPaneState(ref Guid pane, out uint state);
}
