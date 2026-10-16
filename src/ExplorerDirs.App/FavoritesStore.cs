using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed class FavoritesStore
{
    private const int MaxFavorites = 200;
    private readonly string _path;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;

    public string? LoadWarning { get; private set; }

    public FavoritesStore(string path) => _path = path;

    public IReadOnlyList<string> Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_path)) return [];

        List<string> recovered = [];
        var complete = false;
        try
        {
            var values = new Dictionary<int, string>();
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
                else if (key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(key[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                    index is >= 0 and < MaxFavorites && !values.ContainsKey(index) && value.Length > 0)
                    values[index] = value;
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "收藏夹格式高于当前版本；已停止写入原文件。";
                return [];
            }
            if (version != 1 || count is < 0 or > MaxFavorites) complete = false;
            if (count != values.Count || values.Keys.Any(index => index >= count)) complete = false;
            for (var index = 0; index < count; index++)
                if (!values.ContainsKey(index)) complete = false;
            recovered = Normalize(values.OrderBy(entry => entry.Key).Select(entry => entry.Value));
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
    {
        if (!_canOverwrite) throw new IOException($"收藏夹原文件受到保护，已停止覆盖：{_path}");
        var favorites = Normalize(paths);
        if (favorites.Count > MaxFavorites) throw new IOException($"收藏夹最多保存 {MaxFavorites} 项。");
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable favorites. Close the app before editing.");
        builder.AppendLine("[Favorites]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={favorites.Count}");
        for (var index = 0; index < favorites.Count; index++)
            builder.AppendLine($"Item{index}={favorites[index]}");
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"收藏夹已被其他实例修改，请重新启动后再保存：{_path}");
        _lastSeenContent = content;
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
