namespace ExplorerDirs.App;

internal readonly record struct ProgramStarterCommandState(
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool CanImport,
    bool CanApplyImport,
    bool CanLaunch)
{
    public static ProgramStarterCommandState From(
        int entryCount, bool hasSelection, bool selectedEntryValid, bool isImportPreviewOpen,
        int importableEntryCount, int remainingImportCapacity, bool isLaunching) => new(
        CanAdd: !isLaunching && entryCount < ProgramStarterStore.MaximumEntries,
        CanEdit: !isLaunching && hasSelection,
        CanDelete: !isLaunching && hasSelection,
        CanImport: !isLaunching && !isImportPreviewOpen && entryCount < ProgramStarterStore.MaximumEntries,
        CanApplyImport: !isLaunching && isImportPreviewOpen && importableEntryCount > 0 &&
            importableEntryCount <= remainingImportCapacity,
        CanLaunch: !isLaunching && entryCount > 0 && hasSelection && selectedEntryValid);
}
