using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ExplorerDirs.App;

internal sealed record FolderEntryState(string Path, string State)
{
    public string DisplayState => State switch
    {
        "missing" => "未启用",
        "owned" => "已启用",
        "conflict" => "存在冲突",
        _ => "未知"
    };
}
internal sealed record FolderEntryPreview(
    string Executable, string Sha256, bool DirectoryArgumentDeclared,
    string DirectoryVerb, string DriveVerb, string Command,
    IReadOnlyList<FolderEntryState> CurrentState);

internal sealed class FolderEntryManager
{
    private readonly string? _scriptPath;

    public FolderEntryManager(string applicationPath)
    {
        _scriptPath = FindPackagedScript(Path.GetDirectoryName(Path.GetFullPath(applicationPath))!);
    }

    public bool IsAvailable => _scriptPath is not null;

    public async Task<IReadOnlyList<FolderEntryState>> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunAsync("Status", cancellationToken);
        return ParseStates(json);
    }

    public async Task<FolderEntryPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunAsync("Preview", cancellationToken);
        return ParsePreview(json);
    }

    public async Task<IReadOnlyList<FolderEntryState>> EnableAsync(
        string executable, string sha256, CancellationToken cancellationToken = default)
    {
        var json = await RunAsync("Enable", cancellationToken,
            "-Executable", executable, "-ExpectedSha256", sha256, "-ConfirmVerifiedRelease");
        return ParseStates(json);
    }

    public async Task<IReadOnlyList<FolderEntryState>> DisableAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunAsync("Disable", cancellationToken);
        return ParseStates(json);
    }

    internal static IReadOnlyList<FolderEntryState> ParseStates(string json) =>
        JsonSerializer.Deserialize<FolderEntryState[]>(json, JsonOptions) ?? [];

    internal static FolderEntryPreview ParsePreview(string json) =>
        JsonSerializer.Deserialize<FolderEntryPreview>(json, JsonOptions)
        ?? throw new InvalidDataException("文件夹入口预览没有返回有效数据。");

    internal static string? FindPackagedScript(string applicationDirectory)
    {
        var directory = new DirectoryInfo(applicationDirectory);
        for (var level = 0; directory is not null && level < 3; level++, directory = directory.Parent!)
        {
            var script = Path.Combine(directory.FullName, "tools", "folder-verb.ps1");
            if (File.Exists(script) && File.Exists(Path.Combine(directory.FullName, "tools", "FolderVerb.psm1")))
                return script;
        }
        return null;
    }

    private async Task<string> RunAsync(string mode, CancellationToken cancellationToken, params string[] arguments)
    {
        if (_scriptPath is null)
            throw new InvalidOperationException("当前是源码调试目录，文件夹入口仅支持已发布版本。");

        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("找不到 Windows PowerShell。", powershell);

        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_scriptPath)!
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(_scriptPath);
        start.ArgumentList.Add("-Mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("-AsJson");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Windows PowerShell。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "文件夹入口脚本执行失败。" : error.Trim());
        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidDataException("文件夹入口脚本没有返回状态数据。");
        return output.Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
