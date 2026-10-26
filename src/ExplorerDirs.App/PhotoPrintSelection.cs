namespace ExplorerDirs.App;

internal sealed record PhotoPrintSelectionCandidate(
    string DisplayName,
    string? FileSystemPath,
    bool IsFileSystemItem,
    bool IsDirectory);

internal sealed record PhotoPrintRejectedItem(string DisplayName, string Reason);

internal sealed record PhotoPrintSelectionResult(
    IReadOnlyList<string> CandidatePaths,
    IReadOnlyList<PhotoPrintRejectedItem> RejectedItems);

internal static class PhotoPrintSelection
{
    internal static PhotoPrintSelectionResult Filter(IEnumerable<PhotoPrintSelectionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<string> accepted = [];
        List<PhotoPrintRejectedItem> rejected = [];
        foreach (var candidate in candidates)
        {
            if (!candidate.IsFileSystemItem || string.IsNullOrWhiteSpace(candidate.FileSystemPath))
                rejected.Add(new PhotoPrintRejectedItem(candidate.DisplayName, "虚拟项目没有可读取的文件路径。"));
            else if (candidate.IsDirectory)
                rejected.Add(new PhotoPrintRejectedItem(candidate.DisplayName, "文件夹不能作为照片打印。"));
            else
                accepted.Add(candidate.FileSystemPath);
        }
        return new PhotoPrintSelectionResult(accepted, rejected);
    }
}
