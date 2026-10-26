using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class PhotoPrintPreviewGateSmoke
{
    internal static void Run()
    {
        var gate = new PhotoPrintPreviewGate();
        var submitted = 0;
        Require(!gate.CanPrint && !gate.TrySubmit(() => submitted++),
            "a print request cannot be submitted before a preview exists");
        gate.PreviewGenerated();
        Require(gate.CanPrint && gate.TrySubmit(() => submitted++) && submitted == 1,
            "a current preview allows exactly the explicit print dispatch");
        gate.SettingsChanged();
        Require(!gate.CanPrint && !gate.TrySubmit(() => submitted++ ) && submitted == 1,
            "changing paper or layout invalidates the old preview and submits no print job");
        Console.WriteLine("Photo print preview gate smoke: PASS");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Photo print preview gate: " + description);
    }
}
