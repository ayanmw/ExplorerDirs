using System.Runtime.InteropServices;
using System.Text;

internal static class ShellFrameProbe
{
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");

    internal static void ReportCurrentViewHeader(nint browser, string stage)
    {
        var iid = ShellViewId;
        var result = Method<GetCurrentView>(browser, 17)(browser, ref iid, out var view);
        if (result < 0 || view == 0)
        {
            Console.WriteLine($"{stage}: shell-view=0x{result:X8}; header=<unavailable>");
            return;
        }
        nint folderView = 0;
        try
        {
            var folderViewId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            var queryResult = Marshal.QueryInterface(view, in folderViewId, out folderView);
            uint folderFlags = 0;
            var flagsResult = queryResult < 0 || folderView == 0
                ? queryResult
                : Method<GetFolderFlags>(folderView, 25)(folderView, out folderFlags);
            result = Method<GetWindow>(view, 3)(view, out var window);
            if (result < 0 || window == 0)
            {
                Console.WriteLine($"{stage}: view-window=0x{result:X8}; folder-flags=0x{flagsResult:X8}/0x{folderFlags:X8}; header=<unavailable>");
                return;
            }

            var headers = new List<(nint Handle, bool OwnVisible, int Width, int Height, int ColumnCount)>();
            EnumChildWindows(window, (child, _) =>
            {
                var name = new StringBuilder(128);
                if (GetClassNameW(child, name, name.Capacity) > 0 && name.ToString() == "SysHeader32")
                {
                    GetWindowRect(child, out var rect);
                    headers.Add((child, ((long)GetWindowLongPtrW(child, -16) & 0x10000000) != 0,
                        rect.Right - rect.Left, rect.Bottom - rect.Top,
                        unchecked((int)SendMessageW(child, 0x1200, 0, 0)))); // HDM_GETITEMCOUNT
                }
                return true;
            }, 0);

            Console.WriteLine($"{stage}: folder-flags=0x{flagsResult:X8}/0x{folderFlags:X8}; sysheader-count={headers.Count}; " +
                $"headers={string.Join('|', headers.Select(header =>
                    $"hwnd=0x{header.Handle.ToInt64():X},own-visible-style={header.OwnVisible},size={header.Width}x{header.Height},columns={header.ColumnCount}"))}");
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            Marshal.Release(view);
        }
    }

    internal static void ProbeHeaderClick(nint browser)
    {
        var iid = ShellViewId;
        var result = Method<GetCurrentView>(browser, 17)(browser, ref iid, out var view);
        if (result < 0 || view == 0) throw new COMException("Could not get the Shell view for header-click probe.", result);
        nint folderView = 0;
        try
        {
            var folderViewId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            result = Marshal.QueryInterface(view, in folderViewId, out folderView);
            if (result < 0 || folderView == 0) throw new COMException("Could not get IFolderView2 for header-click probe.", result);
            result = Method<GetWindow>(view, 3)(view, out var viewWindow);
            if (result < 0 || viewWindow == 0) throw new COMException("Could not get the view window for header-click probe.", result);
            nint header = 0;
            EnumChildWindows(viewWindow, (child, _) =>
            {
                var name = new StringBuilder(128);
                if (GetClassNameW(child, name, name.Capacity) > 0 && name.ToString() == "SysHeader32")
                {
                    header = child;
                    return false;
                }
                return true;
            }, 0);
            if (header == 0) throw new InvalidOperationException("No native SysHeader32 was found.");

            var owner = GetParent(header);
            var ownerClass = new StringBuilder(128);
            GetClassNameW(owner, ownerClass, ownerClass.Capacity);
            var itemCount = unchecked((int)SendMessageW(header, 0x1200, 0, 0)); // HDM_GETITEMCOUNT
            if (itemCount <= 0) throw new InvalidOperationException("The native header has no columns.");

            var before = ViewStyleProbe.ReadState(folderView);
            var notification = new NmHeader
            {
                Header = new NmHeaderInfo { Window = header, Id = unchecked((nuint)GetDlgCtrlID(header)), Code = -322 }, // HDN_ITEMCLICKW = HDN_FIRST - 22
                Item = 0,
                Button = 0
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NmHeader>());
            try
            {
                Marshal.StructureToPtr(notification, pointer, false);
                var returnValue = SendMessageW(owner, 0x004E, unchecked((nint)notification.Header.Id), pointer); // WM_NOTIFY
                PumpMessages();
                var after = ViewStyleProbe.ReadState(folderView);
                Console.WriteLine($"header-click-probe: header=0x{header.ToInt64():X}; parent={ownerClass}; columns={itemCount}; " +
                    $"notify-code={notification.Header.Code}; return=0x{returnValue.ToInt64():X}; before={before}; after={after}");
                if (before == after) throw new InvalidOperationException("Synthetic native header click did not change Shell sort state.");
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            Marshal.Release(view);
        }
    }

    internal static void ProbeHeaderMouseMessages(nint browser)
    {
        var iid = ShellViewId;
        var result = Method<GetCurrentView>(browser, 17)(browser, ref iid, out var view);
        if (result < 0 || view == 0) throw new COMException("Could not get the Shell view for header mouse probe.", result);
        nint folderView = 0;
        try
        {
            var folderViewId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            result = Marshal.QueryInterface(view, in folderViewId, out folderView);
            if (result < 0 || folderView == 0) throw new COMException("Could not get IFolderView2 for header mouse probe.", result);
            result = Method<GetWindow>(view, 3)(view, out var viewWindow);
            if (result < 0 || viewWindow == 0) throw new COMException("Could not get the view window for header mouse probe.", result);
            nint header = 0;
            EnumChildWindows(viewWindow, (child, _) =>
            {
                var name = new StringBuilder(128);
                if (GetClassNameW(child, name, name.Capacity) > 0 && name.ToString() == "SysHeader32")
                {
                    header = child;
                    return false;
                }
                return true;
            }, 0);
            if (header == 0) throw new InvalidOperationException("No native SysHeader32 was found.");
            var itemRect = new Rect();
            var rectPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
            try
            {
                Marshal.StructureToPtr(itemRect, rectPointer, false);
                Marshal.WriteInt32(rectPointer, 0, 0); // HDM_GETITEMRECT, first column
                if (SendMessageW(header, 0x1207, 0, rectPointer) == 0)
                    throw new InvalidOperationException("The hidden header did not return the first column rectangle.");
                itemRect = Marshal.PtrToStructure<Rect>(rectPointer);
            }
            finally { Marshal.FreeHGlobal(rectPointer); }

            var x = itemRect.Left + Math.Max(1, (itemRect.Right - itemRect.Left) / 2);
            var y = itemRect.Top + Math.Max(1, (itemRect.Bottom - itemRect.Top) / 2);
            var point = unchecked((nint)((y << 16) | (x & 0xFFFF)));
            var before = ViewStyleProbe.ReadState(folderView);
            SendMessageW(header, 0x0201, 1, point); // WM_LBUTTONDOWN, targeted to isolated SysHeader32
            SendMessageW(header, 0x0202, 0, point); // WM_LBUTTONUP
            PumpMessages();
            var after = ViewStyleProbe.ReadState(folderView);
            Console.WriteLine($"header-mouse-message-probe: x={x};y={y};before={before};after={after}");
            if (before == after) throw new InvalidOperationException("Targeted native header mouse messages did not change Shell sort state.");
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            Marshal.Release(view);
        }
    }

    private static void PumpMessages()
    {
        for (var i = 0; i < 20; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NmHeaderInfo { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NmHeader { public NmHeaderInfo Header; public int Item; public int Button; public nint ItemData; }

    internal static void Report(nint parent, string stage)
    {
        EnumChildWindows(parent, (window, _) =>
        {
            var buffer = new StringBuilder(256);
            GetClassNameW(window, buffer, buffer.Capacity);
            var name = buffer.ToString();
            if (name is "NamespaceTreeControl" or "Shell Preview Extension Host")
            {
                GetWindowRect(window, out var bounds);
                // The parent is hidden, so IsWindowVisible is false even for an
                // enabled child. Report the child's own style, not actual visibility.
                var ownVisible = ((long)GetWindowLongPtrW(window, -16) & 0x10000000) != 0;
                var visibleChain = ownVisible;
                for (var ancestor = GetParent(window); ancestor != 0 && ancestor != parent; ancestor = GetParent(ancestor))
                    visibleChain &= ((long)GetWindowLongPtrW(ancestor, -16) & 0x10000000) != 0;
                Console.WriteLine($"{stage}: frame={name}; own-visible-style={ownVisible}; " +
                    $"visible-style-chain={visibleChain}; size={bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}");
            }
            return true;
        }, 0);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCurrentView(nint self, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFolderFlags(nint self, out uint flags);
    private delegate bool EnumChildCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
