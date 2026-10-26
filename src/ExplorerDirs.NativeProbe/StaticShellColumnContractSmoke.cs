using System.Reflection;

internal static class StaticShellColumnContractSmoke
{
    internal static int Run()
    {
        var contract = Assembly.GetExecutingAssembly().GetType("ExplorerDirs.NativeProbe.StaticShellColumnContract");
        if (contract is null)
            throw new InvalidOperationException("The static Shell-column value contract is not implemented.");

        var key = contract.GetProperty("PropertyKey", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Check(key is not null, "custom Shell property key is defined");
        var keyParts = ((Guid FormatId, uint PropertyId))key!;
        Check(keyParts.FormatId != Guid.Empty && keyParts.PropertyId == 1, "custom property key identity is stable");
        var lookup = contract.GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Static);
        Check(lookup is not null, "static value lookup is exposed");
        var compare = contract.GetMethod("Compare", BindingFlags.Public | BindingFlags.Static);
        Check(compare is not null, "typed numeric comparison is exposed");

        ulong Value(string name)
        {
            object?[] args = [name, 0UL];
            var found = (bool)lookup!.Invoke(null, args)!;
            Check(found, $"static value exists for {name}");
            return (ulong)args[1]!;
        }

        int Compare(string left, string right) => (int)compare!.Invoke(null, [Value(left), Value(right)])!;

        Check(Value("two.bin") == 2, "static value 2 is typed as UInt64");
        Check(Value("ten.bin") == 10, "static value 10 is typed as UInt64");
        Check(Value("hundred.bin") == 100, "static value 100 is typed as UInt64");
        Check(Value("TWO.BIN") == 2, "filesystem fixture lookup ignores filename casing");
        object?[] missing = ["unknown.bin", 0UL];
        Check(!(bool)lookup!.Invoke(null, missing)!, "unknown item has no fabricated numeric value");
        Check(Compare("two.bin", "ten.bin") < 0, "numeric ascending distinguishes 2 from 10");
        Check(Compare("ten.bin", "hundred.bin") < 0, "numeric ascending distinguishes 10 from 100");
        Check(Compare("hundred.bin", "two.bin") > 0, "numeric descending comparator reverses correctly");
        Check(Compare("ten.bin", "ten-copy.bin") == 0, "equal numeric values compare equal");
        Console.WriteLine("static Shell-column contract smoke: PASS");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Static Shell-column contract failed: " + name);
    }
}
