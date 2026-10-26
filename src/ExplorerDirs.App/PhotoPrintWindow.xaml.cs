using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ExplorerDirs.App;

public partial class PhotoPrintWindow : Window
{
    private readonly IReadOnlyList<PhotoPrintImageAsset> _images;
    private readonly IReadOnlyList<PhotoPrintRejectedItem> _rejectedItems;
    private readonly List<PrintQueue> _queues = [];
    private readonly PhotoPrintPreviewGate _previewGate = new();
    private bool _loading;
    private PrintQueue? _previewQueue;
    private PrintTicket? _previewTicket;
    private FixedDocument? _printDocument;

    internal PhotoPrintWindow(IReadOnlyList<PhotoPrintImageAsset> images,
        IReadOnlyList<PhotoPrintRejectedItem> rejectedItems)
    {
        InitializeComponent();
        _images = images;
        _rejectedItems = rejectedItems;
        RejectedItemsList.ItemsSource = rejectedItems.Select(item => new RejectedItemDisplay(item)).ToArray();
        Closed += (_, _) => DisposeQueues();
    }

    internal int SubmittedPhotoCount { get; private set; }
    internal int SubmittedPageCount { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LoadPrinters();

    private void LoadPrinters()
    {
        _loading = true;
        try
        {
            using var server = new LocalPrintServer();
            var defaultName = server.DefaultPrintQueue?.FullName;
            _queues.AddRange(server.GetPrintQueues());
            PrinterPicker.ItemsSource = _queues.Select(queue => new PrinterChoice(queue)).ToArray();
            PrinterPicker.SelectedItem = PrinterPicker.Items.Cast<PrinterChoice>()
                .FirstOrDefault(choice => string.Equals(choice.Queue.FullName, defaultName, StringComparison.OrdinalIgnoreCase))
                ?? PrinterPicker.Items.Cast<PrinterChoice>().FirstOrDefault();
            if (PrinterPicker.SelectedItem is null)
                SettingsError.Text = "Windows 没有可用的打印机队列。";
        }
        catch (Exception ex)
        {
            SettingsError.Text = $"无法读取 Windows 打印机：{ex.Message}";
        }
        finally { _loading = false; }
        ConfigurePaperChoices();
        RebuildPreview();
    }

    private void PrinterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ConfigurePaperChoices();
        RebuildPreview();
    }

    private void ConfigurePaperChoices()
    {
        if (PrinterPicker.SelectedItem is not PrinterChoice choice) return;
        _loading = true;
        try
        {
            var queue = choice.Queue;
            var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
            var capabilities = queue.GetPrintCapabilities(ticket);
            var papers = capabilities.PageMediaSizeCapability
                .Where(media => media.Width is > 0 && media.Height is > 0)
                .GroupBy(media => (media.PageMediaSizeName, media.Width, media.Height))
                .Select(group => group.First())
                .Select(media => new PaperChoice(media))
                .ToArray();
            PaperPicker.ItemsSource = papers;
            var preferred = ticket.PageMediaSize;
            PaperPicker.SelectedItem = papers.FirstOrDefault(paper => SameMediaSize(paper.Size, preferred))
                ?? papers.FirstOrDefault();
            if (PaperPicker.SelectedItem is null)
                SettingsError.Text = "所选打印机没有可用纸张信息。";
        }
        catch (Exception ex)
        {
            PaperPicker.ItemsSource = null;
            SettingsError.Text = $"无法读取打印机纸张能力：{ex.Message}";
        }
        finally { _loading = false; }
    }

    private void PaperPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) RebuildPreview();
    }

    private void Settings_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) RebuildPreview();
    }

    private void Settings_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) RebuildPreview();
    }

    private void RebuildPreview()
    {
        _previewGate.SettingsChanged();
        PrintButton.IsEnabled = false;
        PreviewViewer.Document = null;
        PreviewSummary.Text = string.Empty;
        _printDocument = null;
        _previewQueue = null;
        _previewTicket = null;
        SettingsError.Text = string.Empty;
        if (PrinterPicker.SelectedItem is not PrinterChoice printer || PaperPicker.SelectedItem is not PaperChoice paper)
            return;

        try
        {
            var queue = printer.Queue;
            var seedTicket = queue.DefaultPrintTicket ?? new PrintTicket();
            var requestedTicket = new PrintTicket { PageMediaSize = paper.Size };
            var ticket = queue.MergeAndValidatePrintTicket(seedTicket, requestedTicket).ValidatedPrintTicket;
            var capabilities = queue.GetPrintCapabilities(ticket);
            var pageSize = ticket.PageMediaSize;
            var area = capabilities.PageImageableArea
                ?? throw new InvalidOperationException("打印机未提供可打印区域信息。");
            var pageWidth = pageSize?.Width ?? paper.Size.Width
                ?? throw new InvalidOperationException("打印机未提供纸张宽度。");
            var pageHeight = pageSize?.Height ?? paper.Size.Height
                ?? throw new InvalidOperationException("打印机未提供纸张高度。");
            var widthCm = ParseCentimeters(CustomWidth.Text);
            var heightCm = ParseCentimeters(CustomHeight.Text);
            var count = int.Parse(((ComboBoxItem)PhotosPerPagePicker.SelectedItem).Tag.ToString()!);
            var fit = FillMode.IsChecked == true ? PhotoPrintFitMode.Fill : PhotoPrintFitMode.Fit;
            var layout = PhotoPrintLayout.Arrange(_images.Select(image => image.AspectRatio).ToArray(),
                area.ExtentWidth, area.ExtentHeight, count, fit, widthCm, heightCm,
                area.OriginWidth, area.OriginHeight);
            var preview = PhotoPrintDocument.Create(_images, layout, pageWidth, pageHeight, usePreviewSources: true);
            _printDocument = PhotoPrintDocument.Create(_images, layout, pageWidth, pageHeight, usePreviewSources: false);
            PreviewViewer.Document = preview;
            _previewQueue = queue;
            _previewTicket = ticket;
            _previewGate.PreviewGenerated();
            PreviewSummary.Text = $"{_images.Count:N0} 张照片 · {layout.PageCount:N0} 页";
            PrintButton.IsEnabled = _previewGate.CanPrint;
        }
        catch (Exception ex)
        {
            SettingsError.Text = $"无法生成当前打印预览：{ex.Message}";
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewQueue is null || _previewTicket is null || _printDocument is null || !_previewGate.CanPrint)
            return;
        try
        {
            _previewGate.TrySubmit(() =>
            {
                _previewQueue.Refresh();
                var dialog = new PrintDialog { PrintQueue = _previewQueue, PrintTicket = _previewTicket };
                dialog.PrintDocument(_printDocument.DocumentPaginator, "Explorer Dirs - 所选照片");
                SubmittedPhotoCount = _images.Count;
                SubmittedPageCount = _printDocument.Pages.Count;
                DialogResult = true;
            });
        }
        catch (Exception ex)
        {
            SettingsError.Text = $"提交打印任务失败：{ex.Message}";
        }
    }

    private static double? ParseCentimeters(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out var centimeters))
            throw new FormatException("自定义尺寸必须是数字。");
        if (!double.IsFinite(centimeters) || centimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(text), "自定义尺寸必须大于零。");
        return centimeters;
    }

    private static bool SameMediaSize(PageMediaSize candidate, PageMediaSize? target) => target is not null &&
        candidate.PageMediaSizeName == target.PageMediaSizeName &&
        Math.Abs((candidate.Width ?? 0) - (target.Width ?? 0)) < 0.1 &&
        Math.Abs((candidate.Height ?? 0) - (target.Height ?? 0)) < 0.1;

    private void DisposeQueues()
    {
        foreach (var queue in _queues) queue.Dispose();
        _queues.Clear();
    }

    private sealed record PrinterChoice(PrintQueue Queue)
    {
        public override string ToString() => Queue.Name;
    }

    private sealed record PaperChoice(PageMediaSize Size)
    {
        public override string ToString()
        {
            var widthMm = Size.Width!.Value * 25.4 / 96;
            var heightMm = Size.Height!.Value * 25.4 / 96;
            return $"{Size.PageMediaSizeName}: {widthMm:F0} × {heightMm:F0} 毫米";
        }
    }

    private sealed record RejectedItemDisplay(PhotoPrintRejectedItem Item)
    {
        public string DisplayName => $"{Item.DisplayName} — {Item.Reason}";
    }
}
