using ExplorerDirs.App;

internal static class ViewStyleWheelSmoke
{
    public static void Run()
    {
        Check(ViewStyleWheelDispatch.GetFallbackDelta(0, false) == 0,
            "partial notches stay consumed by the app accumulator");
        Check(ViewStyleWheelDispatch.GetFallbackDelta(1, true) == 0,
            "a successfully applied complete notch is consumed by the app");
        Check(ViewStyleWheelDispatch.GetFallbackDelta(2, false) == 240,
            "a failed multi-notch switch forwards the accumulated wheel distance to Shell");
        Check(ViewStyleWheelDispatch.GetFallbackDelta(-1, false) == -120,
            "a failed reverse switch preserves the reverse wheel direction for Shell");

        Check(ViewStyleWheelCycle.Next(4, 16, 120) == ViewStyle.All[4], "wheel up from Details cycles to List");
        Check(ViewStyleWheelCycle.Next(4, 16, -120) == ViewStyle.All[6], "wheel down from Details cycles to Tiles");
        Check(ViewStyleWheelCycle.Next(1, 256, 120) == ViewStyle.All[7], "wheel up wraps from largest icons to Content");
        Check(ViewStyleWheelCycle.Next(8, 16, -120) == ViewStyle.All[0], "wheel down wraps from Content to largest icons");
        Check(ViewStyleWheelCycle.Next(1, 80, 120) == ViewStyle.All[0], "non-preset icon size maps to nearest icon style");
        Check(ViewStyleWheelCycle.Next(1, 256, 240) == ViewStyle.All[6], "two wheel notches advance two styles");
        Check(ViewStyleWheelCycle.Next(2, 16, 120) == ViewStyle.All[2], "Small Icons advances to Medium Icons");
        Check(ViewStyleWheelCycle.Next(99, 16, 120) == ViewStyle.All[4], "unknown native mode resumes from Details");

        var wheel = new ViewStyleWheelAccumulator();
        Check(wheel.Add(60) == 0 && wheel.Add(59) == 0 && wheel.Add(1) == 1,
            "sub-notch positive deltas accumulate before one view change");
        Check(wheel.Add(-60) == 0 && wheel.Add(-60) == -1,
            "sub-notch negative deltas accumulate before one view change");
        var reversing = new ViewStyleWheelAccumulator();
        Check(reversing.Add(60) == 0 && reversing.Add(-120) == 0 && reversing.Add(-60) == -1,
            "opposite partial deltas cancel before completing a reverse notch");
        var highResolution = new ViewStyleWheelAccumulator();
        Check(highResolution.Add(360) == 3 && highResolution.Add(-240) == -2,
            "whole and multi-notch messages preserve their signed notch count");
        var separateTab = new ViewStyleWheelAccumulator();
        Check(separateTab.Add(60) == 0 && new ViewStyleWheelAccumulator().Add(60) == 0 &&
            separateTab.Add(60) == 1,
            "partial wheel motion remains isolated to its owning tab");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"View-style wheel cycle failed: {message}");
    }
}
