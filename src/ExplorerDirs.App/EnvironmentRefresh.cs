using System.Collections;

namespace ExplorerDirs.App;

internal sealed class EnvironmentRefresh
{
    private readonly HashSet<string> _previousPersistedNames = new(StringComparer.OrdinalIgnoreCase);

    public EnvironmentRefresh()
    {
        _previousPersistedNames.UnionWith(Read(EnvironmentVariableTarget.Machine).Keys);
        _previousPersistedNames.UnionWith(Read(EnvironmentVariableTarget.User).Keys);
    }

    public int Reload()
    {
        var machine = Read(EnvironmentVariableTarget.Machine);
        var user = Read(EnvironmentVariableTarget.User);
        var merged = new Dictionary<string, string>(machine, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in user)
            merged[name] = value;

        // Windows constructs PATH by appending the user value to the machine value.
        machine.TryGetValue("Path", out var machinePath);
        user.TryGetValue("Path", out var userPath);
        if (machinePath is not null || userPath is not null)
            merged["Path"] = string.Join(';', new[] { machinePath, userPath }.Where(value => !string.IsNullOrEmpty(value)));

        foreach (var oldName in _previousPersistedNames.Except(merged.Keys, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable(oldName, null, EnvironmentVariableTarget.Process);

        foreach (var (name, value) in merged.Where(pair => !pair.Key.Equals("Path", StringComparison.OrdinalIgnoreCase)))
            Environment.SetEnvironmentVariable(name, Environment.ExpandEnvironmentVariables(value), EnvironmentVariableTarget.Process);
        if (merged.TryGetValue("Path", out var path))
            Environment.SetEnvironmentVariable("Path", Environment.ExpandEnvironmentVariables(path), EnvironmentVariableTarget.Process);

        _previousPersistedNames.Clear();
        _previousPersistedNames.UnionWith(merged.Keys);
        return merged.Count;
    }

    private static Dictionary<string, string> Read(EnvironmentVariableTarget target)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(target))
        {
            if (entry.Key is string name && entry.Value is string value)
                result[name] = value;
        }
        return result;
    }
}
