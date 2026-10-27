using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

// Tracks the ordered per-window session files while keeping profile-wide
// preferences in the directory that contains the primary session.
internal sealed class WindowSessionRegistry
{
    private const int MaximumWindows = 32;
    private readonly string _primaryPath;
    private readonly string _registryPath;
    private List<string> _sessionPaths = [];
    private string? _lastSeenContent;
    private bool _canWrite = true;

    public string RegistryPath => _registryPath;
    public string? LoadWarning { get; private set; }

    public WindowSessionRegistry(string primarySessionPath)
    {
        _primaryPath = Path.GetFullPath(primarySessionPath);
        _registryPath = Path.Combine(Path.GetDirectoryName(_primaryPath)!, "WindowSessions.ini");
    }

    public IReadOnlyList<string> Load()
    {
        LoadWarning = null;
        _canWrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_registryPath))
        {
            _sessionPaths = [_primaryPath];
            return _sessionPaths.ToArray();
        }

        try
        {
            _lastSeenContent = File.ReadAllText(_registryPath);
            var parsed = Parse(_lastSeenContent, _primaryPath);
            if (parsed.NewerVersion)
            {
                _canWrite = false;
                LoadWarning = "窗口会话清单格式高于当前版本；已保留主会话并停止修改清单。";
                _sessionPaths = [_primaryPath];
                return _sessionPaths.ToArray();
            }
            if (parsed.Paths is not null)
            {
                var missingWindow = parsed.Paths.FirstOrDefault(path =>
                    !string.Equals(path, _primaryPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(path));
                if (missingWindow is not null)
                {
                    _canWrite = false;
                    LoadWarning = "窗口会话清单引用的窗口配置文件缺失；已恢复主会话并保护清单不被覆盖。";
                    _sessionPaths = [_primaryPath];
                    return _sessionPaths.ToArray();
                }
                _sessionPaths = parsed.Paths;
                return _sessionPaths.ToArray();
            }
        }
        catch (Exception) { }

        try
        {
            File.Copy(_registryPath, _registryPath + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "窗口会话清单不完整；已备份清单并恢复主会话。";
        }
        catch (Exception)
        {
            _canWrite = false;
            LoadWarning = "窗口会话清单不完整且备份失败；已恢复主会话且停止写入清单。";
        }
        _sessionPaths = [_primaryPath];
        return _sessionPaths.ToArray();
    }

    public string Create(SessionState initialState, IReadOnlyList<PinnedTabEntry> pinnedTabs)
    {
        EnsureLoaded();
        EnsureWritable();
        if (_sessionPaths.Count >= MaximumWindows)
            throw new InvalidOperationException($"最多支持 {MaximumWindows} 个窗口会话。");

        var sessionPath = Path.Combine(Path.GetDirectoryName(_primaryPath)!,
            $"ExplorerDirs.window-{Guid.NewGuid():N}.ini");
        var store = new SessionStore(sessionPath, Path.Combine(Path.GetDirectoryName(_primaryPath)!, "session.json"));
        store.Load();
        store.Save(initialState);
        var pins = new PinnedTabsStore(PinsPath(sessionPath));
        pins.Load();
        pins.Save(pinnedTabs);
        _sessionPaths.Add(sessionPath);
        try { SaveRegistry(); }
        catch
        {
            _sessionPaths.RemoveAt(_sessionPaths.Count - 1);
            try { File.Delete(sessionPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            try { File.Delete(PinsPath(sessionPath)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return sessionPath;
    }

    // Retain the final window so a later launch can restore its last session.
    public bool Remove(string sessionPath)
    {
        EnsureLoaded();
        EnsureWritable();
        var fullPath = Path.GetFullPath(sessionPath);
        var index = _sessionPaths.FindIndex(path => string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || _sessionPaths.Count <= 1) return false;
        _sessionPaths.RemoveAt(index);
        try { SaveRegistry(); }
        catch
        {
            _sessionPaths.Insert(index, fullPath);
            throw;
        }
        return true;
    }

    private void EnsureLoaded()
    {
        if (_sessionPaths.Count == 0) Load();
    }

    private void EnsureWritable()
    {
        if (!_canWrite) throw new IOException("窗口会话清单受到保护，已停止修改。");
    }

    private void SaveRegistry()
    {
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs per-window session registry.");
        builder.AppendLine("[WindowSessions]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={_sessionPaths.Count}");
        for (var index = 0; index < _sessionPaths.Count; index++)
            builder.AppendLine($"Session{index}={Path.GetFileName(_sessionPaths[index])}");
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_registryPath, _lastSeenContent, content,
            "窗口会话清单已被其他进程或外部编辑器修改；本次操作未覆盖该文件。");
        _lastSeenContent = content;
    }

    private static string PinsPath(string sessionPath) => Path.Combine(Path.GetDirectoryName(sessionPath)!,
        Path.GetFileNameWithoutExtension(sessionPath) + ".PinnedTabs.ini");

    private static (List<string>? Paths, bool NewerVersion) Parse(string content, string primaryPath)
    {
        var values = new Dictionary<int, string>();
        var inSection = false;
        var seenSection = false;
        // Preserve unknown future formats even when the version exceeds Int32.
        BigInteger version = -1;
        var count = -1;
        var complete = true;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line.Equals("[WindowSessions]", StringComparison.OrdinalIgnoreCase);
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
                if (version >= 0 || !BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) complete = false;
                else version = version < 0 ? parsed : BigInteger.Max(version, parsed);
            }
            else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
            {
                if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count)) complete = false;
            }
            else if (key.StartsWith("Session", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(key[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                     index is >= 0 and < MaximumWindows && !values.ContainsKey(index))
                values[index] = value;
            else complete = false;
        }

        if (version > 1) return (null, true);
        if (!complete || version != 1 || !seenSection || count is < 1 or > MaximumWindows || values.Count != count ||
            values.Keys.Order().Where((value, index) => value != index).Any()) return (null, false);

        var directory = Path.GetDirectoryName(primaryPath)!;
        var paths = new List<string>(count);
        foreach (var fileName in values.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            var isPrimary = fileName.Equals(Path.GetFileName(primaryPath), StringComparison.OrdinalIgnoreCase);
            var isWindow = fileName.StartsWith("ExplorerDirs.window-", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(fileName[20..^4], "N", out _);
            if (Path.GetFileName(fileName) != fileName || (!isPrimary && !isWindow)) return (null, false);
            paths.Add(Path.GetFullPath(Path.Combine(directory, fileName)));
        }
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count) return (null, false);
        return (paths, false);
    }
}
