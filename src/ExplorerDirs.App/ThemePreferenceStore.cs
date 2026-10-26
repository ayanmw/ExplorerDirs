using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Media;

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
    private ThemePreference _currentPreference = ThemePreference.System;

    public string? LoadWarning { get; private set; }
    public Color? CustomAccentColor { get; private set; }

    public ThemePreference Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        _currentPreference = ThemePreference.System;
        CustomAccentColor = null;
        if (!File.Exists(path)) return ThemePreference.System;

        var complete = true;
        var inTheme = false;
        var seenTheme = false;
        BigInteger version = -1;
        ThemePreference? mode = null;
        Color? customAccentColor = null;
        var accentColorSeen = false;
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
                else if (key.Equals("AccentColor", StringComparison.OrdinalIgnoreCase))
                {
                    if (accentColorSeen) { complete = false; continue; }
                    accentColorSeen = true;
                    if (value.Equals("System", StringComparison.OrdinalIgnoreCase)) customAccentColor = null;
                    else if (TryParseAccentColor(value, out var parsedAccent)) customAccentColor = parsedAccent;
                    else complete = false;
                }
                else complete = false;
            }

            if (version > 2)
            {
                _canOverwrite = false;
                LoadWarning = "主题配置格式高于当前版本；已停止写入原文件。";
                return ThemePreference.System;
            }
            if ((version != 1 && version != 2) || !mode.HasValue || !seenTheme ||
                (version == 1 && accentColorSeen) || (version == 2 && !accentColorSeen)) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete)
        {
            _currentPreference = mode!.Value;
            CustomAccentColor = customAccentColor;
            return mode.Value;
        }
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
        _currentPreference = ThemePreference.System;
        CustomAccentColor = null;
        return ThemePreference.System;
    }

    public void Save(ThemePreference mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        SaveSettings(mode, CustomAccentColor);
    }

    public void SaveAccentColor(Color? customAccentColor) => SaveSettings(_currentPreference, customAccentColor);

    private void SaveSettings(ThemePreference mode, Color? customAccentColor)
    {
        if (!_canOverwrite) throw new IOException($"主题配置原文件受到保护，已停止覆盖：{path}");

        var content = "; Explorer Dirs portable theme preference. Close the app before editing." + Environment.NewLine +
            "[Theme]" + Environment.NewLine +
            "FormatVersion=2" + Environment.NewLine +
            $"Mode={mode}" + Environment.NewLine +
            $"AccentColor={(customAccentColor is { } accent ? $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}" : "System")}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "主题配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
        _currentPreference = mode;
        CustomAccentColor = customAccentColor;
    }

    private static bool TryParseAccentColor(string value, out Color color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#' ||
            !byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }
}
