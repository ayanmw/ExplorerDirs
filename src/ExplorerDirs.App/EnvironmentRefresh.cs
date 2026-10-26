using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ExplorerDirs.App;

internal sealed record PersistedVariable(string Value, bool Expand);
internal sealed record EnvironmentRefreshPlan(
    IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Removed);
internal sealed class EnvironmentRefreshRollbackException(
    IReadOnlyList<string> unrestoredNames, IEnumerable<Exception> errors)
    : AggregateException("环境变量刷新失败，且部分旧值未能恢复。", errors)
{
    public IReadOnlyList<string> UnrestoredNames { get; } = unrestoredNames;
}

internal sealed class EnvironmentRefresh
{
    private readonly HashSet<string> _previousPersistedNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ReferencePattern = new("%([^%]+)%", RegexOptions.CultureInvariant);
    private static readonly Regex PathReferencePattern = new("%PATH%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public EnvironmentRefresh()
    {
        _previousPersistedNames.UnionWith(ReadPersisted(EnvironmentVariableTarget.Machine).Keys);
        _previousPersistedNames.UnionWith(ReadPersisted(EnvironmentVariableTarget.User).Keys);
    }

    public int Reload()
    {
        var machine = ReadPersisted(EnvironmentVariableTarget.Machine);
        var user = ReadPersisted(EnvironmentVariableTarget.User);
        var plan = BuildPlan(machine, user, ReadProcess(), _previousPersistedNames);

        try { ApplyPlan(plan); }
        catch (EnvironmentRefreshRollbackException error)
        {
            RememberUnrestoredNames(_previousPersistedNames, error.UnrestoredNames);
            throw;
        }
        _previousPersistedNames.Clear();
        _previousPersistedNames.UnionWith(plan.Values.Keys);
        return plan.Values.Count;
    }

    internal static void RememberUnrestoredNames(HashSet<string> previousPersistedNames,
        IEnumerable<string> unrestoredNames) => previousPersistedNames.UnionWith(unrestoredNames);

    internal static void ApplyPlan(EnvironmentRefreshPlan plan)
    {
        ApplyPlan(plan,
            name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process),
            (name, value) => Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process));
    }

    internal static void ApplyPlan(EnvironmentRefreshPlan plan, Func<string, string?> read,
        Action<string, string?> write)
    {
        var changes = plan.Removed.Select(name => (Name: name, Value: (string?)null))
            .Concat(plan.Values.Select(pair => (Name: pair.Key, Value: (string?)pair.Value)))
            .ToArray();
        foreach (var (name, value) in changes)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0') || name.Length >= 32767)
                throw new ArgumentException("持久化环境变量名称无效，未刷新进程环境。");
            if (value is { Length: >= 32767 } || value?.Contains('\0') == true)
                throw new ArgumentException("持久化环境变量值无效，未刷新进程环境。");
        }

        var before = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in changes)
            if (!before.ContainsKey(name)) before[name] = read(name);
        var changed = new List<string>();
        try
        {
            foreach (var (name, value) in changes)
            {
                write(name, value);
                changed.Add(name);
            }
        }
        catch (Exception error)
        {
            var rollbackFailures = new List<(string Name, Exception Error)>();
            foreach (var name in changed.AsEnumerable().Reverse().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { write(name, before[name]); }
                catch (Exception rollbackError) { rollbackFailures.Add((name, rollbackError)); }
            }
            if (rollbackFailures.Count > 0)
                throw new EnvironmentRefreshRollbackException(
                    rollbackFailures.Select(failure => failure.Name).ToArray(),
                    new[] { error }.Concat(rollbackFailures.Select(failure => failure.Error)));
            throw;
        }
    }

    internal static EnvironmentRefreshPlan BuildPlan(
        IReadOnlyDictionary<string, PersistedVariable> machine,
        IReadOnlyDictionary<string, PersistedVariable> user,
        IReadOnlyDictionary<string, string> processFallback,
        IEnumerable<string> previouslyPersistedNames)
    {
        var normalizedMachine = new Dictionary<string, PersistedVariable>(machine, StringComparer.OrdinalIgnoreCase);
        var normalizedUser = new Dictionary<string, PersistedVariable>(user, StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, PersistedVariable>(normalizedMachine, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in normalizedUser)
            definitions[name] = value;
        var previous = new HashSet<string>(previouslyPersistedNames, StringComparer.OrdinalIgnoreCase);
        normalizedMachine.TryGetValue("Path", out var machinePath);
        normalizedUser.TryGetValue("Path", out var userPath);
        var resolver = new Resolver(definitions, machinePath, userPath, processFallback, previous);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definitions.Keys)
            values[name] = resolver.Resolve(name) ?? definitions[name].Value;
        var removed = previous.Except(values.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        return new EnvironmentRefreshPlan(values, removed);
    }

    private static Dictionary<string, PersistedVariable> ReadPersisted(EnvironmentVariableTarget target)
    {
        using var key = target == EnvironmentVariableTarget.Machine
            ? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")
            : Registry.CurrentUser.OpenSubKey("Environment");
        var result = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase);
        if (key is null) return result;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0')) continue;
            try
            {
                var kind = key.GetValueKind(name);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
                // Default registry reads expand REG_EXPAND_SZ against this process's old block.
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value)
                    result[name] = new PersistedVariable(value, kind == RegistryValueKind.ExpandString);
            }
            catch (IOException) { /* Ignore one corrupt registry value, not the entire snapshot. */ }
        }
        return result;
    }

    private static Dictionary<string, string> ReadProcess()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            if (entry.Key is string name && entry.Value is string value)
                result[name] = value;
        return result;
    }

    private sealed class Resolver(
        IReadOnlyDictionary<string, PersistedVariable> definitions,
        PersistedVariable? machinePath,
        PersistedVariable? userPath,
        IReadOnlyDictionary<string, string> processFallback,
        HashSet<string> previous)
    {
        public string? Resolve(string name) => Resolve(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);

        private string? Resolve(string name, HashSet<string> stack, string? pathOverride)
        {
            if (stack.Count >= 16 || !stack.Add(name)) return null;
            try
            {
                if (name.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    return ResolvePath(stack);
                if (definitions.TryGetValue(name, out var definition))
                    return Expand(definition, stack, pathOverride);
                if (previous.Contains(name)) return null;
                return processFallback.TryGetValue(name, out var fallback) ? fallback : null;
            }
            finally { stack.Remove(name); }
        }

        private string ResolvePath(HashSet<string> stack)
        {
            var system = machinePath is null ? "" : Expand(machinePath, stack, null).TrimEnd(';');
            var user = userPath is null ? "" : Expand(userPath, stack, system).TrimStart(';');
            if (userPath is { Expand: true } && PathReferencePattern.IsMatch(userPath.Value))
                return user;
            return string.Join(';', new[] { system, user }.Where(value => value.Length > 0));
        }

        private string Expand(PersistedVariable variable, HashSet<string> stack, string? pathOverride)
        {
            if (!variable.Expand) return variable.Value;
            return ReferencePattern.Replace(variable.Value, match =>
            {
                var name = match.Groups[1].Value;
                if (name.Equals("Path", StringComparison.OrdinalIgnoreCase) && pathOverride is not null)
                    return pathOverride;
                return Resolve(name, stack, pathOverride) ?? match.Value;
            });
        }
    }
}
