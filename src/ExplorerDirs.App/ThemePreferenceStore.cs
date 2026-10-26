using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal enum ThemePreference
{
    System,
    Light,
    Dark
}

internal sealed class ThemePreferenceStore(string path)
{
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string? LoadWarning { get; private set; }

    public ThemePreference Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(path)) return ThemePreference.System;

        var complete = true;
        var inTheme = false;
        var seenTheme = false;
        BigInteger version = -1;
        ThemePreference? mode = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inTheme = line.Equals("[Theme]", StringComparison.OrdinalIgnoreCase);
                    if (!inTheme || seenTheme) complete = false;
                    seenTheme |= inTheme;
                    continue;
                }

                var equals = line.IndexOf('=');
                if (!inTheme || equals < 1) { complete = false; continue; }
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
                else if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode.HasValue || !Enum.TryParse(value, ignoreCase: true, out ThemePreference parsed) ||
                        !Enum.IsDefined(parsed)) complete = false;
                    else mode = parsed;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "主题配置格式高于当前版本；已停止写入原文件。";
                return ThemePreference.System;
            }
            if (version != 1 || !mode.HasValue || !seenTheme) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete) return mode!.Value;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "主题配置不完整；已备份原文件并恢复系统主题。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "主题配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return ThemePreference.System;
    }

    public void Save(ThemePreference mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_canOverwrite) throw new IOException($"主题配置原文件受到保护，已停止覆盖：{path}");

        var content = "; Explorer Dirs portable theme preference. Close the app before editing." + Environment.NewLine +
            "[Theme]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"Mode={mode}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "主题配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
