using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal sealed class WindowPlacementStore(string path)
{
    private string? _lastSeen;
    private bool _canWrite = true;
    internal string? LoadWarning { get; private set; }

    internal static string PathForSession(string sessionPath) => Path.ChangeExtension(sessionPath, ".WindowPlacement.ini");

    internal WindowPlacementState? Load()
    {
        _lastSeen = null;
        _canWrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return null;
        try
        {
            _lastSeen = File.ReadAllText(path);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenSection = false;
            var complete = true;
            BigInteger version = -1;
            foreach (var raw in _lastSeen.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('['))
                {
                    if (line != "[WindowPlacement]" || seenSection) complete = false;
                    seenSection = true;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (!seenSection || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (!values.TryAdd(key, value)) complete = false;
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase) &&
                    BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    version = BigInteger.Max(version, parsed);
            }
            if (version > 1)
            {
                _canWrite = false;
                LoadWarning = "窗口位置配置格式高于当前版本；已停止写入原文件。";
                return null;
            }
            if (complete && seenSection && version == 1 && values.Count == 6 &&
                ReadCoordinate(values, "Left", out var left) && ReadCoordinate(values, "Top", out var top) &&
                ReadCoordinate(values, "Right", out var right) && ReadCoordinate(values, "Bottom", out var bottom) &&
                values.TryGetValue("Maximized", out var rawMaximized) && bool.TryParse(rawMaximized, out var maximized))
            {
                var state = new WindowPlacementState(left, top, right, bottom, maximized);
                if (state.IsValid) return state;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "窗口位置配置不完整；已备份原文件，本次使用默认窗口位置。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canWrite = false;
            LoadWarning = "窗口位置配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return null;
    }

    internal void Save(WindowPlacementState state)
    {
        if (!_canWrite) throw new IOException("窗口位置配置受到保护，已停止覆盖。");
        if (!state.IsValid) throw new ArgumentOutOfRangeException(nameof(state));
        var content = FormattableString.Invariant($"; Native workspace coordinates; keep this file beside its window session.\n[WindowPlacement]\nFormatVersion=1\nLeft={state.Left}\nTop={state.Top}\nRight={state.Right}\nBottom={state.Bottom}\nMaximized={state.Maximized}\n");
        PortableIniWriter.WriteIfUnchanged(path, _lastSeen, content,
            "窗口位置配置已被其他实例或外部编辑器修改；已保留原文件。");
        _lastSeen = content;
    }

    private static bool ReadCoordinate(Dictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
