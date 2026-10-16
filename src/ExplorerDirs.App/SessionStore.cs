using System.Globalization;
using System.IO;
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
    bool GroupAscending)
{
    public static ViewState Default(uint mode = 4) => new(mode, -1, [], null, true);
    public ViewState Copy() => this with { SortColumns = SortColumns.ToArray() };
}

internal sealed record TabSettings(string Path, ViewState View);
internal sealed record SessionState(int Layout, uint DefaultViewMode, List<List<TabSettings>> Panes);

internal sealed class SessionStore
{
    private readonly string _path;
    private readonly string _legacyPath;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;

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
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "会话配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }

    internal static string Render(SessionState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable profile. Close the app before editing.");
        builder.AppendLine("[General]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Layout={state.Layout}");
        builder.AppendLine($"DefaultViewMode={state.DefaultViewMode}");
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = state.Panes.ElementAtOrDefault(paneIndex) ?? [];
            builder.AppendLine();
            builder.AppendLine($"[Pane{paneIndex}]");
            builder.AppendLine($"TabCount={tabs.Count}");
            for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
            {
                var tab = tabs[tabIndex];
                builder.AppendLine();
                builder.AppendLine($"[Pane{paneIndex}.Tab{tabIndex}]");
                builder.AppendLine($"Path={tab.Path}");
                builder.AppendLine($"ViewMode={tab.View.Mode}");
                builder.AppendLine($"IconSize={tab.View.IconSize}");
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

    private sealed record ProfileParseResult(SessionState? State, bool Complete, bool NewerFormat);

    private static ProfileParseResult ParseProfile(string content)
    {
        var complete = true;
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
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
                if (current.ContainsKey(key)) complete = false;
                current[key] = line[(equals + 1)..].Trim();
            }
            else complete = false;
        }

        sections.TryGetValue("General", out var general);
        var version = general is null ? 0 : GetInt(general, "FormatVersion", 0);
        if (version > 1) return new ProfileParseResult(null, false, true);
        if (version != 1) complete = false;
        var layout = ReadRequiredInt(general, "Layout", 4, 1, 6, ref complete);
        var defaultView = ReadRequiredInt(general, "DefaultViewMode", 4, 1, 8, ref complete);
        var panes = new List<List<TabSettings>>();
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
                for (var sortIndex = 0; sortIndex < sortCount; sortIndex++)
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
                var view = new ViewState(mode, iconSize, sorts, group, ascending);
                tabs.Add(new TabSettings(path, view));
            }
            panes.Add(tabs);
        }
        if (version != 1 && panes.All(pane => pane.Count == 0))
            return new ProfileParseResult(null, false, false);
        return new ProfileParseResult(new SessionState(layout, (uint)defaultView, panes), complete, false);
    }

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
