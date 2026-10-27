using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

// Real common controls, without a Shell view or desktop input.
internal static class ColumnHeaderFocusProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        // Shell creates version 6 common controls; an unmanifested test process
        // otherwise creates the legacy Header that does not implement HDM_SETFOCUSEDITEM.
        var manifest = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.Header-{Guid.NewGuid():N}.manifest");
        File.WriteAllText(manifest, """
            <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
              <assemblyIdentity version="1.0.0.0" name="ExplorerDirs.HeaderProbe" type="win32"/>
              <dependency><dependentAssembly><assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/></dependentAssembly></dependency>
            </assembly>
            """);
        var context = new ActivationContext { Size = Marshal.SizeOf<ActivationContext>(), Source = manifest };
        var activation = CreateActCtxW(ref context);
        if (activation == -1)
        {
            File.Delete(manifest);
            throw new InvalidOperationException("Version 6 common control activation failed.");
        }
        if (!ActivateActCtx(activation, out var cookie))
        {
            ReleaseActCtx(activation);
            File.Delete(manifest);
            throw new InvalidOperationException("Version 6 common control activation failed.");
        }
        nint parent = 0;
        try
        {
            parent = CreateWindowExW(0x08000000, "STATIC", "", 0x80000000, 0, 0, 400, 200, 0, 0, 0, 0); // WS_EX_NOACTIVATE
            Check(parent != 0, "Header fixture parent creation succeeded");
            var list = CreateWindowExW(0, "SysListView32", "", 0x50000001, 0, 0, 400, 200, parent, 0, 0, 0);
            var column = new Column { Mask = 2 | 4, Width = 160, Text = "Name" };
            SendMessageW(list, 0x1061, 0, ref column); // LVM_INSERTCOLUMNW
            column.Text = "Size";
            SendMessageW(list, 0x1061, 1, ref column);
            var header = SendMessageW(list, 0x101F, 0, 0); // LVM_GETHEADER
            Check(header != 0, "native list owns a header");
            var type = Assembly.Load("ExplorerDirs.App").GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
            var focus = type.GetMethod("FocusNativeColumnHeader", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing native Header focus path: UIA SetFocus rejects this Shell header.");
            var focused = (bool)focus.Invoke(null, [header])!;
            Console.WriteLine($"header={header} focus={GetFocus()} count={SendMessageW(header, 0x1200, 0, 0)} item={SendMessageW(header, 0x121B, 0, 0)} result={focused}");
            Check(focused, "native header accepts keyboard focus");
            Check(GetFocus() == header && SendMessageW(header, 0x121B, 0, 0) == 0,
                "first header item owns native focus");
            SendMessageW(header, 0x100, 0x27, 0); // WM_KEYDOWN / VK_RIGHT
            Check(SendMessageW(header, 0x121B, 0, 0) == 1, "Right moves native header focus");
            Check((bool)focus.Invoke(null, [header])! && SendMessageW(header, 0x121B, 0, 0) == 1,
                "reentry preserves the previously focused column");
            Check(!(bool)focus.Invoke(null, [(nint)0])!, "missing header is rejected");
            SetFocus(list);
            Check(GetFocus() == list, "focus can return to the original list");
            var foregroundAfter = GetForegroundWindow();
            Console.WriteLine($"foreground-before={foreground} after={foregroundAfter} fixture={parent}");
            Check(foregroundAfter == foreground, "foreground unchanged");
            Console.WriteLine("Column header focus: PASS (native hidden common controls; no Shell or desktop input)");
            return 0;
        }
        finally
        {
            DestroyWindow(parent);
            DeactivateActCtx(0, cookie);
            ReleaseActCtx(activation);
            File.Delete(manifest);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Column
    {
        public uint Mask;
        public int Format, Width;
        [MarshalAs(UnmanagedType.LPWStr)] public string Text;
        public int TextLength, SubItem, Image, Order, MinWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ActivationContext
    {
        public int Size;
        public uint Flags;
        public string Source;
        public ushort ProcessorArchitecture, Language;
        public nint AssemblyDirectory, ResourceName, ApplicationName, Module;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateActCtxW(ref ActivationContext context);
    [DllImport("kernel32.dll")] private static extern bool ActivateActCtx(nint context, out nuint cookie);
    [DllImport("kernel32.dll")] private static extern bool DeactivateActCtx(uint flags, nuint cookie);
    [DllImport("kernel32.dll")] private static extern void ReleaseActCtx(nint context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string cls, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref Column lParam);
}
