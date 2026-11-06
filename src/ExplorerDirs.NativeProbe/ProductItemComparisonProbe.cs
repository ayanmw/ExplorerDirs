using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    internal static int RunProductItemComparison(string adapter)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ItemComparison-" + Guid.NewGuid().ToString("N"));
        nint root = 0, folder = 0, before = 0, after = 0, other = 0, repeated = 0, changedAttributes = 0,
            nestedBefore = 0, nestedAfter = 0;
        ProfileShellNamespaceOwner? owner = null;
        try
        {
            Directory.CreateDirectory(fixture);
            var path = Path.Combine(fixture, "changed.bin");
            File.WriteAllBytes(path, new byte[37]);
            File.WriteAllBytes(Path.Combine(fixture, "other.bin"), new byte[83]);
            var classId = Guid.NewGuid();
            owner = ProfileShellNamespaceOwner.Start(classId, fixture, Path.GetFullPath(adapter));
            Check(SHParseDisplayName(CreatePathIdentityParsingName(classId, fixture), 0, out root, 0, out _));
            Check(BindProductDisplayFolder(0, root, 0, in ShellFolder2Id, out folder));
            var parse = At<ParseDisplayName>(folder, 3);
            Check(parse(folder, 0, 0, "changed.bin", 0, out before, 0));
            File.WriteAllBytes(path, new byte[83]);
            Check(parse(folder, 0, 0, "changed.bin", 0, out after, 0));
            Check(parse(folder, 0, 0, "other.bin", 0, out other, 0));
            var compare = At<ProductItemCompare>(folder, 7);
            int Order(uint flags, nint a, nint b)
            {
                var result = compare(folder, flags, a, b);
                Check(result < 0 ? result : 0);
                return unchecked((short)result);
            }
            var canonical = Order(0x10000000, before, after);
            var allFields = Order(0x80000000, before, after);
            var numeric = Order(1, before, after);
            Console.WriteLine($"product-item-comparison: same-path 37->83 B; canonical={canonical}; all-fields={allFields}; numeric={numeric}");
            if (canonical != 0 || numeric >= 0) throw new InvalidOperationException("Fixture lost identity or distinct saved sizes.");
            if (allFields == 0) throw new InvalidOperationException("ALLFIELDS ignored changed size in retained PIDLs.");
            if (Math.Sign(allFields) != -Math.Sign(Order(0x80000000, after, before)))
                throw new InvalidOperationException("ALLFIELDS is not antisymmetric.");
            if (Order(0x80000000, before, before) != 0 || Order(0x80000000, after, other) == 0)
                throw new InvalidOperationException("ALLFIELDS lost self equality or distinct-name identity.");
            Check(parse(folder, 0, 0, "changed.bin", 0, out repeated, 0));
            if (Order(0x80000000, after, repeated) != 0)
                throw new InvalidOperationException("Reparsing unchanged metadata is not equal.");
            var oldAttributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, oldAttributes | FileAttributes.ReadOnly);
                Check(parse(folder, 0, 0, "changed.bin", 0, out changedAttributes, 0));
                if (Order(0x10000000, after, changedAttributes) != 0 || Order(0x80000000, after, changedAttributes) == 0)
                    throw new InvalidOperationException("ALLFIELDS ignored an attribute-only change.");
            }
            finally { File.SetAttributes(path, oldAttributes); }
            var nestedPath = Path.Combine(fixture, "nested");
            Directory.CreateDirectory(nestedPath);
            File.WriteAllBytes(Path.Combine(nestedPath, "leaf.bin"), new byte[12]);
            Check(parse(folder, 0, 0, "nested\\leaf.bin", 0, out nestedBefore, 0));
            File.WriteAllBytes(Path.Combine(nestedPath, "leaf.bin"), new byte[25]);
            Check(parse(folder, 0, 0, "nested\\leaf.bin", 0, out nestedAfter, 0));
            if (Order(0x10000000, nestedBefore, nestedAfter) != 0 || Order(0x80000000, nestedBefore, nestedAfter) == 0)
                throw new InvalidOperationException("ALLFIELDS ignored changed metadata in a relative PIDL tail.");
            Check(At<ComparisonEnum>(folder, 4)(folder, 0, 0x60, out var enumeration));
            try
            {
                var found = false;
                for (;;)
                {
                    // IEnumIDList need not write an unused output slot on S_FALSE.
                    // Initialize it explicitly; a reused out local can retain an already freed PIDL.
                    nint child = 0;
                    var result = At<ComparisonNext>(enumeration, 3)(enumeration, 1, ref child, out var fetched);
                    try
                    {
                        if (result == 1 && fetched == 0) break;
                        Check(result);
                        if (fetched != 1 || child == 0) throw new InvalidOperationException("Invalid native enumeration result.");
                        if (Order(0x10000000, child, after) != 0) continue;
                        found = true;
                        if (Order(0x80000000, child, after) != 0)
                            throw new InvalidOperationException("Unchanged enumerated and parsed PIDLs disagree on metadata.");
                    }
                    finally { if (child != 0) Marshal.FreeCoTaskMem(child); }
                }
                if (!found) throw new InvalidOperationException("Enumeration did not include the comparison fixture.");
            }
            finally { Marshal.Release(enumeration); }
            Console.WriteLine("product-item-comparison: PASS canonical identity vs metadata equality; no Shell view");
            return 0;
        }
        finally
        {
            foreach (var pidl in new[] { before, after, other, repeated, changedAttributes, nestedBefore, nestedAfter, root })
                if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (folder != 0) Marshal.Release(folder);
            owner?.Dispose();
            Directory.Delete(fixture, true);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProductItemCompare(nint self, uint flags, nint left, nint right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ComparisonEnum(nint self, nint owner, uint flags, out nint enumeration);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ComparisonNext(nint self, uint count, ref nint child, out uint fetched);
}
