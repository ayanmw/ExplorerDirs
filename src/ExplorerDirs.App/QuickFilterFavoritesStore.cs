using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record QuickFilterFavorite(string Name, QuickFilterSettings Settings);

internal sealed class QuickFilterFavoritesStore
{
    private const int MaxFavorites = 200;
    private const int MaxNameLength = 128;
    private readonly string _path;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;

    public string? LoadWarning { get; private set; }

    public QuickFilterFavoritesStore(string path) => _path = path;

    public IReadOnlyList<QuickFilterFavorite> Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_path)) return [];

        var values = new Dictionary<int, Dictionary<string, string>>();
        var count = -1;
        BigInteger version = -1;
        var inSection = false;
        var complete = true;
        try { _lastSeenContent = File.ReadAllText(_path); }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "筛选收藏无法读取；本次已停止写入原文件。";
            return [];
        }
        foreach (var raw in _lastSeenContent.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (inSection || !line.Equals("[QuickFilterFavorites]", StringComparison.OrdinalIgnoreCase))
                    complete = false;
                inSection = line.Equals("[QuickFilterFavorites]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (!inSection || equals < 1) { complete = false; continue; }
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
            {
                if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
                    complete = false;
                else
                {
                    if (version >= 0) complete = false;
                    version = BigInteger.Max(version, declared);
                }
            }
            else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
            {
                if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count))
                    complete = false;
            }
            else if (TryReadItemKey(key, out var index, out var field))
            {
                if (!values.TryGetValue(index, out var item)) values[index] = item = new(StringComparer.OrdinalIgnoreCase);
                if (!item.TryAdd(field, value)) complete = false;
            }
            else complete = false;
        }

        if (version > 1)
        {
            _canOverwrite = false;
            LoadWarning = "筛选收藏格式高于当前版本；已停止写入原文件。";
            return [];
        }
        if (version != 1 || count is < 0 or > MaxFavorites) complete = false;

        List<QuickFilterFavorite> recovered = [];
        foreach (var (index, fields) in values.OrderBy(pair => pair.Key))
        {
            if (index >= MaxFavorites || !TryReadFavorite(fields, out var favorite))
            {
                complete = false;
                continue;
            }
            if (recovered.Any(item => string.Equals(item.Name, favorite.Name, StringComparison.OrdinalIgnoreCase)))
            {
                complete = false;
                continue;
            }
            recovered.Add(favorite);
        }
        if (count != recovered.Count || values.Keys.Any(index => index >= count) ||
            Enumerable.Range(0, Math.Clamp(count, 0, MaxFavorites)).Any(index => !values.ContainsKey(index)))
            complete = false;
        if (complete) return recovered;

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = $"筛选收藏配置不完整；已恢复 {recovered.Count} 项并备份原文件。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "筛选收藏配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return recovered;
    }

    public void Save(IEnumerable<QuickFilterFavorite> favorites)
    {
        if (!_canOverwrite) throw new IOException($"筛选收藏原文件受到保护，已停止覆盖：{_path}");
        var normalized = Normalize(favorites);
        if (normalized.Count > MaxFavorites) throw new IOException($"筛选收藏最多保存 {MaxFavorites} 项。");
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable quick-filter favorites. Close the app before editing.");
        builder.AppendLine("[QuickFilterFavorites]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={normalized.Count}");
        for (var index = 0; index < normalized.Count; index++)
        {
            var favorite = normalized[index];
            builder.AppendLine($"Name{index}={favorite.Name}");
            builder.AppendLine($"Pattern{index}={favorite.Settings.Pattern}");
            builder.AppendLine($"Exclude{index}={favorite.Settings.ExcludeMatches.ToString().ToLowerInvariant()}");
            builder.AppendLine($"Folders{index}={favorite.Settings.FilterFolders.ToString().ToLowerInvariant()}");
        }
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"筛选收藏已被其他实例修改，请重新启动后再保存：{_path}");
        _lastSeenContent = content;
    }

    private static List<QuickFilterFavorite> Normalize(IEnumerable<QuickFilterFavorite> favorites)
    {
        List<QuickFilterFavorite> normalized = [];
        foreach (var favorite in favorites)
        {
            var name = NormalizeName(favorite.Name);
            var item = favorite with { Name = name };
            var existing = normalized.FindIndex(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) normalized[existing] = item;
            else normalized.Add(item);
        }
        return normalized;
    }

    private static string NormalizeName(string name)
    {
        if (name is null) throw new ArgumentException("筛选收藏名称不能为空。");
        var normalized = name.Trim();
        if (normalized.Length is 0 or > MaxNameLength || normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException($"筛选收藏名称必须为 1～{MaxNameLength} 个字符，且不能包含换行。");
        return normalized;
    }

    private static bool TryReadFavorite(Dictionary<string, string> fields, out QuickFilterFavorite favorite)
    {
        favorite = null!;
        if (!fields.TryGetValue("Name", out var name) || !fields.TryGetValue("Pattern", out var pattern) ||
            !fields.TryGetValue("Exclude", out var excludeText) || !bool.TryParse(excludeText, out var exclude) ||
            !fields.TryGetValue("Folders", out var foldersText) || !bool.TryParse(foldersText, out var folders))
            return false;
        try
        {
            favorite = new QuickFilterFavorite(NormalizeName(name), new QuickFilterSettings(pattern, exclude, folders));
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool TryReadItemKey(string key, out int index, out string field)
    {
        foreach (var prefix in new[] { "Name", "Pattern", "Exclude", "Folders" })
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(key[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out index) ||
                index is < 0 or >= MaxFavorites)
                continue;
            field = prefix;
            return true;
        }
        index = -1;
        field = string.Empty;
        return false;
    }
}
