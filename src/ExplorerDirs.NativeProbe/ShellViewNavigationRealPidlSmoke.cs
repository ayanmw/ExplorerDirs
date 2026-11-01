using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.NativeProbe;

// Exercises navigation-history PIDL ownership with real Shell identities without creating a view.
internal static class ShellViewNavigationRealPidlSmoke
{
    private const uint Parent = 0x2000;
    private const uint Relative = 0x1000;
    private const uint Back = 0x4000;
    private const uint Forward = 0x8000;

    internal static int Run()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RealPidlHistory-" + Guid.NewGuid().ToString("N"));
        var childPath = Path.Combine(rootPath, "child");
        var nestedPath = Path.Combine(childPath, "nested");
        var invalidPath = Path.Combine(rootPath, "not-a-folder.txt");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(invalidPath, "not a folder");

        nint root = 0;
        nint child = 0;
        nint nested = 0;
        nint invalid = 0;
        nint relativeNested = 0;
        try
        {
            root = ParseFolder(rootPath);
            child = ParseFolder(childPath);
            nested = ParseFolder(nestedPath);
            invalid = ParseItem(invalidPath);
            relativeNested = Clone(ILFindLastID(nested));
            Check(relativeNested != 0, "the real nested PIDL exposes its final relative component");

            var navigatedPaths = new List<string>();
            using var history = new ShellViewNavigationHistory(root, Clone, Free, Equal, GetParent, Combine,
                (target, _) =>
                {
                    var path = GetPath(target);
                    if (!Directory.Exists(path)) return unchecked((int)0x80070057);
                    navigatedPaths.Add(path);
                    return 0;
                });

            Check(history.BrowseObject(child, 0) == 0 && Equal(history.CurrentPidl, child),
                "absolute navigation retains the real Shell child-folder PIDL");
            Check(history.BrowseObject(relativeNested, Relative) == 0 && Equal(history.CurrentPidl, nested),
                "relative navigation combines the real child component with the current absolute PIDL");
            Check(history.BrowseObject(0, Parent) == 0 && Equal(history.CurrentPidl, child),
                "parent navigation resolves and retains the real parent folder PIDL");
            Check(history.BrowseObject(0, Back) == 0 && Equal(history.CurrentPidl, nested) &&
                  history.BrowseObject(0, Forward) == 0 && Equal(history.CurrentPidl, child),
                "back and forward replay retained real Shell folder identities");

            var countBeforeFailure = history.Count;
            var indexBeforeFailure = history.Index;
            var currentBeforeFailure = Clone(history.CurrentPidl);
            try
            {
                Check(history.BrowseObject(invalid, 0) < 0 && history.Count == countBeforeFailure &&
                      history.Index == indexBeforeFailure && Equal(history.CurrentPidl, currentBeforeFailure),
                    "a file rejected as a folder leaves the real-PIDL history unchanged");
            }
            finally { Free(currentBeforeFailure); }

            VerifyComHostBrowseObject(root, child, nested);

            Console.WriteLine($"real-shell-pidl-navigation: history-count={history.Count}; current={GetPath(history.CurrentPidl)}; " +
                $"successful-navigations={navigatedPaths.Count}; shell-view-created=False");
            return 0;
        }
        finally
        {
            Free(relativeNested);
            Free(invalid);
            Free(nested);
            Free(child);
            Free(root);
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    private static nint ParseFolder(string path)
    {
        var pidl = ParseItem(path);
        if (!Directory.Exists(path))
        {
            Free(pidl);
            throw new DirectoryNotFoundException(path);
        }
        return pidl;
    }

    private static nint ParseItem(string path)
    {
        var hr = SHParseDisplayName(path, 0, out var pidl, 0, out _);
        if (hr < 0 || pidl == 0) Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
        return pidl;
    }

    private static nint Clone(nint pidl) => ILClone(pidl);
    private static void Free(nint pidl) { if (pidl != 0) ILFree(pidl); }
    private static bool Equal(nint left, nint right) => ILIsEqual(left, right);
    private static nint Combine(nint parent, nint relative) => ILCombine(parent, relative);

    private static nint GetParent(nint pidl)
    {
        var parent = Clone(pidl);
        if (parent == 0 || ILRemoveLastID(parent)) return parent;
        Free(parent);
        return 0;
    }

    private static string GetPath(nint pidl)
    {
        var path = new StringBuilder(32768);
        if (!SHGetPathFromIDListW(pidl, path))
            throw new InvalidOperationException("The Shell PIDL did not resolve to a filesystem path.");
        return path.ToString();
    }

    private static void VerifyComHostBrowseObject(nint root, nint child, nint nested)
    {
        var history = new ShellViewNavigationHistory(root, Clone, Free, Equal, GetParent, Combine,
            (target, _) => Directory.Exists(GetPath(target)) ? 0 : unchecked((int)0x80070057));
        using var host = new StreamViewHost(0, [], navigation: history);
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var vtable = Marshal.ReadIntPtr(browser);
            var browse = Marshal.GetDelegateForFunctionPointer<BrowseObjectCallback>(
                Marshal.ReadIntPtr(vtable + 11 * nint.Size));
            var relative = Clone(ILFindLastID(nested));
            try
            {
                Check(browse(browser, child, 0) == 0 && Equal(history.CurrentPidl, child) &&
                      browse(browser, relative, Relative) == 0 && Equal(history.CurrentPidl, nested),
                    "the isolated COM browser callback routes absolute and relative real-folder navigation");
            }
            finally { Free(relative); }
        }
        finally { Marshal.Release(browser); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowseObjectCallback(nint self, nint pidl, uint flags);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Real Shell PIDL navigation failed: " + message + ".");
        Console.WriteLine("PASS " + message);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl,
        uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(nint pidl, StringBuilder path);
    [DllImport("shell32.dll", EntryPoint = "ILClone")]
    private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll", EntryPoint = "ILFree")]
    private static extern void ILFree(nint pidl);
    [DllImport("shell32.dll", EntryPoint = "ILIsEqual")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILIsEqual(nint left, nint right);
    [DllImport("shell32.dll", EntryPoint = "ILCombine")]
    private static extern nint ILCombine(nint parent, nint relative);
    [DllImport("shell32.dll", EntryPoint = "ILRemoveLastID")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILRemoveLastID(nint pidl);
    [DllImport("shell32.dll", EntryPoint = "ILFindLastID")]
    private static extern nint ILFindLastID(nint pidl);
}
