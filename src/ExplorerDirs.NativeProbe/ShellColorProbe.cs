using System.Runtime.InteropServices;
using System.Text;

// Diagnostic only: inspect our hidden parent's descendants, never other apps.
// DirectUI HWNDs do not implement ListView messages; report that limitation
// instead of interpreting an unsupported message's zero as a black background.
internal static class ShellColorProbe
{
    internal static void Report(nint parent, string stage)
    {
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        var listViews = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new StringBuilder(256);
            if (GetClassNameW(window, name, name.Capacity) == 0) return true;
            var cls = name.ToString();
            classes[cls] = classes.GetValueOrDefault(cls) + 1;
            if (cls == "SysListView32")
            {
                listViews++;
                // LVM_GETBKCOLOR, LVM_GETTEXTCOLOR, LVM_GETTEXTBKCOLOR.
                Console.WriteLine($"{stage}: list={listViews}; background={Read(window, 0x1000)}; " +
                    $"text={Read(window, 0x1023)}; text-background={Read(window, 0x1025)}");
            }
            return true;
        }, 0);
        Console.WriteLine($"{stage}: classes=" +
            string.Join(",", classes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")));
        if (listViews == 0)
            Console.WriteLine($"{stage}: listview-colors=unavailable (no SysListView32; no rendered-color conclusion)");
    }

    private static string Read(nint window, uint message) =>
        SendMessageTimeoutW(window, message, 0, 0, 2, 250, out var color) == 0
            ? "unavailable" : $"COLORREF:{unchecked((uint)color):X8}";

    private delegate bool EnumChildCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wparam, nint lparam,
        uint flags, uint timeout, out nuint result);
}
