using System.IO;

namespace ExplorerDirs.App;

internal sealed record BreadcrumbSegment(string Label, string Path);

internal static class BreadcrumbPath
{
    public static IReadOnlyList<BreadcrumbSegment> Build(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];
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
