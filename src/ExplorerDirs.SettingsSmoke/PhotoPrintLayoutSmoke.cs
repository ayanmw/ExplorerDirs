using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class PhotoPrintLayoutSmoke
{
    internal static void Run()
    {
        foreach (var photosPerPage in new[] { 1, 2, 4, 6, 9 })
        {
            var ratios = Enumerable.Repeat(1d, photosPerPage + 1).ToArray();
            var layout = PhotoPrintLayout.Arrange(ratios, 600, 800, photosPerPage, PhotoPrintFitMode.Fit);
            Require(layout.PageCount == 2 && layout.Placements.Count == photosPerPage + 1 &&
                    layout.Placements.Take(photosPerPage).All(item => item.PageIndex == 0) &&
                    layout.Placements[^1].PageIndex == 1,
                $"{photosPerPage} photos per page creates correct page count and preserves item order");
        }

        var fit = PhotoPrintLayout.Arrange([2d, 0.5d], 600, 800, 1, PhotoPrintFitMode.Fit);
        Require(fit.Placements[0].SourceCrop == new PhotoPrintUnitRect(0, 0, 1, 1) &&
                fit.Placements[0].Destination.Width / fit.Placements[0].Destination.Height == 2 &&
                fit.Placements[1].Destination.Width / fit.Placements[1].Destination.Height == 0.5,
            "Fit preserves landscape and portrait image ratios without cropping");

        var fill = PhotoPrintLayout.Arrange([2d, 0.5d], 600, 800, 1, PhotoPrintFitMode.Fill);
        Require(fill.Placements[0].SourceCrop.Width < 1 && fill.Placements[0].SourceCrop.Height == 1 &&
                fill.Placements[1].SourceCrop.Width == 1 && fill.Placements[1].SourceCrop.Height < 1,
            "Fill crops only the source dimension needed to cover each target cell");

        var custom = PhotoPrintLayout.Arrange([1d], 600, 800, 1, PhotoPrintFitMode.Fit, 10, 15);
        Require(Math.Abs(custom.Placements[0].Cell.Width - 10 * 96 / 2.54) < 0.001 &&
                Math.Abs(custom.Placements[0].Cell.Height - 15 * 96 / 2.54) < 0.001,
            "custom centimeter dimensions convert to physical WPF page units");

        var imageable = PhotoPrintLayout.Arrange([1d], 500, 700, 1, PhotoPrintFitMode.Fit,
            printableOriginX: 17, printableOriginY: 29);
        Require(imageable.Placements[0].Cell.X >= 17 + 24 && imageable.Placements[0].Cell.Y >= 29 + 24,
            "photo cells are offset into the printer's imageable area");

        Throws<ArgumentOutOfRangeException>(() => PhotoPrintLayout.Arrange([1d], 600, 800, 3, PhotoPrintFitMode.Fit),
            "unsupported photos-per-page values are rejected");
        Throws<ArgumentOutOfRangeException>(() => PhotoPrintLayout.Arrange([double.NaN], 600, 800, 1, PhotoPrintFitMode.Fit),
            "invalid source ratios are rejected");
        Throws<ArgumentOutOfRangeException>(() => PhotoPrintLayout.Arrange([1d], 600, 800, 1, PhotoPrintFitMode.Fit, 0, 10),
            "custom dimensions must both be positive");
        Console.WriteLine("Photo print layout smoke: PASS");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Photo print layout: " + description);
    }

    private static void Throws<TException>(Action action, string description) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception("Photo print layout: " + description);
    }
}
