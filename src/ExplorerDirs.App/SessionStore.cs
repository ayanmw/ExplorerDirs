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

    public string Path => _path;

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
        if (File.Exists(_path))
        {
            try
            {
                var state = Parse(File.ReadAllText(_path));
                if (state is not null) return state;
            }
            catch (Exception) { /* Preserve the unreadable file below. */ }
            try
            {
                var backup = _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak";
                File.Copy(_path, backup);
            }
            catch (Exception) { _canOverwrite = false; }
            return null;
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

    public void Save(SessionState state)
    {
        if (!_canOverwrite)
            throw new IOException($"无法备份损坏的配置文件，已停止覆盖：{_path}");
        var content = Render(state);
        if (File.Exists(_path) && File.ReadAllText(_path) == content) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, _path, true);
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

    internal static SessionState? Parse(string content)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                current = sections.TryGetValue(name, out var existing) ? existing :
                    sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is not null && equals > 0)
                current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        if (!sections.TryGetValue("General", out var general) || GetInt(general, "FormatVersion", 0) != 1)
            return null;
        var panes = new List<List<TabSettings>>();
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = new List<TabSettings>();
            var paneName = $"Pane{paneIndex}";
            var count = sections.TryGetValue(paneName, out var pane) ?
                Math.Clamp(GetInt(pane, "TabCount", 0), 0, 100) : 0;
            for (var tabIndex = 0; tabIndex < count; tabIndex++)
            {
                if (!sections.TryGetValue($"{paneName}.Tab{tabIndex}", out var values) ||
                    !values.TryGetValue("Path", out var path) || string.IsNullOrWhiteSpace(path))
                    continue;
                var sorts = new List<SortColumnState>();
                var sortCount = Math.Clamp(GetInt(values, "SortCount", 0), 0, 16);
                for (var sortIndex = 0; sortIndex < sortCount; sortIndex++)
                {
                    if (!values.TryGetValue($"Sort{sortIndex}", out var serialized)) continue;
                    var separator = serialized.LastIndexOf(',');
                    if (separator < 0) continue;
                    var key = PropertyKeyState.Parse(serialized[..separator]);
                    if (key is not null && int.TryParse(serialized[(separator + 1)..], out var direction) &&
                        direction is 1 or -1)
                        sorts.Add(new SortColumnState(key, direction));
                }
                var mode = (uint)Math.Clamp(GetInt(values, "ViewMode", 4), 1, 8);
                var view = new ViewState(mode, GetInt(values, "IconSize", -1), sorts,
                    PropertyKeyState.Parse(values.GetValueOrDefault("GroupKey")),
                    !bool.TryParse(values.GetValueOrDefault("GroupAscending"), out var ascending) || ascending);
                tabs.Add(new TabSettings(path, view));
            }
            panes.Add(tabs);
        }
        return new SessionState(GetInt(general, "Layout", 4),
            (uint)Math.Clamp(GetInt(general, "DefaultViewMode", 4), 1, 8), panes);
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    private sealed record LegacySessionState(int Layout, List<List<string>> Panes);
}
