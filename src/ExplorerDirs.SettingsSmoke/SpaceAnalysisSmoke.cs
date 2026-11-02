using System.IO;
using ExplorerDirs.App;

internal static class SpaceAnalysisSmoke
{
    internal static async Task Run()
    {
        decimal[] sizes = [0, 1, 7, 31, 2, 100, 4];
        var rectangles = SpaceTreemapLayout.Create(sizes, 900, 500);
        var total = sizes.Sum();
        if (rectangles.Count != sizes.Count(size => size > 0)) throw new Exception("Treemap lost a nonzero item.");
        foreach (var rectangle in rectangles)
        {
            var expectedArea = 900 * 500 * (double)(sizes[rectangle.ItemIndex] / total);
            if (rectangle.Width <= 0 || rectangle.Height <= 0 || rectangle.X < -0.001 || rectangle.Y < -0.001 ||
                rectangle.X + rectangle.Width > 900.001 || rectangle.Y + rectangle.Height > 500.001 ||
                Math.Abs(rectangle.Width * rectangle.Height - expectedArea) > 0.01)
                throw new Exception("Treemap area or bounds are incorrect.");
            foreach (var other in rectangles.Where(item => item.ItemIndex != rectangle.ItemIndex))
                if (Math.Min(rectangle.X + rectangle.Width, other.X + other.Width) - Math.Max(rectangle.X, other.X) > 0.001 &&
                    Math.Min(rectangle.Y + rectangle.Height, other.Y + other.Height) - Math.Max(rectangle.Y, other.Y) > 0.001)
                    throw new Exception("Treemap rectangles overlap.");
        }
        if (SpaceTreemapLayout.Create([0, 0], 100, 100).Count != 0 ||
            SpaceTreemapLayout.Create([1], double.NaN, 100).Count != 0 ||
            SpaceTreemapLayout.Create([(decimal)ulong.MaxValue, (decimal)ulong.MaxValue], 200, 100).Count != 2)
            throw new Exception("Treemap zero/large size handling failed.");
        var root = Path.Combine(AppContext.BaseDirectory, "space-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "child", "nested"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "root.bin"), new byte[13]);
            File.WriteAllBytes(Path.Combine(root, "child", "a.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(root, "child", "nested", "b.bin"), new byte[2]);
            var snapshot = await SpaceAnalysisSnapshot.ReadAsync(root, FolderSizeSource.BuiltIn, CancellationToken.None);
            if (snapshot.KnownBytes != 20 || snapshot.Entries.Count != 2 || snapshot.IsPartial ||
                snapshot.Entries.Single(item => item.IsDirectory).Bytes != 7 ||
                snapshot.Entries.Single(item => item.IsDirectory).Percentage != 35)
                throw new Exception("Space analysis double-counted subtrees or calculated incorrect percentages.");
            var unavailable = new FolderSizeDataSource((_, _, _) => Task.FromResult(new IndexedSizePage(EverythingSizeStatus.IndexUnavailable, [])));
            var partial = await SpaceAnalysisSnapshot.ReadAsync(root, FolderSizeSource.Everything, CancellationToken.None, dataSource: unavailable);
            if (!partial.IsPartial || partial.KnownBytes != 13 || partial.Entries.Single(item => item.IsDirectory).Bytes.HasValue)
                throw new Exception("Unavailable directory sizes were treated as zero/complete.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await SpaceAnalysisSnapshot.ReadAsync(root, FolderSizeSource.BuiltIn, cancelled.Token); throw new Exception("Cancelled analysis completed."); }
            catch (OperationCanceledException) { }
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))
                Directory.Delete(root, true);
        }
        Console.WriteLine("Space analysis smoke: PASS (treemap area/overlap/bounds/large sizes; real subtree totals and percentages; unavailable values; cancellation; no UI)");
    }
}
