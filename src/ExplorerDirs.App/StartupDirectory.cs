using System.IO;

namespace ExplorerDirs.App;

internal sealed record StartupDirectoryRequest(string? Directory, string? QdrFile, string? SelectPath, string? Warning);

internal static class StartupDirectory
{
    public static StartupDirectoryRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return new(null, null, null, null);
        if (arguments.Count == 1 && arguments[0].StartsWith("/select,", StringComparison.OrdinalIgnoreCase))
            return ParseSelection(arguments[0][8..]);
        if (arguments.Count == 2 && arguments[0].Equals("/select,", StringComparison.OrdinalIgnoreCase))
            return ParseSelection(arguments[1]);
        if (arguments.Count == 2 && arguments[0].Equals("/select", StringComparison.OrdinalIgnoreCase))
            return ParseSelection(arguments[1]);
        if (arguments.Count != 1)
            return new(null, null, null, "启动参数只能包含一个目录或 Q-Dir 状态文件；已按原会话打开。");

        var value = arguments[0];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.StartsWith('/'))
            return new(null, null, null, "不支持的启动参数；已按原会话打开。");
        try
        {
            // Resolving a path is filesystem-free. Offline shares remain valid targets.
            var fullPath = Path.GetFullPath(value);
            return Path.GetExtension(value).Equals(".qdr", StringComparison.OrdinalIgnoreCase)
                ? new(null, fullPath, null, null)
                : new(fullPath, null, null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(null, null, null, "启动路径无效；已按原会话打开。");
        }
    }

    private static StartupDirectoryRequest ParseSelection(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new(null, null, null, "缺少要定位的文件或文件夹；已按原会话打开。");
        try
        {
            // Resolving the item and its parent is filesystem-free; Shell performs the actual lookup.
            var fullPath = Path.GetFullPath(value);
            var parent = Path.GetDirectoryName(fullPath);
            return parent is null
                ? new(null, null, null, "无法定位磁盘根目录中的项目；已按原会话打开。")
                : new(parent, null, fullPath, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(null, null, null, "启动定位路径无效；已按原会话打开。");
        }
    }
}
