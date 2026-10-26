using ExplorerDirs.App;
using System.IO;
using System.Text;

namespace ExplorerDirs.SettingsSmoke;

internal static class ViewPresetStoreSmoke
{
    public static void Run(string root)
    {
        var directory = Path.Combine(root, "view-preset-store-smoke");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ViewPresets.ini");
        var store = new ViewPresetStore(path);
        Check(store.Load().Count == 0, "a missing profile starts with no named view presets");

        var nameKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
        var typeKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);
        var preset = new ViewPreset("Work details", 4, -1,
            [new DisplayColumnState(nameKey, 320), new DisplayColumnState(typeKey, 180)]);
        var groupKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
        var current = new ViewState(3, 16, [new SortColumnState(typeKey, -1)], groupKey, false,
            [new DisplayColumnState(typeKey, 90)]);
        var applied = preset.ApplyTo(current);
        Check(applied.Mode == preset.Mode && applied.IconSize == preset.IconSize &&
              applied.DisplayColumns!.SequenceEqual(preset.DisplayColumns) &&
              applied.SortColumns.SequenceEqual(current.SortColumns) && applied.GroupKey == current.GroupKey &&
              applied.GroupAscending == current.GroupAscending,
            "applying a named preset changes only view mode, icon size, and display columns");
        store.SavePreset(preset);
        var reopened = new ViewPresetStore(path);
        var loadedPreset = reopened.Load().Single();
        Check(loadedPreset.Name == preset.Name && loadedPreset.Mode == preset.Mode &&
              loadedPreset.IconSize == preset.IconSize && loadedPreset.DisplayColumns.SequenceEqual(preset.DisplayColumns),
            "named view mode and ordered column widths round-trip through the profile INI");

        reopened.SavePreset(preset with { Name = "work details", Mode = 3, IconSize = 16 });
        Check(reopened.Presets.Count == 1 && reopened.Presets[0].Mode == 3 &&
              reopened.Presets[0].DisplayColumns.SequenceEqual(preset.DisplayColumns),
            "saving a case-insensitive matching name replaces that preset without changing its column order");
        reopened.DeletePreset("WORK DETAILS");
        Check(reopened.Presets.Count == 0 && new ViewPresetStore(path).Load().Count == 0,
            "deleting a named preset persists the removal");

        var invalidPreset = preset with
        {
            DisplayColumns = [new DisplayColumnState(nameKey, 120), new DisplayColumnState(nameKey, 200)]
        };
        var invalidRejected = false;
        try { reopened.SavePreset(invalidPreset); }
        catch (ArgumentException) { invalidRejected = true; }
        Check(invalidRejected && reopened.Presets.Count == 0,
            "duplicate Shell property keys cannot be saved as a partial or ambiguous view layout");

        File.WriteAllText(path, "[ViewPresets]\nFormatVersion=99\nItems=\n");
        var future = new ViewPresetStore(path);
        Check(future.Load().Count == 0 && future.LoadWarning is not null,
            "a future profile format is reported without applying unknown state");
        var futureWriteRejected = false;
        try { future.SavePreset(preset); }
        catch (IOException) { futureWriteRejected = true; }
        Check(futureWriteRejected && File.ReadAllText(path).Contains("FormatVersion=99", StringComparison.Ordinal),
            "a future profile format is never overwritten by this version");

        var nullColumnJson = "[{\"Name\":\"damaged\",\"Mode\":4,\"IconSize\":-1,\"DisplayColumns\":[null]}]";
        var nullColumnPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(nullColumnJson));
        File.WriteAllText(path, $"[ViewPresets]\nFormatVersion=1\nItems={nullColumnPayload}\n");
        var damaged = new ViewPresetStore(path);
        Check(damaged.Load().Count == 0 && damaged.LoadWarning is not null &&
              Directory.GetFiles(directory, "ViewPresets.ini.invalid-*.bak").Length == 1,
            "a null column in edited profile data is backed up and ignored without escaping the loader");

        File.WriteAllText(path, "[ViewPresets]\nFormatVersion=1\nItems=\n");
        var conflicted = new ViewPresetStore(path);
        conflicted.Load();
        File.AppendAllText(path, "; external edit\n");
        var conflictRejected = false;
        try { conflicted.SavePreset(preset); }
        catch (IOException) { conflictRejected = true; }
        Check(conflictRejected && File.ReadAllText(path).EndsWith("; external edit\n", StringComparison.Ordinal),
            "a concurrent profile edit is preserved rather than overwritten");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("View preset store smoke failed: " + message);
    }
}
