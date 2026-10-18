using System.Globalization;

namespace ExplorerDirs.App;

internal sealed record DisplayColumnState(PropertyKeyState Key, int Width);

internal static class ColumnLayout
{
    public const int MaximumColumns = 256;

    public static bool IsValid(IReadOnlyList<DisplayColumnState> columns) =>
        columns.Count is > 0 and <= MaximumColumns &&
        columns.All(column => column.Key.FormatId != Guid.Empty && column.Width is >= -2 and <= 65535) &&
        columns.Select(column => column.Key).Distinct().Count() == columns.Count;

    public static IReadOnlyList<DisplayColumnState>? Parse(
        Dictionary<string, string> values, bool required, ref bool complete)
    {
        var fieldCount = values.Keys.Count(key => key.StartsWith("DisplayColumn", StringComparison.OrdinalIgnoreCase));
        if (fieldCount == 0 && !required) return null; // Legacy profile: use Shell defaults.
        if (!values.TryGetValue("DisplayColumnCount", out var text) ||
            !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count is < 0 or > MaximumColumns || fieldCount != count + 1)
        {
            complete = false;
            return null;
        }
        if (count == 0) return null; // No captured layout, not a request to hide all columns.
        var columns = new List<DisplayColumnState>(count);
        for (var index = 0; index < count; index++)
        {
            if (!values.TryGetValue($"DisplayColumn{index}", out var serialized)) break;
            var separator = serialized.LastIndexOf(',');
            var key = separator < 0 ? null : PropertyKeyState.Parse(serialized[..separator]);
            if (key is null || !int.TryParse(serialized[(separator + 1)..], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var width)) break;
            columns.Add(new DisplayColumnState(key, width));
        }
        if (columns.Count == count && IsValid(columns)) return columns;
        // Do not restore a partial list: that would hide valid Shell columns.
        complete = false;
        return null;
    }
}
