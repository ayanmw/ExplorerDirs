using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class PhotoPrintImageLoaderSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"photo-print-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "一张.png");
        var brokenPath = Path.Combine(root, "坏图.jpg");
        var textPath = Path.Combine(root, "文本.txt");
        var lastPath = Path.Combine(root, "last.png");
        try
        {
            SavePng(firstPath, width: 8, height: 4);
            File.WriteAllBytes(brokenPath, [0, 1, 2, 3, 4]);
            File.WriteAllText(textPath, "not an image");
            SavePng(lastPath, width: 4, height: 8);

            var result = PhotoPrintImageLoader.DecodeAsync([firstPath, brokenPath, textPath, lastPath])
                .GetAwaiter().GetResult();
            Require(result.Images.Select(image => image.Path).SequenceEqual([firstPath, lastPath]),
                "valid images remain ordered around a corrupt image");
            Require(result.RejectedItems.Count == 2 &&
                    result.RejectedItems.Select(item => item.DisplayName).SequenceEqual(["坏图.jpg", "文本.txt"]) &&
                    !string.IsNullOrWhiteSpace(result.RejectedItems[0].Reason),
                "corrupt images and non-image files are reported individually without rejecting valid images");
            Require(result.Images.All(image => image.Source.IsFrozen && image.PreviewSource.IsFrozen),
                "decoded print and preview sources are frozen for cross-thread document use");
            Require(result.Images[0].Source.PixelWidth == 8 && result.Images[0].Source.PixelHeight == 4,
                "print source preserves full decoded pixel dimensions");
            Require(result.Images.All(image => image.PreviewSource.PixelWidth <= PhotoPrintImageLoader.MaximumPreviewDimension &&
                                                image.PreviewSource.PixelHeight <= PhotoPrintImageLoader.MaximumPreviewDimension),
                "preview source dimensions are bounded independently of the print source");
            RunOnSta(() =>
            {
                var layout = PhotoPrintLayout.Arrange([result.Images[0].AspectRatio, result.Images[1].AspectRatio],
                    600, 800, 1, PhotoPrintFitMode.Fit);
                var document = PhotoPrintDocument.Create(result.Images, layout, 600, 800, usePreviewSources: true);
                Require(document.Pages.Count == 2 && layout.PageCount == 2,
                    "the fixed document paginator contains the expected pages for the captured image order");
            });
            using (new FileStream(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using var cancelled = new CancellationTokenSource();
            var interruptedPaths = new CancelBetweenImages(firstPath, lastPath, cancelled);
            try
            {
                PhotoPrintImageLoader.DecodeAsync(interruptedPaths, cancelled.Token).GetAwaiter().GetResult();
                throw new Exception("cancelled decoding returned a partial successful image batch");
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            using (new FileStream(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Console.WriteLine("Photo print image loader smoke: PASS");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CancelBetweenImages(string first, string second, CancellationTokenSource stop)
        : IReadOnlyList<string>
    {
        public int Count => 2;
        public string this[int index] => index switch
        {
            0 => first, 1 => second, _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        public IEnumerator<string> GetEnumerator()
        {
            yield return first;
            stop.Cancel();
            yield return second;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void SavePng(string path, int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)0xFF, width * height * 4).ToArray();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Photo print image loader: " + description);
    }
}
