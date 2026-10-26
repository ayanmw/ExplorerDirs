using System.IO;

namespace ExplorerDirs.App;

internal sealed record BreadcrumbSegment(string Label, string Path);

internal static class BreadcrumbPath
{
    public static string NormalizeAddressInput(string input)
    {
        if (input.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            input.StartsWith(@"\\.\", StringComparison.Ordinal)) return input;

        var isAbsoluteDrivePath = input.Length >= 3 &&
            char.IsAsciiLetter(input[0]) && input[1] == ':' && input[2] is '\\' or '/';
        var normalized = input.Replace('/', '\\');
        var isUncPath = normalized.StartsWith(@"\\", StringComparison.Ordinal);
        if (!isAbsoluteDrivePath && !isUncPath) return input;

        var result = new System.Text.StringBuilder(normalized.Length);
        var leadingSeparators = isUncPath ? 2 : 0;
        foreach (var character in normalized)
        {
            if (character == '\\' && result.Length > 0 && result[^1] == '\\')
            {
                if (result.Length < leadingSeparators) result.Append(character);
                continue;
            }
            result.Append(character);
        }
        return result.ToString();
    }

    public static IReadOnlyList<BreadcrumbSegment> Build(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];
        if (ShellLocationIdentity.TryNormalize(path, out var identity, out var isFileSystem) && !isFileSystem)
            return [new BreadcrumbSegment(ShellLocationIdentity.GetDisplayName(identity), identity)];
        try
        {
            if (!System.IO.Path.IsPathFullyQualified(path))
                return [new BreadcrumbSegment(path, path)];

            var normalized = path.Replace('/', '\\');
            var root = System.IO.Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root)) return [new BreadcrumbSegment(path, path)];
            var label = root.TrimEnd('\\');
            if (label.Length == 0) label = root;
            List<BreadcrumbSegment> segments = [new(label, root)];
            var current = root;
            foreach (var name in normalized[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, name);
                segments.Add(new BreadcrumbSegment(name, current));
            }
            return segments;
        }
        catch (Exception)
        {
            return [new BreadcrumbSegment(path, path)];
        }
    }
}
