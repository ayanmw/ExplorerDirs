using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record FavoriteEntry(string Path, string Name = "", string Group = "");

internal sealed class FavoritesStore
{
    private const int MaxFavorites = 200;
    private readonly string _path;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;
    private Dictionary<string, FavoriteEntry> _details = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<string> Folders { get; private set; } = [];
    internal FavoriteEntry Describe(string path) => _details.GetValueOrDefault(path) ?? new(path);

    public string? LoadWarning { get; private set; }

    public FavoritesStore(string path) => _path = path;

    public IReadOnlyList<string> Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        _details.Clear();
        Folders = [];
        if (!File.Exists(_path)) return [];

        List<string> recovered = [];
        var complete = false;
        try
        {
            var values = new Dictionary<int, string>();
            var names = new Dictionary<int, string>();
            var groups = new Dictionary<int, string>();
            var folders = new Dictionary<int, string>();
            var folderCount = -1;
            var count = -1;
            BigInteger version = -1;
            var inSection = false;
            complete = true;
            _lastSeenContent = File.ReadAllText(_path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (inSection) complete = false;
                    inSection = line.Equals("[Favorites]", StringComparison.OrdinalIgnoreCase);
                    if (!inSection) complete = false;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (!inSection || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion))
                        complete = false;
                    else
                    {
                        if (version >= 0) complete = false;
                        version = BigInteger.Max(version, parsedVersion);
                    }
                }
                else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
                {
                    if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count))
                        complete = false;
                }
                else if (key.Equals("FolderCount", StringComparison.OrdinalIgnoreCase))
                {
                    if (folderCount >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out folderCount))
                        complete = false;
                }
                else if (key.StartsWith("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(key[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var folderIndex) ||
                        folderIndex is < 0 or >= MaxFavorites || !folders.TryAdd(folderIndex, value)) complete = false;
                }
                else if ((key.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                          key.StartsWith("Group", StringComparison.OrdinalIgnoreCase)))
                {
                    var isName = key.StartsWith("Name", StringComparison.OrdinalIgnoreCase);
                    var metadata = isName ? names : groups;
                    if (!int.TryParse(key[(isName ? 4 : 5)..], out var metadataIndex) ||
                        metadataIndex is < 0 or >= MaxFavorites || metadata.ContainsKey(metadataIndex))
                        complete = false;
                    else metadata[metadataIndex] = value;
                }
                else if (key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(key[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                    index is >= 0 and < MaxFavorites && !values.ContainsKey(index) && value.Length > 0)
                    values[index] = value;
                else complete = false;
            }

            if (version > 3)
            {
                _canOverwrite = false;
                LoadWarning = "收藏夹格式高于当前版本；已停止写入原文件。";
                return [];
            }
            if (version < 1 || version > 3 || count is < 0 or > MaxFavorites) complete = false;
            if (version < 3 && (folderCount >= 0 || folders.Count != 0)) complete = false;
            if (version == 3 && (folderCount is < 0 or > MaxFavorites || folders.Count != folderCount ||
                folders.Keys.Any(index => index >= folderCount))) complete = false;
            if (version == 1 && (names.Count != 0 || groups.Count != 0)) complete = false;
            if (version >= 2 && (names.Count != values.Count || groups.Count != values.Count ||
                names.Keys.Any(index => !values.ContainsKey(index)) || groups.Keys.Any(index => !values.ContainsKey(index))))
                complete = false;
            if (count != values.Count || values.Keys.Any(index => index >= count)) complete = false;
            // A damaged Count is already marked incomplete; never loop beyond the supported catalog budget.
            // Favorites, common locations and recent addresses all load this file on the UI thread.
            for (var index = 0; index < Math.Clamp(count, 0, MaxFavorites); index++)
                if (!values.ContainsKey(index)) complete = false;
            recovered = Normalize(values.OrderBy(entry => entry.Key).Select(entry => entry.Value));
            string DecodeMetadata(Dictionary<int, string> metadata, int index)
            {
                if (!metadata.TryGetValue(index, out var encoded)) return "";
                try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)); }
                catch (Exception error) when (error is FormatException or DecoderFallbackException)
                { complete = false; return ""; }
            }
            foreach (var pair in values)
                _details[pair.Value] = new(pair.Value, DecodeMetadata(names, pair.Key), DecodeMetadata(groups, pair.Key));
            var decodedFolders = folders.OrderBy(pair => pair.Key).Select(pair => DecodeMetadata(folders, pair.Key)).ToArray();
            if (decodedFolders.Any(string.IsNullOrWhiteSpace) ||
                decodedFolders.Distinct(StringComparer.OrdinalIgnoreCase).Count() != decodedFolders.Length) complete = false;
            Folders = decodedFolders.Where(name => !string.IsNullOrWhiteSpace(name))
                .Concat(_details.Values.Select(item => item.Group).Where(name => name.Length > 0))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (recovered.Count != values.Count) complete = false;
        }
        catch (Exception)
        {
            complete = false;
        }
        if (complete) return recovered;

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = $"收藏夹配置不完整；已恢复 {recovered.Count} 项并备份原文件。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "收藏夹配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return recovered;
    }

    public void Save(IEnumerable<string> paths)
        => SaveCore(paths.Select(Describe).ToArray(), useMetadata: _details.Values.Any(item => item.Name.Length > 0 || item.Group.Length > 0));

    internal void SaveEntries(IEnumerable<FavoriteEntry> entries, IEnumerable<string>? folders = null)
        => SaveCore(entries.ToArray(), useMetadata: true, folders);

    private void SaveCore(IReadOnlyList<FavoriteEntry> entries, bool useMetadata, IEnumerable<string>? folders = null)
    {
        if (!_canOverwrite) throw new IOException($"收藏夹原文件受到保护，已停止覆盖：{_path}");
        if (entries.Any(item => item.Path.Contains('\n') || item.Path.Contains('\r')))
            throw new IOException("收藏路径不能包含换行符。");
        var favorites = Normalize(entries.Select(item => item.Path));
        var details = favorites.ToDictionary(path => path, path => entries.First(item => SamePath(item.Path.Trim(), path)) with { Path = path },
            StringComparer.OrdinalIgnoreCase);
        if (favorites.Count > MaxFavorites) throw new IOException($"收藏夹最多保存 {MaxFavorites} 项。");
        var folderNames = (folders ?? Folders).Select(name => name.Trim())
            .Concat(details.Values.Select(item => item.Group.Trim()).Where(name => name.Length > 0))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (folderNames.Length > MaxFavorites || folderNames.Any(string.IsNullOrWhiteSpace))
            throw new IOException("收藏夹分组不能为空，最多保存 200 个分组。");
        var treeFormat = folderNames.Length > 0;
        useMetadata |= treeFormat;
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable favorites. Close the app before editing.");
        builder.AppendLine("[Favorites]");
        builder.AppendLine(treeFormat ? "FormatVersion=3" : useMetadata ? "FormatVersion=2" : "FormatVersion=1");
        builder.AppendLine($"Count={favorites.Count}");
        if (treeFormat)
        {
            builder.AppendLine($"FolderCount={folderNames.Length}");
            for (var index = 0; index < folderNames.Length; index++)
                builder.AppendLine($"Folder{index}={Convert.ToBase64String(Encoding.UTF8.GetBytes(folderNames[index]))}");
        }
        for (var index = 0; index < favorites.Count; index++)
        {
            builder.AppendLine($"Item{index}={favorites[index]}");
            if (useMetadata)
            {
                var entry = details[favorites[index]];
                builder.AppendLine($"Name{index}={Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Name.Trim()))}");
                builder.AppendLine($"Group{index}={Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Group.Trim()))}");
            }
        }
        var content = builder.ToString();
        // Empty tree groups require v3; retain the last v2 snapshot before upgrading it.
        if (treeFormat && _lastSeenContent is not null && _lastSeenContent.Contains("FormatVersion=2", StringComparison.Ordinal))
            File.WriteAllText(_path + $".v2-{Guid.NewGuid():N}.bak", _lastSeenContent, new UTF8Encoding(false));
        // Preserve the loaded v1 snapshot before the first metadata upgrade.
        if (useMetadata && _lastSeenContent is not null && _lastSeenContent.Contains("FormatVersion=1", StringComparison.Ordinal))
            File.WriteAllText(_path + $".v1-{Guid.NewGuid():N}.bak", _lastSeenContent, new UTF8Encoding(false));
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"收藏夹已被其他实例修改，请重新启动后再保存：{_path}");
        _lastSeenContent = content;
        _details = details;
        Folders = folderNames;
    }

    internal static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static List<string> Normalize(IEnumerable<string> paths)
    {
        List<string> result = [];
        foreach (var path in paths)
        {
            var clean = path.Trim();
            if (clean.Length > 0 && !result.Any(existing => SamePath(existing, clean))) result.Add(clean);
        }
        return result;
    }
}
