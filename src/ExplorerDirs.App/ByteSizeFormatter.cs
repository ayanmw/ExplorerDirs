using System.Globalization;

namespace ExplorerDirs.App;

internal enum SizeDisplayUnit
{
    Auto,
    Bytes,
    Kilobytes,
    Megabytes,
    Gigabytes,
    Terabytes
}

internal sealed record SizeDisplayOptions(SizeDisplayUnit Unit, int DecimalPlaces, bool UseThousandsSeparator,
    bool ShowRecursiveCounts = true)
{
    internal static SizeDisplayOptions Default { get; } = new(SizeDisplayUnit.Auto, 2, false);
}

internal static class ByteSizeFormatter
{
    private static SizeDisplayOptions _options = SizeDisplayOptions.Default;

    internal static event EventHandler? OptionsChanged;
    internal static SizeDisplayOptions Options => _options;

    internal static void SetOptions(SizeDisplayOptions options)
    {
        Validate(options);
        if (_options == options) return;
        _options = options;
        OptionsChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static string Format(ulong bytes)
        => Format(bytes, _options);

    internal static string Format(ulong bytes, SizeDisplayOptions options)
    {
        Validate(options);
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var value = (decimal)bytes;
        var unit = options.Unit == SizeDisplayUnit.Auto ? 0 : (int)options.Unit - 1;
        if (options.Unit == SizeDisplayUnit.Auto)
        {
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        }
        else if (unit > 0)
        {
            for (var index = 0; index < unit; index++) value /= 1024;
        }

        var numberFormat = (options.UseThousandsSeparator ? "#,##0" : "0") +
            (options.DecimalPlaces == 0 ? "" : "." + new string('#', options.DecimalPlaces));
        return value.ToString(numberFormat, CultureInfo.CurrentCulture) + " " + units[unit];
    }

    internal static string FormatRecursiveDirectorySummary(RecursiveDirectoryScanResult result, SizeDisplayOptions options)
    {
        var summary = $"目录总计 {Format(result.Bytes, options)}";
        if (options.ShowRecursiveCounts)
            summary += $" · {result.Files:N0} 个文件 · {result.Directories:N0} 个子目录";
        if (result.Unreadable > 0) summary += $" · 无法读取/已跳过 {result.Unreadable:N0} 处";
        return summary;
    }

    private static void Validate(SizeDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Unit)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.DecimalPlaces is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(options));
    }
}
