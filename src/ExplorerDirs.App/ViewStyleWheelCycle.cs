namespace ExplorerDirs.App;

internal static class ViewStyleWheelCycle
{
    public static ViewStyle Next(uint mode, int iconSize, int delta)
    {
        var styles = ViewStyle.All;
        var current = FindIndex(mode, iconSize);
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        // Positive wheel movement advances toward earlier presets.
        var direction = Math.Sign(delta);
        if (direction == 0) direction = 1;
        var next = (current - direction * notches) % styles.Count;
        if (next < 0) next += styles.Count;
        return styles[next];
    }

    private static int FindIndex(uint mode, int iconSize)
    {
        if (mode == 1)
        {
            var iconSizes = new[] { 256, 96, 48 };
            var closest = 0;
            for (var i = 1; i < iconSizes.Length; i++)
                if (Math.Abs((long)iconSizes[i] - iconSize) < Math.Abs((long)iconSizes[closest] - iconSize))
                    closest = i;
            return closest;
        }
        for (var i = 3; i < ViewStyle.All.Count; i++)
            if (ViewStyle.All[i].Mode == mode) return i;
        return 5; // Unrecognized native modes resume from the common Details view.
    }
}
