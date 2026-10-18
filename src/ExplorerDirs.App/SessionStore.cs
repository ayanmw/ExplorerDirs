using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace ExplorerDirs.App;

internal sealed record PropertyKeyState(Guid FormatId, uint PropertyId)
{
    public override string ToString() => $"{FormatId:B}:{PropertyId}";

    public static PropertyKeyState? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.LastIndexOf(':');
        return separator > 0 && Guid.TryParse(value[..separator], out var formatId) &&
            uint.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var propertyId)
            ? new PropertyKeyState(formatId, propertyId) : null;
    }
}

internal sealed record SortColumnState(PropertyKeyState Key, int Direction);

internal sealed record ViewState(
    uint Mode,
    int IconSize,
    IReadOnlyList<SortColumnState> SortColumns,
    PropertyKeyState? GroupKey,
    bool GroupAscending,
    IReadOnlyList<DisplayColumnState>? DisplayColumns = null)
{
    public static ViewState Default(uint mode = 4) => new(mode, -1, [], null, true);
    public ViewState Copy() => this with
    {
        SortColumns = SortColumns.ToArray(), DisplayColumns = DisplayColumns?.ToArray()
    };
}

internal sealed record TabSettings(string Path, ViewState View);
internal sealed record SessionState(int Layout, uint DefaultViewMode, List<List<TabSettings>> Panes,
    int ActivePane = 0, IReadOnlyList<int>? ActiveTabIndices = null);

internal sealed class SessionStore
{
    private readonly string _path;
    private readonly string _legacyPath;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;
    private bool _versionOneBackedUp;

    public string Path => _path;
    public string? LoadWarning { get; private set; }

    public SessionStore(string? path = null, string? legacyPath = null)
    {
        _path = path ?? ResolvePortablePath();
        _legacyPath = legacyPath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
            "ExplorerDirs.Dev",
#else
            "ExplorerDirs",
#endif
            "session.json");
    }

    private static string ResolvePortablePath()
    {
        var executableDirectory = new DirectoryInfo(AppContext.BaseDirectory);
#if DEBUG
        for (var directory = executableDirectory; directory is not null; directory = directory.Parent)
            if (directory.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(directory.FullName, ".state", "ExplorerDirs.ini");
        return System.IO.Path.Combine(executableDirectory.FullName, ".state", "ExplorerDirs.ini");
#else
        if (executableDirectory.Parent?.Name.Equals("releases", StringComparison.OrdinalIgnoreCase) == true &&
            executableDirectory.Parent.Parent is { } projectRoot)
            return System.IO.Path.Combine(projectRoot.FullName, "profile", "ExplorerDirs.ini");
        return System.IO.Path.Combine(executableDirectory.FullName, "profile", "ExplorerDirs.ini");
#endif
    }

    public SessionState? Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        _versionOneBackedUp = false;
        if (File.Exists(_path))
        {
            ProfileParseResult? parsed = null;
            try
            {
                _lastSeenContent = File.ReadAllText(_path);
                parsed = ParseProfile(_lastSeenContent);
                if (parsed.Complete && parsed.State is not null) return parsed.State;
            }
            catch (Exception) { /* Preserve the unreadable file below. */ }
            if (parsed?.NewerFormat == true)
            {
                _canOverwrite = false;
                LoadWarning = "配置格式高于当前版本；已停止写入原配置。";
                return null;
            }
            var backedUp = BackupInvalidProfile();
            var recoveredCount = parsed?.State?.Panes.Sum(pane => pane.Count) ?? 0;
            LoadWarning = backedUp
                ? $"配置不完整；已恢复 {recoveredCount} 个标签并备份原文件。"
                : "配置不完整且备份失败；本次不会覆盖原配置。";
            return parsed?.State;
        }

        // One-way migration leaves the former profile untouched for older releases.
        try
        {
            if (!File.Exists(_legacyPath)) return null;
            var old = JsonSerializer.Deserialize<LegacySessionState>(File.ReadAllText(_legacyPath));
            if (old?.Panes is not { Count: > 0 }) return null;
            var state = new SessionState(old.Layout, 4, old.Panes.Select(pane =>
                pane.Select(path => new TabSettings(path, ViewState.Default())).ToList()).ToList());
            Save(state);
            return state;
        }
        catch (Exception) { return null; }
    }

    private bool BackupInvalidProfile()
    {
        try
        {
            var backup = _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
            File.Copy(_path, backup);
            return true;
        }
        catch (Exception)
        {
            _canOverwrite = false;
            return false;
        }
    }

    public void Save(SessionState state)
    {
        if (!_canOverwrite)
            throw new IOException($"无法备份损坏的配置文件，已停止覆盖：{_path}");
        var content = Render(state);
        if (!_versionOneBackedUp && _lastSeenContent is not null &&
            ParseProfile(_lastSeenContent).Version == 1)
        {
            // Preserve exactly the snapshot we loaded; a concurrent edit is still
            // rejected by WriteIfUnchanged below. Never silently discard v1 settings.
            File.WriteAllText(_path + $".before-v2-{Guid.NewGuid():N}.bak", _lastSeenContent);
            _versionOneBackedUp = true;
        }
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "会话配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }

    internal static string Render(SessionState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable profile. Close the app before editing.");
        builder.AppendLine("[General]");
        builder.AppendLine("FormatVersion=2");
        builder.AppendLine($"Layout={state.Layout}");
        builder.AppendLine($"DefaultViewMode={state.DefaultViewMode}");
        builder.AppendLine($"ActivePane={Math.Clamp(state.ActivePane, 0, 3)}");
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = state.Panes.ElementAtOrDefault(paneIndex) ?? [];
            builder.AppendLine();
            builder.AppendLine($"[Pane{paneIndex}]");
            builder.AppendLine($"TabCount={tabs.Count}");
            builder.AppendLine($"ActiveTab={Math.Clamp(state.ActiveTabIndices?.ElementAtOrDefault(paneIndex) ?? 0,
                0, Math.Max(0, tabs.Count - 1))}");
            for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
            {
                var tab = tabs[tabIndex];
                builder.AppendLine();
                builder.AppendLine($"[Pane{paneIndex}.Tab{tabIndex}]");
                builder.AppendLine($"Path={tab.Path}");
                builder.AppendLine($"ViewMode={tab.View.Mode}");
                builder.AppendLine($"IconSize={tab.View.IconSize}");
                builder.AppendLine($"DisplayColumnCount={tab.View.DisplayColumns?.Count ?? 0}");
                if (tab.View.DisplayColumns is { } displayColumns)
                    for (var columnIndex = 0; columnIndex < displayColumns.Count; columnIndex++)
                        builder.AppendLine($"DisplayColumn{columnIndex}={displayColumns[columnIndex].Key},{displayColumns[columnIndex].Width}");
                builder.AppendLine($"SortCount={tab.View.SortColumns.Count}");
                for (var sortIndex = 0; sortIndex < tab.View.SortColumns.Count; sortIndex++)
                {
                    var sort = tab.View.SortColumns[sortIndex];
                    builder.AppendLine($"Sort{sortIndex}={sort.Key},{sort.Direction}");
                }
                builder.AppendLine($"GroupKey={tab.View.GroupKey}");
                builder.AppendLine($"GroupAscending={tab.View.GroupAscending.ToString().ToLowerInvariant()}");
            }
        }
        return builder.ToString();
    }

    internal static SessionState? Parse(string content) => ParseProfile(content).State;

    private sealed record ProfileParseResult(SessionState? State, bool Complete, bool NewerFormat, int Version = 0);

    private static ProfileParseResult ParseProfile(string content)
    {
        var complete = true;
        var newerFormatSeen = false;
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        string? currentSection = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                currentSection = name;
                if (sections.TryGetValue(name, out var existing))
                {
                    complete = false;
                    current = existing;
                }
                else current = sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is not null && equals > 0)
            {
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (currentSection?.Equals("General", StringComparison.OrdinalIgnoreCase) == true &&
                    key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase) &&
                    BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var declaredVersion) && declaredVersion > 2)
                    newerFormatSeen = true;
                if (current.ContainsKey(key)) complete = false;
                current[key] = value;
            }
            else complete = false;
        }

        sections.TryGetValue("General", out var general);
        var version = general is null ? 0 : GetInt(general, "FormatVersion", 0);
        if (newerFormatSeen || version > 2) return new ProfileParseResult(null, false, true);
        if (version is not (1 or 2)) complete = false;
        var layout = ReadRequiredInt(general, "Layout", 4, 1, 6, ref complete);
        var defaultView = ReadRequiredInt(general, "DefaultViewMode", 4, 1, 8, ref complete);
        var activePane = ReadOptionalInt(general, "ActivePane", 0, 0, 3, ref complete);
        var panes = new List<List<TabSettings>>();
        var activeTabs = new List<int>();
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = new List<TabSettings>();
            var paneName = $"Pane{paneIndex}";
            sections.TryGetValue(paneName, out var pane);
            var count = ReadRequiredInt(pane, "TabCount", 0, 0, 100, ref complete);
            var indices = new SortedSet<int>(Enumerable.Range(0, count));
            var tabPrefix = paneName + ".Tab";
            foreach (var sectionName in sections.Keys.Where(name =>
                name.StartsWith(tabPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (!int.TryParse(sectionName[tabPrefix.Length..], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var index) || index is < 0 or >= 100)
                {
                    complete = false;
                    continue;
                }
                if (index >= count) complete = false;
                indices.Add(index);
            }
            foreach (var tabIndex in indices)
            {
                if (!sections.TryGetValue($"{paneName}.Tab{tabIndex}", out var values) ||
                    !values.TryGetValue("Path", out var path) || string.IsNullOrWhiteSpace(path))
                {
                    complete = false;
                    continue;
                }
                var sorts = new List<SortColumnState>();
                var sortCount = ReadRequiredInt(values, "SortCount", 0, 0, 16, ref complete);
                var sortIndices = new SortedSet<int>(Enumerable.Range(0, sortCount));
                foreach (var sortName in values.Keys.Where(key =>
                    key.StartsWith("Sort", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("SortCount", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!int.TryParse(sortName[4..], NumberStyles.None, CultureInfo.InvariantCulture,
                            out var index) || index is < 0 or >= 16 ||
                        !sortName.Equals($"Sort{index}", StringComparison.OrdinalIgnoreCase))
                    {
                        complete = false;
                        continue;
                    }
                    if (index >= sortCount) complete = false;
                    sortIndices.Add(index);
                }
                foreach (var sortIndex in sortIndices)
                {
                    if (!values.TryGetValue($"Sort{sortIndex}", out var serialized))
                    {
                        complete = false;
                        continue;
                    }
                    var separator = serialized.LastIndexOf(',');
                    var key = separator < 0 ? null : PropertyKeyState.Parse(serialized[..separator]);
                    if (key is not null && int.TryParse(serialized[(separator + 1)..], out var direction) &&
                        direction is 1 or -1)
                        sorts.Add(new SortColumnState(key, direction));
                    else complete = false;
                }
                var mode = (uint)ReadRequiredInt(values, "ViewMode", 4, 1, 8, ref complete);
                var iconSize = ReadRequiredInt(values, "IconSize", -1, -1, 1024, ref complete);
                if (!values.TryGetValue("GroupKey", out var groupText)) complete = false;
                var group = PropertyKeyState.Parse(groupText);
                if (!string.IsNullOrEmpty(groupText) && group is null) complete = false;
                if (!values.TryGetValue("GroupAscending", out var ascendingText) ||
                    !bool.TryParse(ascendingText, out var ascending))
                {
                    complete = false;
                    ascending = true;
                }
                var columns = ColumnLayout.Parse(values, version >= 2, ref complete);
                var view = new ViewState(mode, iconSize, sorts, group, ascending, columns);
                tabs.Add(new TabSettings(path, view));
            }
            panes.Add(tabs);
            activeTabs.Add(ReadOptionalInt(pane, "ActiveTab", 0, 0, Math.Max(0, tabs.Count - 1),
                ref complete));
        }
        if (version is not (1 or 2) && panes.All(pane => pane.Count == 0))
            return new ProfileParseResult(null, false, false);
        return new ProfileParseResult(new SessionState(layout, (uint)defaultView, panes,
            activePane, activeTabs), complete, false, version);
    }

    private static int ReadOptionalInt(Dictionary<string, string>? values, string key, int fallback,
        int minimum, int maximum, ref bool complete) =>
        values is null || !values.ContainsKey(key) ? fallback :
            ReadRequiredInt(values, key, fallback, minimum, maximum, ref complete);

    private static int ReadRequiredInt(Dictionary<string, string>? values, string key, int fallback,
        int minimum, int maximum, ref bool complete)
    {
        if (values is not null && values.TryGetValue(key, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) &&
            result >= minimum && result <= maximum)
            return result;
        complete = false;
        return fallback;
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    private sealed record LegacySessionState(int Layout, List<List<string>> Panes);
}
