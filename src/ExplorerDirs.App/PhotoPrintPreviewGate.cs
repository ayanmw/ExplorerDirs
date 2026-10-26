namespace ExplorerDirs.App;

internal sealed class PhotoPrintPreviewGate
{
    private long _settingsVersion;
    private long? _previewVersion;

    internal bool CanPrint => _previewVersion == _settingsVersion;

    internal void SettingsChanged()
    {
        _settingsVersion++;
        _previewVersion = null;
    }

    internal void PreviewGenerated() => _previewVersion = _settingsVersion;

    internal bool TrySubmit(Action submit)
    {
        ArgumentNullException.ThrowIfNull(submit);
        if (!CanPrint) return false;
        submit();
        return true;
    }
}
