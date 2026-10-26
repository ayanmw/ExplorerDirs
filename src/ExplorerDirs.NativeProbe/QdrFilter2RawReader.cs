namespace ExplorerDirs.NativeProbe;

internal readonly record struct QdrFilter2RawRule(int Index, string Pattern, IReadOnlyList<string> Fields);

/// <summary>
/// Reads the observable INI row shape without assigning meanings to undocumented Filter2 columns.
/// </summary>
internal static class QdrFilter2RawReader
{
    internal static bool TryParse(string iniText, out IReadOnlyList<QdrFilter2RawRule> rules)
    {
        ArgumentNullException.ThrowIfNull(iniText);
        var parsed = new SortedDictionary<int, QdrFilter2RawRule>();
        var inFilter2 = false;
        foreach (var rawLine in iniText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inFilter2 = line.Equals("[Filter2]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inFilter2) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0 || !int.TryParse(line.AsSpan(0, separator),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index))
                continue;

            var columns = line[(separator + 1)..].Split('=');
            if (columns.Length != 7 || columns[0].Length == 0 || parsed.ContainsKey(index))
            {
                rules = Array.Empty<QdrFilter2RawRule>();
                return false;
            }

            parsed.Add(index, new QdrFilter2RawRule(index, columns[0], columns[1..]));
        }

        rules = parsed.Values.ToArray();
        return true;
    }
}
