using System.IO.Enumeration;

namespace ExplorerDirs.App;

internal sealed record QuickFilterSettings
{
    private readonly string[] _patterns;
    private readonly ColumnValueFilter[] _columnFilters;

    internal static QuickFilterSettings Empty { get; } = new(string.Empty, false, false);

    public string Pattern { get; }
    public bool ExcludeMatches { get; }
    public bool FilterFolders { get; }
    public bool HasPattern => _patterns.Length > 0;
    public IReadOnlyList<ColumnValueFilter> ColumnFilters => _columnFilters;
    public bool HasColumnFilters => _columnFilters.Length > 0;
    public bool HasAnyFilter => HasPattern || HasColumnFilters;

    public QuickFilterSettings(string pattern, bool excludeMatches, bool filterFolders,
        IEnumerable<ColumnValueFilter>? columnFilters = null)
    {
        _patterns = (pattern ?? string.Empty).Replace('\r', ';').Replace('\n', ';')
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Pattern = string.Join(';', _patterns);
        ExcludeMatches = excludeMatches;
        FilterFolders = filterFolders;
        _columnFilters = (columnFilters ?? [])
            .Where(filter => filter.Values.Count > 0)
            .GroupBy(filter => filter.Key)
            .Select(group => new ColumnValueFilter(group.Key, group.SelectMany(filter => filter.Values)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    public QuickFilterSettings WithNameFilter(string pattern, bool excludeMatches, bool filterFolders) =>
        new(pattern, excludeMatches, filterFolders, _columnFilters);

    public QuickFilterSettings WithColumnFilters(IEnumerable<ColumnValueFilter> columnFilters) =>
        new(Pattern, ExcludeMatches, FilterFolders, columnFilters);

    public bool Matches(string name) => HasPattern &&
        _patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));

    public bool ShouldShow(string name, bool isFolder)
    {
        if (!HasPattern || (isFolder && !FilterFolders)) return true;
        var matches = Matches(name);
        return ExcludeMatches ? !matches : matches;
    }

    public bool MatchesColumnFilters(Func<PropertyKeyState, string?> readValue)
    {
        foreach (var filter in _columnFilters)
        {
            var value = readValue(filter.Key);
            if (value is not null && !filter.Values.Contains(value, StringComparer.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}

internal sealed record ColumnValueFilter(PropertyKeyState Key, IReadOnlyList<string> Values);
