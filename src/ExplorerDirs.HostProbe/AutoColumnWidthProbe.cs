using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

// Exercises Ctrl+NumPad+ through the production ExplorerBrowserHost without showing a window.
internal static class AutoColumnWidthProbe
{
    internal static int Run(Type hostType, IReadOnlyList<HwndHost> hosts, HwndSource source, nint foregroundBefore)
    {
        var assembly = hostType.Assembly;
        var viewStyleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
        var details = ((System.Collections.IEnumerable)viewStyleType.GetProperty("All")!.GetValue(null)!)
            .Cast<object>().Single(style => (uint)viewStyleType.GetProperty("Mode")!.GetValue(style)! == 4);
        var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
        if (!Equals(setStyle.Invoke(hosts[0], [details]), true))
            throw new Exception("The product ExplorerBrowserHost did not accept the Details view baseline.");

        var columnManager = assembly.GetType("ExplorerDirs.App.ShellColumnManager", true)!;
        var capture = columnManager.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public)!;
        var apply = columnManager.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public)!;
        var layoutType = assembly.GetType("ExplorerDirs.App.DisplayColumnState", true)!;
        var narrow = new List<object>();
        var current = Capture(hostType, hosts[0], capture);
        foreach (var column in current)
        {
            var key = layoutType.GetProperty("Key")!.GetValue(column)!;
            narrow.Add(Activator.CreateInstance(layoutType, key, 110)!);
        }
        var viewPointer = GetViewPointer(hostType, hosts[0]);
        try
        {
            var genericList = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(layoutType))!;
            foreach (var column in narrow) genericList.Add(column);
            Marshal.ThrowExceptionForHR((int)apply.Invoke(null, [viewPointer, genericList, null])!);
        }
        finally { Marshal.Release(viewPointer); }

        var baseline = Capture(hostType, hosts[0], capture);
        var keyProperty = layoutType.GetProperty("Key")!;
        var nameIndex = baseline.FindIndex(column =>
        {
            var key = keyProperty.GetValue(column)!;
            return (uint)key.GetType().GetProperty("PropertyId")!.GetValue(key)! == 10;
        });
        if (nameIndex < 0) throw new Exception("The product Details view did not expose the Name column.");
        var otherPanesBefore = hosts.Skip(1).Select(host => Capture(hostType, host, capture)).ToArray();
        var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hosts[0])!;
        var listView = FindListView(viewWindow);
        if (listView == 0) throw new Exception("The active Shell view did not expose its native SysListView32.");

        var foreground = GetForegroundWindow();
        var originalKeyboardState = new byte[256];
        if (!GetKeyboardState(originalKeyboardState))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not capture the probe thread keyboard state.");
        try
        {
            var simulated = originalKeyboardState.ToArray();
            simulated[0x11] |= 0x80;
            simulated[0xA2] |= 0x80;
            if (!SetKeyboardState(simulated))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not simulate Ctrl for the hidden host probe.");
            if (SetFocus(listView) != listView && GetFocus() != listView)
                throw new Exception($"The hidden product file list could not acquire thread focus: expected={listView:X}, actual={GetFocus():X}.");

            var translate = hostType.GetMethod("TranslateAcceleratorCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var message = new MSG { hwnd = listView, message = 0x0100, wParam = (nint)0x6B, lParam = (nint)1 };
            var handled = translate.Invoke(hosts[0], [message, ModifierKeys.Control]);
            if (!Equals(handled, true)) throw new Exception("The production host did not consume Ctrl+VK_ADD.");
            var after = Capture(hostType, hosts[0], capture);
            var otherPanesAfter = hosts.Skip(1).Select(host => Capture(hostType, host, capture)).ToArray();
            var nameBefore = (int)layoutType.GetProperty("Width")!.GetValue(baseline[nameIndex])!;
            var nameAfter = (int)layoutType.GetProperty("Width")!.GetValue(after[nameIndex])!;
            var otherPanesUnchanged = otherPanesBefore.Select((columns, index) => columns.SequenceEqual(otherPanesAfter[index])).All(value => value);
            Console.WriteLine($"auto-column-width host=production-ExplorerBrowserHost name-width={nameBefore}->{nameAfter} focus-retained={GetFocus() == listView} other-panes-unchanged={otherPanesUnchanged} parent-hidden={!IsWindowVisible(source.Handle)} foreground-unchanged={GetForegroundWindow() == foreground}");
            if (nameAfter <= nameBefore || !otherPanesUnchanged || IsWindowVisible(source.Handle) ||
                GetForegroundWindow() != foreground || foreground != foregroundBefore)
                throw new Exception("Ctrl+NumPad+ did not auto-fit the active production Shell view without affecting other panes or the desktop.");
        }
        finally
        {
            if (!SetKeyboardState(originalKeyboardState))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the probe thread keyboard state.");
        }
        return 0;
    }

    private static List<object> Capture(Type hostType, HwndHost host, MethodInfo capture)
    {
        var view = GetViewPointer(hostType, host);
        try
        {
            object?[] args = [view, null];
            var result = (int)capture.Invoke(null, args)!;
            Marshal.ThrowExceptionForHR(result);
            return ((System.Collections.IEnumerable)args[1]!).Cast<object>().ToList();
        }
        finally { Marshal.Release(view); }
    }

    private static nint GetViewPointer(Type hostType, HwndHost host)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        return view != 0 ? view : throw new Exception("The product Shell folder view is unavailable.");
    }

    private static nint FindListView(nint parent)
    {
        nint result = 0;
        EnumChildWindows(parent, (child, _) =>
        {
            var name = new StringBuilder(32);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() != "SysListView32") return true;
            result = child;
            return false;
        }, 0);
        return result;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetKeyboardState([In] byte[] state);
}
