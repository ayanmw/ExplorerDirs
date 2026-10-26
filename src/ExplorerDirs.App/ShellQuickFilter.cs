using System.IO.Enumeration;

namespace ExplorerDirs.App;

internal sealed record QuickFilterSettings
{
    private readonly string[] _patterns;

    internal static QuickFilterSettings Empty { get; } = new(string.Empty, false, false);

    public string Pattern { get; }
    public bool ExcludeMatches { get; }
    public bool FilterFolders { get; }
    public bool HasPattern => _patterns.Length > 0;

    public QuickFilterSettings(string pattern, bool excludeMatches, bool filterFolders)
    {
        _patterns = (pattern ?? string.Empty).Replace('\r', ';').Replace('\n', ';')
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Pattern = string.Join(';', _patterns);
        ExcludeMatches = excludeMatches;
        FilterFolders = filterFolders;
    }

    public bool Matches(string name) => HasPattern &&
        _patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));

    public bool ShouldShow(string name, bool isFolder)
    {
        if (!HasPattern || (isFolder && !FilterFolders)) return true;
        var matches = Matches(name);
        return ExcludeMatches ? !matches : matches;
    }
}
