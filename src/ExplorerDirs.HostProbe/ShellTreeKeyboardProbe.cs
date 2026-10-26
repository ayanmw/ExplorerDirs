using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// Exercises the production WPF HwndHost accelerator path without showing a window
// or injecting input into the desktop. The native child still gets real key messages.
internal static class ShellTreeKeyboardProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        var child = Directory.CreateDirectory(Path.Combine(fixture, "child")).FullName;
        Directory.CreateDirectory(Path.Combine(child, "nested", "deep"));
        Directory.CreateDirectory(Path.Combine(fixture, "other"));

        var source = new HwndSource(new HwndSourceParameters("Hidden ShellTreeHost accelerator probe")
        {
            WindowStyle = unchecked((int)0x80000000), Width = 480, Height = 320
        });
        object? host = null;
        try
        {
            var hostType = Assembly.Load("ExplorerDirs.App").GetType("ExplorerDirs.App.ShellTreeHost", true)!;
            host = Activator.CreateInstance(hostType, nonPublic: true)!;
            source.RootVisual = (Visual)host;
            Pump(TimeSpan.FromMilliseconds(300));

            var tree = hostType.GetField("_tree", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)
                ?? throw new InvalidOperationException("Production ShellTreeHost did not build its native tree.");
            var treeType = tree.GetType();
            treeType.GetMethod("SetRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tree, [fixture, null]);
            Pump(TimeSpan.FromMilliseconds(300));
            var selected = (bool)treeType.GetMethod("SelectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tree, [fixture, null])!;
            if (!selected) throw new InvalidOperationException("Production NativeShellTree could not select the isolated root.");
            Pump(TimeSpan.FromMilliseconds(100));

            var treeWindow = (nint)treeType.GetProperty("WindowHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tree)!;
            var control = FindWindowExW(treeWindow, 0, "SysTreeView32", null);
            var caret = SendMessageW(control, 0x110a, 9, 0); // TVGN_CARET
            if (control == 0 || caret == 0) throw new InvalidOperationException("The production tree has no focused root item.");

            var priorFocus = GetFocus();
            SetFocus(control);
            var hostHasNativeFocus = (bool)hostType.GetMethod("HasNativeFocus", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host, null)!;
            if (!hostHasNativeFocus) throw new InvalidOperationException("ShellTreeHost did not recognize focus in its SysTreeView32 child.");

            SendMessageW(control, 0x1102, 1, caret); // TVE_COLLAPSE
            SendThroughHost(hostType, host, control, 0x100, 0x6b); // WM_KEYDOWN / VK_ADD
            SendThroughHost(hostType, host, control, 0x102, 0x2b); // WM_CHAR / '+'
            SendThroughHost(hostType, host, control, 0x101, 0x6b); // WM_KEYUP / VK_ADD
            Pump(TimeSpan.FromMilliseconds(100));
            var plusExpanded = (ReadTreeItemState(control, caret) & 0x20) != 0;

            var firstChild = SendMessageW(control, 0x110a, 4, caret);
            if (firstChild == 0) throw new InvalidOperationException("The expanded isolated root has no child directory.");
            SendThroughHost(hostType, host, control, 0x100, 0x6a); // WM_KEYDOWN / VK_MULTIPLY
            SendThroughHost(hostType, host, control, 0x102, 0x2a); // WM_CHAR / '*'
            SendThroughHost(hostType, host, control, 0x101, 0x6a); // WM_KEYUP / VK_MULTIPLY
            Pump(TimeSpan.FromMilliseconds(100));
            var multiplyExpandedChild = (ReadTreeItemState(control, firstChild) & 0x20) != 0;
            var grandchild = SendMessageW(control, 0x110a, 4, firstChild);
            var multiplyExpandedDeep = grandchild != 0 && (ReadTreeItemState(control, grandchild) & 0x20) != 0;

            SendThroughHost(hostType, host, control, 0x100, 0x6d); // WM_KEYDOWN / VK_SUBTRACT
            SendThroughHost(hostType, host, control, 0x102, 0x2d); // WM_CHAR / '-'
            SendThroughHost(hostType, host, control, 0x101, 0x6d); // WM_KEYUP / VK_SUBTRACT
            Pump(TimeSpan.FromMilliseconds(100));
            var minusCollapsed = (ReadTreeItemState(control, caret) & 0x20) == 0;
            if (priorFocus != 0 && IsWindow(priorFocus)) SetFocus(priorFocus);

            Console.WriteLine($"shell-tree-host-keys focus-recognized={hostHasNativeFocus}; plus-expanded={plusExpanded}; multiply-expanded-child={multiplyExpandedChild}; multiply-expanded-deep={multiplyExpandedDeep}; minus-collapsed={minusCollapsed}; foreground-unchanged={GetForegroundWindow() == foreground}");
            return plusExpanded && multiplyExpandedChild && multiplyExpandedDeep && minusCollapsed &&
                GetForegroundWindow() == foreground ? 0 : 1;
        }
        finally
        {
            source.RootVisual = null;
            source.Dispose();
            try { Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            GC.KeepAlive(host);
        }
    }

    private static void SendThroughHost(Type hostType, object host, nint hwnd, uint message, int key)
    {
        var translate = hostType.GetMethod("TranslateAcceleratorCore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("ShellTreeHost.TranslateAcceleratorCore was unavailable.");
        var nativeMessage = new MSG { hwnd = hwnd, message = unchecked((int)message), wParam = key, lParam = 0 };
        var arguments = new object?[] { nativeMessage, ModifierKeys.None };
        var handled = (bool)translate.Invoke(host, arguments)!;
        if (!handled)
        {
            var routedMessage = (MSG)arguments[0]!;
            _ = DispatchMessageW(ref routedMessage);
        }
    }

    private static uint ReadTreeItemState(nint control, nint item)
    {
        var value = new TreeItem { Mask = 8, Item = item, StateMask = 0x20 };
        if (SendMessageW(control, 0x110c, 0, ref value) == 0) throw new InvalidOperationException("Tree item state query failed.");
        return value.State;
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
        public uint Mask; public nint Item; public uint State, StateMask; public nint Text;
        public int TextLength, Image, SelectedImage, Children; public nint Parameter;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref TreeItem item);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessageW(ref MSG message);
}
