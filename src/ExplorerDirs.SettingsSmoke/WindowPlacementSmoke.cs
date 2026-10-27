using ExplorerDirs.App;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.SettingsSmoke;

internal static class WindowPlacementSmoke
{
    internal static void Run(string root)
    {
        var path = WindowPlacementStore.PathForSession(Path.Combine(root, "ExplorerDirs.ini"));
        var store = new WindowPlacementStore(path);
        Check(store.Load() is null && store.LoadWarning is null, "old profiles keep default startup until a position is saved");
        var leftMonitor = new WindowPlacementState(-1800, -240, -600, 560, false);
        store.Save(leftMonitor);
        Check(new WindowPlacementStore(path).Load() == leftMonitor,
            "negative left/upper monitor coordinates and normal window size survive reopening the store");
        var maximized = leftMonitor with { Maximized = true };
        store.Save(maximized);
        Check(new WindowPlacementStore(path).Load() == maximized, "maximized state preserves the ordinary restore rectangle");

        var otherPath = WindowPlacementStore.PathForSession(Path.Combine(root, "ExplorerDirs.window-123.ini"));
        var other = new WindowPlacementStore(otherPath);
        Check(other.Load() is null, "each window begins with its own placement sidecar");
        other.Save(new WindowPlacementState(2000, 100, 3300, 1000, false));
        Check(new WindowPlacementStore(path).Load() == maximized, "saving a second window does not change the first window's monitor");

        var native = NativeWindowPlacement.ToNative(maximized);
        Check(Marshal.SizeOf<NativeWindowPlacement.Placement>() == 44 && native.Length == 44,
            "the Win32 WINDOWPLACEMENT ABI has the required 44-byte layout");
        Check(NativeWindowPlacement.FromNative(native, false) == maximized,
            "native restoration preserves signed workspace coordinates without a DIP conversion");
        native.ShowCommand = 2;
        Check(NativeWindowPlacement.FromNative(native, true).Maximized,
            "minimizing a maximized window retains maximized restoration");
        Check(!NativeWindowPlacement.FromNative(native, false).Maximized,
            "minimizing a normal window retains normal restoration");
        Check(NativeWindowPlacement.ToNative(NativeWindowPlacement.FromNative(native, false)).ShowCommand == 1,
            "reopening never restores the minimized state");
        Check(!new WindowPlacementState(100, 0, 100, 800, false).IsValid &&
              !new WindowPlacementState(int.MinValue, 0, int.MaxValue, 800, false).IsValid,
            "empty and overflowing rectangles cannot reach native restoration");

        var prior = File.ReadAllText(path);
        File.WriteAllText(path, prior + "; externally changed\n");
        ExpectIOException(() => store.Save(leftMonitor), "external placement edits are protected");
        Check(File.ReadAllText(path) == prior + "; externally changed\n", "conflicts leave the file untouched");

        var futurePath = Path.Combine(root, "future-placement.ini");
        var future = prior.Replace("FormatVersion=1", "FormatVersion=999999999999999999999999");
        File.WriteAllText(futurePath, future);
        var futureStore = new WindowPlacementStore(futurePath);
        Check(futureStore.Load() is null && futureStore.LoadWarning is not null, "newer formats fall back without interpreting their coordinates");
        ExpectIOException(() => futureStore.Save(leftMonitor), "newer placement formats cannot be overwritten");
        Check(File.ReadAllText(futurePath) == future, "newer format bytes remain intact");

        var invalidPath = Path.Combine(root, "invalid-placement.ini");
        File.WriteAllText(invalidPath, prior.Replace("Right=-600", "Right=-1800"));
        var invalidStore = new WindowPlacementStore(invalidPath);
        Check(invalidStore.Load() is null && invalidStore.LoadWarning is not null &&
              Directory.GetFiles(root, "invalid-placement.ini.invalid-*.bak").Length == 1,
            "damaged geometry is backed up before falling back to the default window");
        invalidStore.Save(leftMonitor);
        Check(new WindowPlacementStore(invalidPath).Load() == leftMonitor, "a valid new position can replace backed-up damaged data");
        Console.WriteLine("Window placement smoke: PASS (configuration/native data only; no windows created)");
    }

    private static void ExpectIOException(Action action, string message)
    {
        try { action(); }
        catch (IOException) { Console.WriteLine("PASS " + message); return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
