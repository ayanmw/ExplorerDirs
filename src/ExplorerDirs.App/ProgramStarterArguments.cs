using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ExplorerDirs.App;

internal sealed record ProgramStarterPaneContext(
    string Directory,
    IReadOnlyList<string> SelectedFiles,
    IReadOnlyList<string> VisibleFiles);

internal sealed record ProgramStarterContext(
    int ActivePaneIndex,
    IReadOnlyList<ProgramStarterPaneContext> Panes,
    string ApplicationPath,
    string WindowsDirectory,
    string SystemDirectory,
    string DesktopDirectory,
    string AppDataDirectory,
    string PersonalDirectory);

internal sealed record ProgramStarterCommand(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

internal sealed record ProgramStarterBuildResult(ProgramStarterCommand? Command, string? Error)
{
    public bool Success => Command is not null;
}

internal static class ProgramStarterArguments
{
    private static readonly Regex PlaceholderToken = new("%[^%]+%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ProgramStarterBuildResult Build(ProgramStarterEntry entry, ProgramStarterContext context)
    {
        if (context.Panes.Count is < 1 or > 4 || context.ActivePaneIndex < 0 || context.ActivePaneIndex >= context.Panes.Count)
            return Failure("当前窗格上下文无效。");

        try
        {
            var scalarValues = CreateScalarValues(context);
            var executable = ExpandScalar(entry.ExecutablePath, scalarValues);
            var applicationDirectory = Path.GetDirectoryName(Path.GetFullPath(context.ApplicationPath)) ?? string.Empty;
            var hasWorkingDirectory = !string.IsNullOrWhiteSpace(entry.WorkingDirectory);
            var workingDirectory = hasWorkingDirectory
                ? ExpandScalar(entry.WorkingDirectory, scalarValues)
                : context.Panes[context.ActivePaneIndex].Directory;
            if (hasWorkingDirectory && !Path.IsPathFullyQualified(workingDirectory))
                workingDirectory = Path.GetFullPath(Path.Combine(applicationDirectory, workingDirectory));
            else if (!hasWorkingDirectory && !Path.IsPathFullyQualified(workingDirectory))
                workingDirectory = applicationDirectory;
            if (string.IsNullOrWhiteSpace(executable)) return Failure("程序路径不能为空。");
            if (!Path.IsPathFullyQualified(workingDirectory))
                return Failure("工作目录必须是完整的文件系统路径。");

            var arguments = new List<string>();
            foreach (var token in SplitWindowsArguments(entry.Arguments))
            {
                if (TryGetListPlaceholder(token, context, out var paths, out var listError))
                {
                    if (listError is not null) return Failure(listError);
                    if (paths.Count == 0) return Failure($"占位符 {token} 没有可传递的文件。");
                    arguments.AddRange(paths);
                }
                else
                    arguments.Add(ExpandScalar(token, scalarValues));
            }

            return new ProgramStarterBuildResult(new ProgramStarterCommand(executable, arguments, workingDirectory), null);
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message);
        }
    }

    private static ProgramStarterBuildResult Failure(string message) => new(null, message);

    private static Dictionary<string, string> CreateScalarValues(ProgramStarterContext context)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%windir%"] = context.WindowsDirectory,
            ["%sysdir%"] = context.SystemDirectory,
            ["%desktop%"] = context.DesktopDirectory,
            ["%appdat%"] = context.AppDataDirectory,
            ["%personal%"] = context.PersonalDirectory
        };
        var root = Path.GetPathRoot(Path.GetFullPath(context.ApplicationPath));
        if (!string.IsNullOrEmpty(root)) values["%drive%"] = root;
        for (var index = 0; index < context.Panes.Count; index++)
        {
            var paneNumber = index + 1;
            values[$"%curdir{paneNumber}%"] = context.Panes[index].Directory;
        }
        values["%curdir%"] = context.Panes[context.ActivePaneIndex].Directory;

        var selected = context.Panes[context.ActivePaneIndex].SelectedFiles;
        if (selected.Count > 0 && Path.IsPathFullyQualified(selected[0]))
            values["%fdir%"] = Path.GetDirectoryName(selected[0]) ?? string.Empty;
        return values;
    }

    private static string ExpandScalar(string text, IReadOnlyDictionary<string, string> scalarValues)
    {
        var matches = PlaceholderToken.Matches(text);
        var covered = new bool[text.Length];
        foreach (Match match in matches)
            Array.Fill(covered, true, match.Index, match.Length);
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '%' && !covered[index])
                throw new ArgumentException("参数中包含格式不完整的 % 占位符。");

        return PlaceholderToken.Replace(text, match =>
        {
            if (IsListPlaceholder(match.Value))
                throw new ArgumentException($"列表占位符 {match.Value} 必须作为独立参数。");
            if (!scalarValues.TryGetValue(match.Value, out var replacement))
                throw new ArgumentException($"存在未知占位符：{match.Value}");
            return replacement;
        });
    }

    private static bool IsListPlaceholder(string value) =>
        value.Equals("%sel_files%", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("%all_files%", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(value, "^%(sel|all)_files[1-4]%$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryGetListPlaceholder(string token, ProgramStarterContext context,
        out IReadOnlyList<string> paths, out string? error)
    {
        paths = [];
        error = null;
        if (!IsListPlaceholder(token)) return false;

        var paneIndex = context.ActivePaneIndex;
        var name = token[1..^1];
        var suffixStart = name.Length - 1;
        if (suffixStart >= 0 && name[suffixStart] is >= '1' and <= '4')
        {
            paneIndex = name[suffixStart] - '1';
            name = name[..suffixStart];
        }
        if (paneIndex < 0 || paneIndex >= context.Panes.Count)
        {
            error = $"占位符 {token} 指向当前不存在的窗格。";
            return true;
        }

        paths = name.Equals("sel_files", StringComparison.OrdinalIgnoreCase)
            ? context.Panes[paneIndex].SelectedFiles
            : context.Panes[paneIndex].VisibleFiles;
        if (paths.Any(path => !Path.IsPathFullyQualified(path) || path.StartsWith("::{", StringComparison.Ordinal)))
        {
            error = $"占位符 {token} 包含无法作为文件参数传递的 Shell 虚拟项目。";
            paths = [];
        }
        return true;
    }

    private static IReadOnlyList<string> SplitWindowsArguments(string commandLine)
    {
        List<string> result = [];
        var index = 0;
        while (true)
        {
            while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index])) index++;
            if (index >= commandLine.Length) return result;

            var argument = new StringBuilder();
            var quoted = false;
            while (index < commandLine.Length && (quoted || !char.IsWhiteSpace(commandLine[index])))
            {
                var slashes = 0;
                while (index < commandLine.Length && commandLine[index] == '\\')
                {
                    slashes++;
                    index++;
                }

                if (index < commandLine.Length && commandLine[index] == '"')
                {
                    argument.Append('\\', slashes / 2);
                    if (slashes % 2 == 1)
                    {
                        argument.Append('"');
                        index++;
                    }
                    else if (quoted && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                    {
                        argument.Append('"');
                        index += 2;
                    }
                    else
                    {
                        quoted = !quoted;
                        index++;
                    }
                }
                else
                {
                    argument.Append('\\', slashes);
                    if (index < commandLine.Length && (quoted || !char.IsWhiteSpace(commandLine[index])))
                        argument.Append(commandLine[index++]);
                }
            }

            if (quoted) throw new ArgumentException("参数中的引号未闭合。");
            result.Add(argument.ToString());
        }
    }
}
