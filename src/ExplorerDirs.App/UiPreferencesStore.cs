using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal sealed class UiPreferencesStore(string path)
    : PortableBooleanPreference(path, "Appearance", "Compact", "外观");

internal sealed class TabStripVisibilityStore(string path)
    : PortableBooleanPreference(path, "Tabs", "AlwaysShow", "标签栏", defaultValue: true);

internal sealed class AddressBarStyleStore(string path)
    : PortableBooleanPreference(path, "AddressBar", "Classic", "地址栏样式");

// Separate files let older releases preserve settings they do not understand.
internal class PortableBooleanPreference(string path, string section, string keyName, string label,
    bool defaultValue = false)
{
    private readonly string _path = path;
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string? LoadWarning { get; private set; }

    public bool Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_path)) return defaultValue;

        var complete = true;
        var inAppearance = false;
        var seenAppearance = false;
        BigInteger version = -1;
        bool? compact = null;
        try
        {
            _lastSeenContent = File.ReadAllText(_path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inAppearance = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                    if (!inAppearance || seenAppearance) complete = false;
                    seenAppearance |= inAppearance;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (!inAppearance || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        complete = false;
                    else
                    {
                        if (version >= 0) complete = false;
                        version = BigInteger.Max(version, parsed);
                    }
                }
                else if (key.Equals(keyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (compact.HasValue || !bool.TryParse(value, out var parsed)) complete = false;
                    else compact = parsed;
                }
                else complete = false;
            }
            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = $"{label}配置格式高于当前版本；已停止写入原文件。";
                return defaultValue;
            }
            if (version != 1 || !compact.HasValue || !seenAppearance) complete = false;
        }
        catch (Exception) { complete = false; }
        if (complete) return compact!.Value;

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = $"{label}配置不完整；已备份原文件并恢复可读取的设置。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = $"{label}配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return compact ?? defaultValue;
    }

    public void Save(bool compact)
    {
        if (!_canOverwrite) throw new IOException($"{label}配置原文件受到保护，已停止覆盖：{_path}");
        var content = "; Explorer Dirs portable preference. Close the app before editing." + Environment.NewLine +
            $"[{section}]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"{keyName}={compact.ToString().ToLowerInvariant()}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"{label}配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
