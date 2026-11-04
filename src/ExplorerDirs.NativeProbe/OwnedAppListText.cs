using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Cross-process message data only, for the exact child App created by the probe.
// No input or executable memory. Successful calls release their marshalling buffers.
internal static class OwnedAppListText
{
    internal static string Read(nint process, uint processId, nint list, int row, int column)
    {
        GetWindowThreadProcessId(list, out var owner);
        if (owner != processId || IntPtr.Size != 8)
            throw new InvalidOperationException("List text probe requires its owned x64 App window.");
        const int capacity = 512;
        var headerSize = Marshal.SizeOf<ListItem>();
        var memory = VirtualAllocEx(process, 0, (nuint)(headerSize + capacity * 2), 0x3000, 4);
        if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var dispatched = false;
        var completed = false;
        try
        {
            var item = new ListItem { Item = row, SubItem = column, Text = memory + headerSize, TextCapacity = capacity };
            var bytes = new byte[headerSize];
            var local = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.Copy(new byte[headerSize], 0, local, headerSize);
                Marshal.StructureToPtr(item, local, false);
                Marshal.Copy(local, bytes, 0, bytes.Length);
            }
            finally { Marshal.FreeHGlobal(local); }
            if (!WriteProcessMemory(process, memory, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            dispatched = true;
            completed = SendMessageTimeoutW(list, 0x1073, row, memory, 0x22, 1000, out var length) != 0;
            if (!completed) throw new TimeoutException("Owned App list text request did not finish.");
            if (length < 0 || length >= capacity) throw new InvalidOperationException("Invalid native list text length.");
            var text = new byte[(int)length * 2];
            if (text.Length == 0) return string.Empty;
            if (!ReadProcessMemory(process, memory + headerSize, text, (nuint)text.Length, out var read) || read != (nuint)text.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return Encoding.Unicode.GetString(text);
        }
        finally
        {
            // A timed-out receiver may still use the pointer. The probe fails and
            // terminates its owned child in its finally; process exit reclaims that buffer.
            if (!dispatched || completed) VirtualFreeEx(process, memory, 0, 0x8000);
        }
    }

    internal static int Find(nint process, uint processId, nint list, string name)
    {
        GetWindowThreadProcessId(list, out var owner);
        if (owner != processId || IntPtr.Size != 8) throw new InvalidOperationException("Expected owned x64 App list.");
        // x64 LVFINDINFOW: flags@0, psz@8, lParam@16, POINT@24, vkDirection@32.
        var text = Encoding.Unicode.GetBytes(name + "\0");
        var data = new byte[40 + text.Length];
        var memory = VirtualAllocEx(process, 0, (nuint)data.Length, 0x3000, 4);
        if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var dispatched = false;
        var completed = false;
        try
        {
            BitConverter.GetBytes(2u).CopyTo(data, 0);
            BitConverter.GetBytes((long)memory + 40).CopyTo(data, 8);
            text.CopyTo(data, 40);
            if (!WriteProcessMemory(process, memory, data, (nuint)data.Length, out var written) || written != (nuint)data.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            dispatched = true;
            completed = SendMessageTimeoutW(list, 0x1053, -1, memory, 0x22, 1000, out var index) != 0;
            if (!completed) throw new TimeoutException("Owned App list name search did not finish.");
            return unchecked((int)index);
        }
        finally { if (!dispatched || completed) VirtualFreeEx(process, memory, 0, 0x8000); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListItem
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask;
        public nint Text; public int TextCapacity, Image; public nint Parameter;
        public int Indent, GroupId; public uint ColumnCount; public nint Columns, ColumnFormats; public int Group;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint operation);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint read);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
