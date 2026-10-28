using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class NativeShellTreeIconFallbackSmoke
{
    internal static void Run()
    {
        var folder = Path.GetTempPath();
        Check(ShellTreeIconIndexResolver.TryResolve(folder, out var normal, out var open),
            "SHGetFileInfo resolves the production Shell tree icon indices for a real folder");
        Check(normal >= 0 && open >= 0,
            $"Shell tree image-list indices are nonnegative (normal={normal}, open={open})");
        var systemImageList = ShellTreeIconIndexResolver.GetSystemImageList();
        Check(systemImageList != 0, "the system image list used to resolve Shell indices is available");
        var imageCount = ImageList_GetImageCount(systemImageList);
        Check(normal < imageCount && open < imageCount,
            $"resolved folder icons exist in the system image list (normal={normal}, open={open}, count={imageCount})");
        Console.WriteLine($"Native Shell tree icon smoke: PASS (normal={normal}, open={open}, system-image-count={imageCount})");

        foreach (var shellFolder in new[]
                 {
                     "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
                     "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"  // Recycle Bin
                 })
        {
            var shellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Check(SHCreateItemFromParsingName(shellFolder, 0, in shellItemId, out var shellItem) >= 0 && shellItem != 0,
                $"Windows creates an IShellItem for virtual folder {shellFolder}");
            try
            {
                Check(TryGetPidlIconIndices(shellFolder, out var expectedNormal, out var expectedOpen),
                    $"Windows resolves icon indices from the absolute PIDL for {shellFolder}");
                var events = new NativeTreeEvents(() => { }, () => { }, (_, _, _) => false);
                Check(events.OnGetDefaultIconIndex(shellItem, out var actualNormal, out var actualOpen) == 0,
                    $"production NamespaceTreeControl icon callback resolves virtual folder {shellFolder}");
                Check(actualNormal == expectedNormal && actualOpen == expectedOpen,
                    $"virtual folder icon indices match the Shell PIDL (actual={actualNormal}/{actualOpen}, expected={expectedNormal}/{expectedOpen})");
                Check(actualNormal < imageCount && actualOpen < imageCount,
                    $"virtual folder icon indices exist in the bound system image list (normal={actualNormal}, open={actualOpen}, count={imageCount})");
                Console.WriteLine($"Native Shell tree virtual icon smoke: PASS ({shellFolder}; normal={actualNormal}; open={actualOpen}; system-image-count={imageCount})");
            }
            finally { System.Runtime.InteropServices.Marshal.Release(shellItem); }
        }

        RunHiddenTreeControlImageListSmoke();

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static void RunHiddenTreeControlImageListSmoke()
    {
        var foregroundBefore = GetForegroundWindow();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var parent = new System.Windows.Interop.HwndSource(
                    new System.Windows.Interop.HwndSourceParameters("ExplorerDirs.HiddenShellTreeIconSmoke")
                    {
                        Width = 320,
                        Height = 240,
                        WindowStyle = 0 // Keep the parent off-screen and non-visible.
                    });
                using var tree = new NativeShellTree(parent.Handle, 320, 240);
                var treeView = FindWindowEx(tree.WindowHandle, 0, "SysTreeView32", null);
                Require(treeView != 0, "the production NamespaceTreeControl exposes its native tree view");
                var boundImageList = SendMessage(treeView, 0x1108, 0, 0); // TVM_GETIMAGELIST / TVSIL_NORMAL
                Require(boundImageList == ShellTreeIconIndexResolver.GetSystemImageList(),
                    "the production tree control binds the same system image list used by SHGetFileInfo icon indices");
                Require(GetForegroundWindow() == foregroundBefore,
                    "creating the hidden production tree control leaves the user's foreground window unchanged");
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true, Name = "ExplorerDirs hidden Shell tree icon smoke" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Hidden Shell tree icon integration smoke failed.", failure);
        Require(GetForegroundWindow() == foregroundBefore,
            "disposing the hidden production tree control leaves the user's foreground window unchanged");
        Console.WriteLine("Native Shell tree control image-list smoke: PASS (hidden parent; foreground unchanged)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool TryGetPidlIconIndices(string parsingName, out int normal, out int open)
    {
        normal = open = -1;
        if (SHParseDisplayName(parsingName, 0, out var pidl, 0, out _) < 0 || pidl == 0) return false;
        try
        {
            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ShellFileInfo>();
            if (SHGetFileInfo(pidl, 0, out var closed, size, 0x00004000 | 0x00000001 | 0x00000008) == 0 || closed.Index < 0)
                return false;
            normal = closed.Index;
            if (SHGetFileInfo(pidl, 0, out var expanded, size,
                    0x00004000 | 0x00000001 | 0x00000002 | 0x00000008) != 0 && expanded.Index >= 0)
                open = expanded.Index;
            else
                open = normal;
            return true;
        }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl); }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string name, nint bindContext, in Guid interfaceId, out nint shellItem);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern nint SHGetFileInfo(nint pidl, uint attributes, out ShellFileInfo info, uint infoSize, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern int ImageList_GetImageCount(nint imageList);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
