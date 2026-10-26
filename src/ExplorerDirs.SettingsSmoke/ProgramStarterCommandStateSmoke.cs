using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterCommandStateSmoke
{
    public static void Run()
    {
        var empty = ProgramStarterCommandState.From(0, false, false, false, 0, 200, false);
        Check(empty.CanAdd && empty.CanImport && !empty.CanEdit && !empty.CanDelete && !empty.CanLaunch,
            "empty list allows add/import only");

        var selectedValid = ProgramStarterCommandState.From(1, true, true, false, 0, 199, false);
        Check(selectedValid.CanEdit && selectedValid.CanDelete && selectedValid.CanLaunch,
            "selected valid item allows edit/delete/launch");

        var selectedInvalid = ProgramStarterCommandState.From(1, true, false, false, 0, 199, false);
        Check(selectedInvalid.CanEdit && selectedInvalid.CanDelete && !selectedInvalid.CanLaunch,
            "selected invalid item can be repaired or deleted but not launched");

        var preview = ProgramStarterCommandState.From(0, false, false, true, 1, 200, false);
        Check(!preview.CanImport && preview.CanApplyImport,
            "import preview disables another import and allows applying accepted entries");
        Check(!ProgramStarterCommandState.From(0, false, false, true, 0, 200, false).CanApplyImport,
            "empty import preview cannot be applied");

        var busy = ProgramStarterCommandState.From(1, true, true, false, 0, 199, true);
        Check(!busy.CanAdd && !busy.CanEdit && !busy.CanDelete && !busy.CanImport && !busy.CanLaunch,
            "launch in progress disables conflicting commands");

        var full = ProgramStarterCommandState.From(200, false, false, false, 0, 0, false);
        Check(!full.CanAdd && !full.CanImport,
            "the 200-entry limit disables add and import when no capacity remains");
        var partialCapacity = ProgramStarterCommandState.From(198, false, false, true, 3, 2, false);
        Check(!partialCapacity.CanApplyImport,
            "an import preview larger than the remaining capacity cannot be applied");
        var fitsCapacity = ProgramStarterCommandState.From(198, false, false, true, 2, 2, false);
        Check(fitsCapacity.CanApplyImport,
            "an import preview that fits the remaining capacity can be applied");
        Console.WriteLine("Program Starter command-state smoke: PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Program Starter command state: " + message);
        Console.WriteLine("PASS " + message);
    }
}
