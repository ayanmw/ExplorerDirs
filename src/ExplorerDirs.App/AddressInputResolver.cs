using System.IO;

namespace ExplorerDirs.App;

internal static class AddressInputResolver
{
    internal static string Resolve(string input, string? currentFileSystemPath)
    {
        var value = (input ?? string.Empty).Trim().Trim('"');
        // Explicit device paths are verbatim. Expanding a percent-named component
        // before recognizing the prefix can silently redirect it to another folder.
        if (value.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            value.StartsWith("\\\\.\\", StringComparison.Ordinal)) return value;
        value = BreadcrumbPath.NormalizeAddressInput(Environment.ExpandEnvironmentVariables(value));
        if (value.Length == 0) return string.Empty;

        if (value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
            return value + Path.DirectorySeparatorChar;

        if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("::{", StringComparison.Ordinal) ||
            value.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            value.StartsWith("\\\\.\\", StringComparison.Ordinal))
            return value;

        var hasDrivePrefix = value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri && !hasDrivePrefix)
            return value;

        if (Path.IsPathFullyQualified(value)) return value;

        // Drive-relative forms such as C:folder depend on process per-drive state;
        // leave them to the existing Shell validator instead of guessing a base.
        if (value.Length >= 3 && hasDrivePrefix && value[2] is not ('\\' or '/'))
            return value;

        if (string.IsNullOrWhiteSpace(currentFileSystemPath) ||
            !ShellLocationIdentity.TryNormalize(currentFileSystemPath, out var basePath, out var isFileSystem) ||
            !isFileSystem)
            return value;

        try
        {
            return Path.GetFullPath(value.Replace('/', '\\'), basePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value;
        }
    }
}
