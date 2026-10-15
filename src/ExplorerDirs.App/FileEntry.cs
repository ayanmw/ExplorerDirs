using System.IO;

namespace ExplorerDirs.App;

internal sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long? Size, DateTime Modified)
{
    public string Kind => IsDirectory ? "文件夹" : "文件";
    public string SizeText => Size?.ToString("N0") ?? "";
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

    public static IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken)
    {
        var items = new List<FileEntry>();
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var isDirectory = info is DirectoryInfo;
                var size = info is FileInfo file ? file.Length : (long?)null;
                items.Add(new FileEntry(info.Name, info.FullName, isDirectory, size, info.LastWriteTime));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return items.OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
