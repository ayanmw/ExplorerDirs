using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class ShellTreeIconHostProbe
{
    private const uint SystemImageIndex = 0x00004000;
    private const uint SmallIcon = 0x00000001;
    private const uint TvifImage = 0x00000002;
    private const uint TvifSelectedImage = 0x00000020;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "tree-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "child"));
        using var source = new HwndSource(new HwndSourceParameters("Hidden ShellTreeHost icon probe")
        {
            WindowStyle = unchecked((int)0x80000000),
            Width = 360,
            Height = 260
        });
        object? host = null;
        try
        {
            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var hostType = appAssembly.GetType("ExplorerDirs.App.ShellTreeHost", true)!;
            host = Activator.CreateInstance(hostType, nonPublic: true)!;
            source.RootVisual = (Visual)host;
            Pump(TimeSpan.FromMilliseconds(200));
            var tree = hostType.GetField("_tree", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)
                ?? throw new InvalidOperationException("Production ShellTreeHost did not create NativeShellTree.");
            var treeType = tree.GetType();
            treeType.GetMethod("SetRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tree, [fixture, null]);
            Pump(TimeSpan.FromMilliseconds(250));

            var treeWindow = (nint)treeType.GetProperty("WindowHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tree)!;
            var control = FindWindowExW(treeWindow, 0, "SysTreeView32", null);
            var root = SendMessageW(control, 0x110a, 0, 0); // TVM_GETNEXTITEM / TVGN_ROOT.
            if (control == 0 || root == 0) throw new InvalidOperationException("The hosted Shell tree has no root row.");

            var item = new TreeItem { Mask = TvifImage | TvifSelectedImage, Item = root };
            if (SendMessageW(control, 0x113e, 0, ref item) == 0)
                throw new InvalidOperationException("TVM_GETITEM did not return the hosted tree root image indices.");
            var expected = GetIconIndex(fixture);
            _ = SendMessageW(control, 0x1102, 2, root); // TVM_EXPAND / TVE_EXPAND.
            Pump(TimeSpan.FromMilliseconds(150));
            var child = SendMessageW(control, 0x110a, 4, root); // TVM_GETNEXTITEM / TVGN_CHILD.
            if (child == 0) throw new InvalidOperationException("The hosted Shell tree did not enumerate its child folder.");
            var childItem = new TreeItem { Mask = TvifImage | TvifSelectedImage, Item = child };
            if (SendMessageW(control, 0x113e, 0, ref childItem) == 0)
                throw new InvalidOperationException("TVM_GETITEM did not return the hosted tree child image indices.");
            var expectedChild = GetIconIndex(Path.Combine(fixture, "child"));
            var systemList = (nint)appAssembly
                .GetType("ExplorerDirs.App.ShellTreeIconIndexResolver", true)!
                .GetMethod("GetSystemImageList", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null)!;
            var boundList = SendMessageW(control, 0x1108, 0, 0); // TVM_GETIMAGELIST / TVSIL_NORMAL.
            var count = systemList == 0 ? 0 : ImageList_GetImageCount(systemList);
            var client = new Rect();
            if (!GetClientRect(control, out client)) throw new InvalidOperationException("Could not query the hosted tree bounds.");
            var pixels = CaptureRootIcon(control, systemList, item.Image, client);
            var foregroundUnchanged = GetForegroundWindow() == foreground;
            Console.WriteLine($"shell-tree-host-icons: root={item.Image}/{item.SelectedImage};expected-root={expected};child={childItem.Image}/{childItem.SelectedImage};expected-child={expectedChild};image-count={count};bound-system-list={boundList == systemList};client={client.Right}x{client.Bottom};icon-pixel-match={pixels.Match}/{pixels.Template};foreground-unchanged={foregroundUnchanged}");

            if (!foregroundUnchanged || systemList == 0 || boundList != systemList || item.Image != expected ||
                childItem.Image != expectedChild || item.Image < 0 || item.Image >= count ||
                childItem.Image < 0 || childItem.Image >= count || pixels.Template < 12 || pixels.Match * 4 < pixels.Template * 3)
                throw new InvalidOperationException("The WPF-hosted Shell tree did not render its resolved system folder icon into the expected row.");
            return 0;
        }
        finally
        {
            source.RootVisual = null;
            GC.KeepAlive(host);
            source.Dispose();
            try { Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The hidden Shell tree icon probe changed the user's foreground window.");
        }
    }

    private static (int Match, int Template) CaptureRootIcon(nint control, nint imageList, int iconIndex, Rect client)
    {
        if (client.Right < 64 || client.Bottom < 24) throw new InvalidOperationException("The hosted Shell tree is too small for icon capture.");
        var screenDc = GetDC(control);
        if (screenDc == 0) throw new InvalidOperationException("Could not acquire the hosted tree DC.");
        nint targetDc = 0;
        nint targetBitmap = 0;
        nint iconDc = 0;
        nint iconBitmap = 0;
        nint brush = 0;
        nint oldTarget = 0;
        nint oldIcon = 0;
        try
        {
            targetDc = CreateCompatibleDC(screenDc);
            targetBitmap = CreateCompatibleBitmap(screenDc, client.Right, client.Bottom);
            iconDc = CreateCompatibleDC(screenDc);
            if (targetDc == 0 || targetBitmap == 0 || iconDc == 0)
                throw new InvalidOperationException("Could not allocate the hosted tree icon pixel targets.");
            oldTarget = SelectObject(targetDc, targetBitmap);
            var background = GetSysColor(5); // COLOR_WINDOW.
            brush = CreateSolidBrush(background);
            if (oldTarget == 0 || brush == 0) throw new InvalidOperationException("Could not initialize the hosted tree pixel target.");
            var bounds = new Rect { Right = client.Right, Bottom = client.Bottom };
            _ = FillRect(targetDc, ref bounds, brush);
            _ = SendMessageW(control, 0x0318, targetDc, 0x0000000c); // WM_PRINTCLIENT / PRF_CLIENT | PRF_ERASEBKGND.

            if (!ImageList_GetIconSize(imageList, out var iconWidth, out var iconHeight) || iconWidth <= 0 || iconHeight <= 0)
                throw new InvalidOperationException("Could not query the Shell icon template dimensions.");
            iconBitmap = CreateCompatibleBitmap(screenDc, iconWidth, iconHeight);
            if (iconBitmap == 0) throw new InvalidOperationException("Could not allocate the Shell icon template bitmap.");
            oldIcon = SelectObject(iconDc, iconBitmap);
            if (oldIcon == 0) throw new InvalidOperationException("Could not select the Shell icon template bitmap.");
            try
            {
                var iconBounds = new Rect { Right = iconWidth, Bottom = iconHeight };
                _ = FillRect(iconDc, ref iconBounds, brush);
                if (!ImageList_Draw(imageList, iconIndex, iconDc, 0, 0, 1))
                    throw new InvalidOperationException("The Shell image list could not draw the resolved folder icon.");
                var template = new List<(int X, int Y, uint Color)>();
                for (var y = 0; y < iconHeight; y++)
                for (var x = 0; x < iconWidth; x++)
                {
                    var color = GetPixel(iconDc, x, y);
                    if (color != background) template.Add((x, y, color));
                }
                var best = 0;
                for (var y = 0; y <= Math.Min(16, client.Bottom - iconHeight); y++)
                for (var x = 0; x <= Math.Min(56, client.Right - iconWidth); x++)
                {
                    var matches = 0;
                    foreach (var pixel in template)
                        if (GetPixel(targetDc, x + pixel.X, y + pixel.Y) == pixel.Color) matches++;
                    best = Math.Max(best, matches);
                }
                return (best, template.Count);
            }
            finally
            {
                if (oldIcon != 0) _ = SelectObject(iconDc, oldIcon);
                if (iconBitmap != 0) _ = DeleteObject(iconBitmap);
            }
        }
        finally
        {
            if (brush != 0) _ = DeleteObject(brush);
            if (oldTarget != 0) _ = SelectObject(targetDc, oldTarget);
            if (targetBitmap != 0) _ = DeleteObject(targetBitmap);
            if (iconDc != 0) _ = DeleteDC(iconDc);
            if (targetDc != 0) _ = DeleteDC(targetDc);
            _ = ReleaseDC(control, screenDc);
        }
    }

    private static int GetIconIndex(string path)
    {
        var size = (uint)Marshal.SizeOf<ShellFileInfo>();
        if (SHGetFileInfo(path, 0, out var info, size, SystemImageIndex | SmallIcon) == 0 || info.Index < 0)
            throw new InvalidOperationException("SHGetFileInfo could not resolve the fixture folder icon.");
        return info.Index;
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TreeItem
    {
        public uint Mask;
        public nint Item;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextCapacity;
        public int Image;
        public int SelectedImage;
        public int ChildCount;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref TreeItem item);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImageList_Draw(nint list, int index, nint dc, int x, int y, uint style);
    [DllImport("comctl32.dll")] private static extern int ImageList_GetImageCount(nint list);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImageList_GetIconSize(nint list, out int width, out int height);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")] private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
}
