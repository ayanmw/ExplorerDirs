using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class SelectionPropertyProbe
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        File.WriteAllBytes(Path.Combine(root, "three.txt"), new byte[3]);
        File.WriteAllBytes(Path.Combine(root, "five.txt"), new byte[5]);
        File.WriteAllBytes(Path.Combine(root, "empty.txt"), []);
        File.WriteAllBytes(Path.Combine(root, "folder", "nested.txt"), new byte[7]);
        foreach (var (label, names, expected) in new (string, string[], ulong?)[]
        {
            ("single-file", ["three.txt"], 3),
            ("two-files", ["three.txt", "five.txt"], 8),
            ("empty-file", ["empty.txt"], 0),
            ("folder-only", ["folder"], null),
            ("mixed", ["three.txt", "folder", "five.txt"], 8)
        })
            Probe(label, names.Select(name => Path.Combine(root, name)).ToArray(), expected);
        return 0;
    }

    internal static int RunScale()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.SelectionProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var counts = new[] { 25, 250, 1000 };
        var paths = new string[counts[^1]];
        nint[] pidls = new nint[paths.Length];
        try
        {
            for (var i = 0; i < paths.Length; i++)
            {
                paths[i] = Path.Combine(root, $"item-{i:D4}.bin");
                var size = (i % 7) + 1;
                File.WriteAllBytes(paths[i], new byte[size]);
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], 0, out pidls[i], 0, out _));
            }

            foreach (var count in counts)
            {
                nint items = 0;
                try
                {
                    Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)count, pidls[..count], out items));
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var actual = ShellSelectionSize.Read(items, count);
                    var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                    var expectedForCount = paths.Take(count).Select((_, i) => (ulong)((i % 7) + 1)).Aggregate(0UL, (sum, size) => sum + size);
                    if (actual != expectedForCount)
                        throw new Exception($"{count} items: expected {expectedForCount} bytes, got {actual?.ToString() ?? "unknown"}.");
                    Console.WriteLine($"scale: items={count}; bytes={actual}; production-read-ms={elapsed.TotalMilliseconds:F2}");
                }
                finally
                {
                    if (items != 0) Marshal.Release(items);
                }
            }
            return 0;
        }
        finally
        {
            foreach (var pidl in pidls) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            foreach (var path in paths)
                if (File.Exists(path)) File.Delete(path);
            Directory.Delete(root);
        }
    }

    private static void Probe(string label, string[] paths, ulong? expected)
    {
        var pidls = new nint[paths.Length];
        nint items = 0, store = 0;
        var value = new Variant();
        try
        {
            for (var i = 0; i < paths.Length; i++)
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], 0, out pidls[i], 0, out _));
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out items));
            var storeId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var hr = Method<GetStore>(items, 4)(items, 8, ref storeId, out store); // GPS_FASTPROPERTIESONLY
            Console.Write($"{label}: fast-store={hr:X8}");
            if (hr == 0 && store != 0)
            {
                var key = new Key { Format = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), Id = 12 };
                hr = Method<GetValue>(store, 5)(store, ref key, out value);
                Console.Write($"; size={hr:X8}; type={value.Type}; bytes={value.UInt64}");
            }
            Console.WriteLine();
            var known = ShellSelectionSize.Read(items, paths.Length);
            if (known != expected) throw new Exception($"{label}: production size reader disagrees with fixture bytes.");
            if (ShellSelectionSize.Read(items, paths.Length + 1) is not null)
                throw new Exception("Changed selection count was not rejected.");
            Console.WriteLine($"{label}: production-reader=PASS; changed-count=unknown");
        }
        finally
        {
            PropVariantClear(ref value);
            if (store != 0) Marshal.Release(store);
            if (items != 0) Marshal.Release(items);
            foreach (var pidl in pidls) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static T Method<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public ulong UInt64; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetStore(nint self, uint flags, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetValue(nint self, ref Key key, out Variant value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In] nint[] pidls, out nint items);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
}
