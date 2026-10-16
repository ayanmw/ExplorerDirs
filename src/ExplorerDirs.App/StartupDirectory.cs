using System.IO;

namespace ExplorerDirs.App;

internal sealed record StartupDirectoryRequest(string? Directory, string? Warning);

internal static class StartupDirectory
{
    public static StartupDirectoryRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return new(null, null);
        if (arguments.Count != 1)
            return new(null, "启动参数只能包含一个目录；已按原会话打开。");

        var value = arguments[0];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.StartsWith('/'))
            return new(null, "不支持的启动目录参数；已按原会话打开。");
        try
        {
            // Resolving a path is filesystem-free. Offline shares remain valid targets.
            return new(Path.GetFullPath(value), null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(null, "启动目录路径无效；已按原会话打开。");
        }
    }
}
