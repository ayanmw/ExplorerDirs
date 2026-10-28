using System.Globalization;

namespace ExplorerDirs.NativeProbe;

// Contract for the next isolated IShellFolder2 adapter step. These fixed values
// deliberately avoid filesystem scans and make text-vs-number ordering visible.
internal static class StaticShellColumnContract
{
    public static (Guid FormatId, uint PropertyId) PropertyKey { get; } =
        (new Guid("A38B883C-FA8A-4E1C-8B52-C605E5D01234"), 1);

    private static readonly IReadOnlyDictionary<string, ulong> Values =
        new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["two.bin"] = 2,
            ["ten.bin"] = 10,
            ["hundred.bin"] = 100,
            ["ten-copy.bin"] = 10
        };

    public static bool TryGetValue(string displayName, out ulong value) =>
        Values.TryGetValue(displayName, out value);

    public static int Compare(ulong left, ulong right) => left.CompareTo(right);

    public static string Format(ulong value) => value.ToString(CultureInfo.CurrentCulture);
}
