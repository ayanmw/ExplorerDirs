using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed class TabHoverActivationPreference(string path)
{
    internal const int DefaultDelayMilliseconds = 1000;
    internal static IReadOnlyList<int> AllowedDelayMilliseconds { get; } = [0, 200, 500, 1000, 1500, 2000, 3000];

    private string? _lastSeenContent;
    private bool _canOverwrite = true;
    internal string? LoadWarning { get; private set; }

    internal int Load()
    {
        _lastSeenContent = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return DefaultDelayMilliseconds;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        int? delay = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[TabHoverActivation]", StringComparison.OrdinalIgnoreCase);
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
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || version >= 0)
                        complete = false;
                    else version = parsed;
                }
                else if (key.Equals("DelayMilliseconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (delay.HasValue || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        complete = false;
                    else delay = parsed;
                }
                else complete = false;
            }
        }
        catch (Exception)
        {
            complete = false;
        }

        if (version > 1)
        {
            _canOverwrite = false;
            LoadWarning = "标签悬停设置格式高于当前版本；已停止写入原文件。";
            return DefaultDelayMilliseconds;
        }
        if (delay.HasValue && !AllowedDelayMilliseconds.Contains(delay.Value))
            delay = null;
        var safeDelay = delay ?? DefaultDelayMilliseconds;
        if (version != 1 || !seenSection || delay is null)
            complete = false;
        if (complete) return delay!.Value;

        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "标签悬停设置不完整；已备份原文件并恢复默认值。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "标签悬停设置不完整且备份失败；本次不会覆盖原文件。";
        }
        return safeDelay;
    }

    internal void Save(int delayMilliseconds)
    {
        if (!AllowedDelayMilliseconds.Contains(delayMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        if (!_canOverwrite) throw new IOException($"标签悬停设置原文件受到保护，已停止覆盖：{path}");

        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable tab-hover activation preference.");
        builder.AppendLine("[TabHoverActivation]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"DelayMilliseconds={delayMilliseconds.ToString(CultureInfo.InvariantCulture)}");
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            $"标签悬停设置已被其他实例修改，请重新启动后再保存：{path}");
        _lastSeenContent = content;
    }
}
