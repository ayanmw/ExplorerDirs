using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed class QuickFilterHistoryStore
{
    internal const int MaxEntries = 50;
    private const int MaxPatternLength = 1024;
    private readonly string _path;
    private readonly List<string> _patterns = [];
    private bool _loaded;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;

    internal string? LoadWarning { get; private set; }

    internal QuickFilterHistoryStore(string path) => _path = path;

    internal IReadOnlyList<string> Load()
    {
        if (_loaded) return _patterns.ToArray();
        _loaded = true;
        _patterns.Clear();
        _canOverwrite = true;
        _lastSeenContent = null;
        LoadWarning = null;
        if (!File.Exists(_path)) return [];

        try { _lastSeenContent = File.ReadAllText(_path); }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "筛选历史无法读取；本次已停止写入原文件。";
            return [];
        }

        var values = new Dictionary<int, string>();
        var count = -1;
        BigInteger version = -1;
        var inSection = false;
        var complete = true;
        foreach (var raw in _lastSeenContent.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (inSection || !line.Equals("[QuickFilterHistory]", StringComparison.OrdinalIgnoreCase))
                    complete = false;
                inSection = line.Equals("[QuickFilterHistory]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (!inSection || equals < 1) { complete = false; continue; }
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
            {
                if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var declared) || version >= 0)
                    complete = false;
                else version = declared;
            }
            else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
            {
                if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count))
                    complete = false;
            }
            else if (TryReadPatternKey(key, out var index))
            {
                if (!values.TryAdd(index, value)) complete = false;
            }
            else complete = false;
        }

        if (version > 1)
        {
            _canOverwrite = false;
            LoadWarning = "筛选历史格式高于当前版本；已停止写入原文件。";
            return [];
        }
        if (version != 1 || count is < 0 or > MaxEntries) complete = false;

        foreach (var (index, value) in values.OrderBy(pair => pair.Key))
        {
            if (index >= MaxEntries || !TryNormalize(value, out var normalized) ||
                _patterns.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                complete = false;
                continue;
            }
            _patterns.Add(normalized);
        }
        if (count != _patterns.Count || values.Keys.Any(index => index >= count) ||
            Enumerable.Range(0, Math.Clamp(count, 0, MaxEntries)).Any(index => !values.ContainsKey(index)))
            complete = false;
        if (complete) return _patterns.ToArray();

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = $"筛选历史配置不完整；已恢复 {_patterns.Count} 项并备份原文件。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "筛选历史配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return _patterns.ToArray();
    }

    internal void Record(string pattern)
    {
        EnsureLoaded();
        if (!TryNormalize(pattern, out var normalized)) return;
        var previous = _patterns.ToArray();
        var existing = _patterns.FindIndex(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing == 0 && string.Equals(_patterns[0], normalized, StringComparison.Ordinal)) return;
        if (existing >= 0) _patterns.RemoveAt(existing);
        _patterns.Insert(0, normalized);
        if (_patterns.Count > MaxEntries) _patterns.RemoveRange(MaxEntries, _patterns.Count - MaxEntries);
        try { Save(); }
        catch
        {
            _patterns.Clear();
            _patterns.AddRange(previous);
            throw;
        }
    }

    internal bool Remove(string pattern)
    {
        EnsureLoaded();
        var index = _patterns.FindIndex(item => string.Equals(item, pattern, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var previous = _patterns.ToArray();
        _patterns.RemoveAt(index);
        try { Save(); }
        catch
        {
            _patterns.Clear();
            _patterns.AddRange(previous);
            throw;
        }
        return true;
    }

    private void EnsureLoaded()
    {
        if (!_loaded) _ = Load();
        if (!_canOverwrite) throw new IOException($"筛选历史原文件受到保护，已停止覆盖：{_path}");
    }

    private void Save()
    {
        if (!_canOverwrite) throw new IOException($"筛选历史原文件受到保护，已停止覆盖：{_path}");
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable quick-filter history.");
        builder.AppendLine("[QuickFilterHistory]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={_patterns.Count}");
        for (var index = 0; index < _patterns.Count; index++)
            builder.AppendLine($"Pattern{index}={_patterns[index]}");
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"筛选历史已被其他实例修改，请重新启动后再保存：{_path}");
        _lastSeenContent = content;
    }

    private static bool TryNormalize(string value, out string normalized)
    {
        normalized = new QuickFilterSettings(value, false, false).Pattern;
        return normalized.Length is > 0 and <= MaxPatternLength;
    }

    private static bool TryReadPatternKey(string key, out int index)
    {
        const string prefix = "Pattern";
        index = -1;
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
            index is >= 0 and < MaxEntries;
    }
}

internal static class QuickFilterHistoryKeyRouting
{
    internal static bool ShouldRemoveSelectedEntry(int key, bool dropDownOpen, bool hasSelection) =>
        key == 0x2E && dropDownOpen && hasSelection;
}
