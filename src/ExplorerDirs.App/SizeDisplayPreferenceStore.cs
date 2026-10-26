using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal sealed class SizeDisplayPreferenceStore(string path)
{
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string? LoadWarning { get; private set; }

    public SizeDisplayOptions Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(path)) return SizeDisplayOptions.Default;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        SizeDisplayUnit? unit = null;
        int? decimalPlaces = null;
        bool? useThousandsSeparator = null;
        bool? showRecursiveCounts = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[SizeDisplay]", StringComparison.OrdinalIgnoreCase);
                    if (!inSection || seenSection) complete = false;
                    seenSection |= inSection;
                    continue;
                }

                var equals = line.IndexOf('=');
                if (!inSection || equals < 1) { complete = false; continue; }
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
                else if (key.Equals("Unit", StringComparison.OrdinalIgnoreCase))
                {
                    if (unit.HasValue || !Enum.TryParse(value, ignoreCase: true, out SizeDisplayUnit parsed) ||
                        !Enum.IsDefined(parsed)) complete = false;
                    else unit = parsed;
                }
                else if (key.Equals("DecimalPlaces", StringComparison.OrdinalIgnoreCase))
                {
                    if (decimalPlaces.HasValue || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                        parsed is < 0 or > 3) complete = false;
                    else decimalPlaces = parsed;
                }
                else if (key.Equals("UseThousandsSeparator", StringComparison.OrdinalIgnoreCase))
                {
                    if (useThousandsSeparator.HasValue || !bool.TryParse(value, out var parsed)) complete = false;
                    else useThousandsSeparator = parsed;
                }
                else if (key.Equals("ShowRecursiveCounts", StringComparison.OrdinalIgnoreCase))
                {
                    if (showRecursiveCounts.HasValue || !bool.TryParse(value, out var parsed)) complete = false;
                    else showRecursiveCounts = parsed;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "大小显示配置格式高于当前版本；已停止写入原文件。";
                return SizeDisplayOptions.Default;
            }
            if (version != 1 || !unit.HasValue || !decimalPlaces.HasValue ||
                !useThousandsSeparator.HasValue || !seenSection) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete)
            return new SizeDisplayOptions(unit!.Value, decimalPlaces!.Value, useThousandsSeparator!.Value,
                showRecursiveCounts ?? true);

        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "大小显示配置不完整；已备份原文件并恢复默认格式。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "大小显示配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return SizeDisplayOptions.Default;
    }

    public void Save(SizeDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Unit)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.DecimalPlaces is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(options));
        if (!_canOverwrite) throw new IOException($"大小显示配置原文件受到保护，已停止覆盖：{path}");

        var content = "; Explorer Dirs portable size display preference. Close the app before editing." + Environment.NewLine +
            "[SizeDisplay]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"Unit={options.Unit}" + Environment.NewLine +
            $"DecimalPlaces={options.DecimalPlaces.ToString(CultureInfo.InvariantCulture)}" + Environment.NewLine +
            $"UseThousandsSeparator={options.UseThousandsSeparator.ToString().ToLowerInvariant()}" + Environment.NewLine +
            $"ShowRecursiveCounts={options.ShowRecursiveCounts.ToString().ToLowerInvariant()}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "大小显示配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
