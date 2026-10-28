using System.Runtime.InteropServices;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class FilterAttachmentSmoke
{
    internal static void Run()
    {
        var opaque = Marshal.GetIUnknownForObject(new object());
        var siteObject = new FakeFilterSite();
        var site = Marshal.GetIUnknownForObject(siteObject);
        var filter = Marshal.GetIUnknownForObject(new ShellFolderFilter(QuickFilterSettings.Empty));
        try
        {
            ExpectFailure(() => ShellFilterAttachment.Apply(opaque, 0), unchecked((int)0x80004002),
                "a missing filter site must not report successful attachment");
            ShellFilterAttachment.Apply(site, filter);
            Check(siteObject.Calls == 1 && siteObject.LastFilter == filter, "the actual COM site slot receives the filter identity");
            ShellFilterAttachment.Apply(site, 0);
            Check(siteObject.Calls == 2 && siteObject.LastFilter == 0, "null filter detaches through the same native slot");
            siteObject.Result = unchecked((int)0x80004005);
            ExpectFailure(() => ShellFilterAttachment.Apply(site, filter), siteObject.Result,
                "the native SetFilter failure is returned to the caller");
            var calls = siteObject.Calls;
            ExpectFailure(() => ShellFilterAttachment.Apply(site, opaque), unchecked((int)0x80004002),
                "an object without IFolderFilter is rejected before SetFilter");
            Check(siteObject.Calls == calls, "invalid filter interfaces never reach the site");
        }
        finally
        {
            Marshal.Release(filter);
            Marshal.Release(site);
            Marshal.Release(opaque);
            GC.KeepAlive(siteObject);
        }
        Console.WriteLine("Filter attachment smoke: PASS (managed COM sites; no Shell view created)");
    }

    private static void ExpectFailure(Action action, int result, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex.HResult == result) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}

[ComVisible(true), Guid("C0A651F5-B48B-11D2-B5ED-006097C686F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITestFolderFilterSite
{
    [PreserveSig] int SetFilter(nint filter);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class FakeFilterSite : ITestFolderFilterSite
{
    internal int Calls;
    internal nint LastFilter;
    internal int Result;
    public int SetFilter(nint filter) { Calls++; LastFilter = filter; return Result; }
}
