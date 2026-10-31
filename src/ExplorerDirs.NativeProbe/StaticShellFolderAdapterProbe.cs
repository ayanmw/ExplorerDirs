using System.Runtime.InteropServices;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Hidden one-folder prototype. It uses the native COM shim because SHCreateShellFolderView
// rejects the equivalent managed CCW; no ExplorerBrowser or user files are involved.
internal static class StaticShellFolderAdapterProbe
{
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellFolderViewId = new("37A378C0-F82D-11CE-AE65-08002B2E1262");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid ShellItem2Id = new("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private static readonly Guid ParentAndItemId = new("B3A4B685-B685-4805-99D9-5DEAD2873236");
    private static readonly Guid DataObjectHandlerId = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    private static readonly Guid ShellFolderObjectHandlerId = new("3981E224-F559-11D3-8E3A-00C04F6837D5");
    private static readonly Guid ShellViewObjectHandlerId = new("3981E226-F559-11D3-8E3A-00C04F6837D5");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenu3Id = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
    private static readonly Guid NameKeyFormat = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid SizeKeyFormat = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid DocumentSummaryFormat = new("D5CDD502-2E9C-101B-9397-08002B2CF9AE");

    internal static int RunFolderContract(bool requireIdentity = false, bool strictInterfaces = false, bool prototypeClass = false, bool wrappedPidls = false, bool shellItemParentProbe = false)
    {
        VerifySortMatrixIsolation();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        var stage = "sort-matrix-isolation";
        var cloneBehavior = "not-tested";
        var absolutePidlBehavior = "not-tested";
        nint folderPidl = 0, folder = 0, adapter = 0, nativeLibrary = 0;
        var items = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(fixture);
            WriteSortableFixtures(fixture, "ten-copy.bin");
            stage = "parse-fixture-folder";
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            stage = "bind-filesystem-folder";
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolder2Id, out folder));
            stage = "load-native-adapter";
            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var shellDetailsSize = GetShellDetailsSize(nativeLibrary);
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, wrappedPidls ? "CreateWrappedPidlShellFolderAdapter" : prototypeClass ? "CreateClassIdentityShellFolderAdapter" : strictInterfaces ? "CreateStrictShellFolderAdapter" : "CreateStaticShellFolderAdapter"));
            Check(createAdapter(folder, out adapter));
            if (requireIdentity) VerifyForwardedIdentity(adapter, strictInterfaces);
            if (wrappedPidls)
            {
                stage = "wrapped-data-object-file-operation-copy";
                var copyTargetPath = Path.Combine(fixture, "copy-target");
                Directory.CreateDirectory(copyTargetPath);
                var copySource = ParseChild(adapter, "two.bin");
                try
                {
                    VerifyWrappedDataObjectCopy(adapter, copySource, Path.Combine(fixture, "two.bin"), copyTargetPath);
                }
                finally { ILFree(copySource); }

                stage = "wrapped-folder-shell-view-object";
                Check(At<CreateViewObject>(adapter, 8)(adapter, 0, in ShellViewId, out var unopenedView));
                try
                {
                    var verifyViewFolder = Marshal.GetDelegateForFunctionPointer<VerifyViewFolder>(
                        NativeLibrary.GetExport(nativeLibrary, "VerifyStaticShellViewFolder"));
                    Check(verifyViewFolder(unopenedView, adapter, out var adapterBackedView));
                    if (adapterBackedView == 0)
                        throw new Exception("The unopened Shell view object did not retain the wrapped folder adapter.");
                }
                finally { Marshal.Release(unopenedView); }

                stage = "wrapped-nested-folder-adapter";
                var nestedFolderPath = Path.Combine(fixture, "nested-folder");
                Directory.CreateDirectory(nestedFolderPath);
                var childFolderPath = Path.Combine(nestedFolderPath, "child-folder");
                Directory.CreateDirectory(childFolderPath);
                File.WriteAllBytes(Path.Combine(childFolderPath, "two.bin"), new byte[2]);
                var nestedPidl = ParseChild(adapter, "nested-folder");
                nint nestedFolder = 0;
                try
                {
                    var bindHr = At<BindToObject>(adapter, 5)(adapter, nestedPidl, 0, in ShellFolder2Id, out nestedFolder);
                    if (bindHr < 0 || nestedFolder == 0)
                        throw new Exception($"Wrapped folder BindToObject failed: hr=0x{bindHr:X8}, folder=0x{nestedFolder:X}.");
                    var nestedHeader = Marshal.AllocCoTaskMem(shellDetailsSize);
                    try
                    {
                        var nestedAdapterColumnFound = false;
                        for (uint nestedColumn = 0; nestedColumn < 64; nestedColumn++)
                        {
                            Marshal.Copy(new byte[shellDetailsSize], 0, nestedHeader, shellDetailsSize);
                            if (At<GetDetailsOf>(nestedFolder, 18)(nestedFolder, 0, nestedColumn, nestedHeader) < 0) continue;
                            if (ReadDetailsText(nestedHeader) == "Registered schema control")
                            {
                                nestedAdapterColumnFound = true;
                                break;
                            }
                        }
                        if (!nestedAdapterColumnFound)
                            throw new Exception("A folder opened through the wrapped adapter did not retain its adapter column metadata.");
                    }
                    finally { Marshal.FreeCoTaskMem(nestedHeader); }
                    var childFolderPidl = ParseChild(nestedFolder, "child-folder");
                    nint childFolder = 0;
                    try
                    {
                        var childBindHr = At<BindToObject>(nestedFolder, 5)(nestedFolder, childFolderPidl, 0, in ShellFolder2Id, out childFolder);
                        if (childBindHr < 0 || childFolder == 0)
                            throw new Exception($"Second-level wrapped folder BindToObject failed: hr=0x{childBindHr:X8}, folder=0x{childFolder:X}.");
                        var nestedChild = ParseChild(childFolder, "two.bin");
                        try { VerifyWrappedShellItemObjects(childFolder, nestedChild); }
                        finally { ILFree(nestedChild); }
                    }
                    finally
                    {
                        if (childFolder != 0) Marshal.Release(childFolder);
                        ILFree(childFolderPidl);
                    }
                }
                finally
                {
                    if (nestedFolder != 0) Marshal.Release(nestedFolder);
                    ILFree(nestedPidl);
                }

                stage = "wrapped-enumeration";
                var rawEnumHr = At<EnumObjects>(folder, 4)(folder, 0, 0x60, out var rawEnumerator);
                if (rawEnumHr < 0) throw new Exception($"Filesystem IEnumIDList creation failed: 0x{rawEnumHr:X8}.");
                int rawCloneHr;
                try { rawCloneHr = At<EnumClone>(rawEnumerator, 6)(rawEnumerator, out var rawClone); if (rawClone != 0) Marshal.Release(rawClone); }
                finally { Marshal.Release(rawEnumerator); }

                var enumerate = At<EnumObjects>(adapter, 4);
                VerifyWrappedEnumeratorCacheRetry(enumerate, adapter, nativeLibrary);
                var enumHr = enumerate(adapter, 0, 0x60, out var enumerator);
                if (enumHr < 0) throw new Exception($"Wrapped EnumObjects failed: 0x{enumHr:X8}.");
                try
                {
                    var firstHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var firstChild, out var firstFetched);
                    if (firstHr < 0 || firstFetched != 1 || firstChild == 0)
                        throw new Exception($"Wrapped enumerator did not produce its first cursor item: hr=0x{firstHr:X8}, fetched={firstFetched}.");
                    VerifyWrappedShellItemObjects(adapter, firstChild);

                    var cloneHr = At<EnumClone>(enumerator, 6)(enumerator, out var clone);
                    if (cloneHr < 0 || clone == 0)
                        throw new Exception($"Wrapped enumerator must provide an independent clone even when the filesystem enumerator Clone returns 0x{rawCloneHr:X8}; got 0x{cloneHr:X8}.");
                    cloneBehavior = "supported";
                    try
                    {
                        for (var index = 0; index < 2; index++)
                        {
                            var sourceNextHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var sourceChild, out var sourceFetched);
                            var cloneNextHr = At<EnumNext>(clone, 3)(clone, 1, out var clonedChild, out var cloneFetched);
                            if (sourceNextHr < 0 || sourceFetched != 1 || sourceChild == 0 ||
                                cloneNextHr < 0 || cloneFetched != 1 || clonedChild == 0)
                                throw new Exception($"Wrapped enumerator clone lost cursor item {index + 1}: source=0x{sourceNextHr:X8}/{sourceFetched}, clone=0x{cloneNextHr:X8}/{cloneFetched}.");
                            try
                            {
                                if (!ILIsEqual(sourceChild, clonedChild))
                                    throw new Exception($"Wrapped enumerator clone did not preserve independent cursor position {index + 1}.");
                                VerifyWrappedShellItemObjects(adapter, clonedChild);
                            }
                            finally
                            {
                                ILFree(clonedChild);
                                ILFree(sourceChild);
                            }
                        }
                    }
                    finally { Marshal.Release(clone); }

                    var resetHr = At<EnumReset>(enumerator, 5)(enumerator);
                    if (resetHr < 0) throw new Exception($"Wrapped enumerator Reset failed: 0x{resetHr:X8}.");
                    var resetNextHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var resetChild, out var resetFetched);
                    if (resetNextHr < 0 || resetFetched != 1 || resetChild == 0)
                        throw new Exception($"Wrapped enumerator did not return to its first cursor item: hr=0x{resetNextHr:X8}, fetched={resetFetched}.");
                    try
                    {
                        if (!ILIsEqual(firstChild, resetChild))
                            throw new Exception("Wrapped enumerator Reset changed the first child identity.");
                    }
                    finally
                    {
                        ILFree(resetChild);
                        ILFree(firstChild);
                    }

                    var skipCloneHr = At<EnumClone>(enumerator, 6)(enumerator, out var skipClone);
                    if (skipCloneHr < 0 || skipClone == 0)
                        throw new Exception($"Wrapped enumerator could not clone before Skip: 0x{skipCloneHr:X8}.");
                    try
                    {
                        var sourceSkipHr = At<EnumSkip>(enumerator, 4)(enumerator, 1);
                        var cloneSkipHr = At<EnumSkip>(skipClone, 4)(skipClone, 1);
                        if (sourceSkipHr < 0 || cloneSkipHr < 0)
                            throw new Exception($"Wrapped enumerator Skip failed: source=0x{sourceSkipHr:X8}, clone=0x{cloneSkipHr:X8}.");

                        var sourceAfterSkipHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var sourceAfterSkip, out var sourceAfterSkipFetched);
                        var cloneAfterSkipHr = At<EnumNext>(skipClone, 3)(skipClone, 1, out var cloneAfterSkip, out var cloneAfterSkipFetched);
                        if (sourceAfterSkipHr < 0 || sourceAfterSkipFetched != 1 || sourceAfterSkip == 0 ||
                            cloneAfterSkipHr < 0 || cloneAfterSkipFetched != 1 || cloneAfterSkip == 0)
                        {
                            if (sourceAfterSkip != 0) ILFree(sourceAfterSkip);
                            if (cloneAfterSkip != 0) ILFree(cloneAfterSkip);
                            throw new Exception($"Wrapped enumerator lost its cursor after Skip: source=0x{sourceAfterSkipHr:X8}/{sourceAfterSkipFetched}, clone=0x{cloneAfterSkipHr:X8}/{cloneAfterSkipFetched}.");
                        }
                        try
                        {
                            if (!ILIsEqual(sourceAfterSkip, cloneAfterSkip))
                                throw new Exception("Wrapped enumerator Skip changed independent cursor positions.");
                        }
                        finally
                        {
                            ILFree(sourceAfterSkip);
                            ILFree(cloneAfterSkip);
                        }
                    }
                    finally { Marshal.Release(skipClone); }

                    var resetForBatchHr = At<EnumReset>(enumerator, 5)(enumerator);
                    if (resetForBatchHr < 0) throw new Exception($"Wrapped enumerator Reset before batch reads failed: 0x{resetForBatchHr:X8}.");
                    VerifyWrappedEnumeratorBatch(enumerator, 3, expectPartial: false);
                    VerifyWrappedEnumeratorBatch(enumerator, 100, expectPartial: true);
                    var resetAfterBatchHr = At<EnumReset>(enumerator, 5)(enumerator);
                    if (resetAfterBatchHr < 0) throw new Exception($"Wrapped enumerator Reset after batch reads failed: 0x{resetAfterBatchHr:X8}.");

                    stage = "wrapped-enumerator-next";
                    var nextHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var child, out var fetched);
                    if (nextHr < 0) throw new Exception($"Wrapped IEnumIDList.Next failed: 0x{nextHr:X8}.");
                    if (fetched != 1 || child == 0) throw new Exception("Wrapped adapter enumeration returned no child PIDL.");
                    try
                    {
                        VerifyWrappedShellItemObjects(adapter, child);
                    }
                    finally { ILFree(child); }
                }
                finally { Marshal.Release(enumerator); }
            }
            stage = "parse-and-validate-items";
            foreach (var name in new[] { "two.bin", "ten.bin", "hundred.bin", "ten-copy.bin" })
                items.Add(name, ParseChild(adapter, name));

            foreach (var name in new[] { "pending-a.bin", "pending-b.bin" })
            {
                File.WriteAllText(Path.Combine(fixture, name), "value pending");
                items.Add(name, ParseChild(adapter, name));
            }
            if (wrappedPidls)
            {
                stage = "wrapped-absolute-pidl-resolution";
                var rawAbsoluteHr = SHParseDisplayName(Path.Combine(fixture, "two.bin"), 0, out var rawAbsolute, 0, out _);
                if (rawAbsoluteHr < 0) throw new Exception($"Raw filesystem absolute PIDL setup failed: 0x{rawAbsoluteHr:X8}.");
                var combined = ILCombine(folderPidl, items["two.bin"]);
                if (combined == 0) throw new Exception("Could not combine the folder PIDL and wrapped child PIDL.");
                try
                {
                    var rawName = DescribeFileSystemPath(rawAbsolute);
                    absolutePidlBehavior = $"raw={rawName}; wrapped={DescribeFileSystemPath(combined)}";
                }
                finally
                {
                    ILFree(combined);
                    ILFree(rawAbsolute);
                }
                if (shellItemParentProbe)
                {
                    stage = "shell-item-parent-identity";
                    VerifyShellItemParentIdentity(adapter, items["two.bin"], Path.Combine(fixture, "two.bin"));
                    stage = "shell-item-folder-handlers";
                    VerifyShellItemFolderHandlers(adapter, shellDetailsSize);
                }
            }
            stage = "validate-adapter-columns";
            var columnIndex = GetColumnCount(folder, shellDetailsSize);
            stage = "custom-column-metadata";
            var property = StaticShellColumnContract.PropertyKey;
            var expectedKey = new ShellColumnManager.NativeKey { FormatId = property.FormatId, PropertyId = property.PropertyId };
            var keyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var variant = Marshal.AllocCoTaskMem(24);
            var details = Marshal.AllocCoTaskMem(shellDetailsSize);
            try
            {
                Marshal.StructureToPtr(expectedKey, keyPointer, false);
                Check(At<GetDefaultColumnState>(adapter, 16)(adapter, (uint)columnIndex, out var state));
                if (state != 0x12) throw new Exception($"Unexpected custom column state 0x{state:X}.");
                Check(At<MapColumn>(adapter, 19)(adapter, (uint)columnIndex, keyPointer));
                var mappedKey = Marshal.PtrToStructure<ShellColumnManager.NativeKey>(keyPointer);
                if (mappedKey.FormatId != expectedKey.FormatId || mappedKey.PropertyId != expectedKey.PropertyId)
                    throw new Exception("Custom column did not map to its property key.");

                foreach (var (name, expectedValue) in new[]
                {
                    ("two.bin", 2UL), ("ten.bin", 10UL), ("hundred.bin", 100UL), ("ten-copy.bin", 10UL)
                })
                {
                    stage = $"custom-column-{name}-text";
                    Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
                    Check(At<GetDetailsOf>(adapter, 18)(adapter, 0, (uint)columnIndex, details));
                    if (ReadDetailsText(details) != "Static numeric test") throw new Exception("Custom column title mismatch.");
                    Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
                    Check(At<GetDetailsOf>(adapter, 18)(adapter, items[name], (uint)columnIndex, details));
                    if (ReadDetailsText(details) != expectedValue.ToString()) throw new Exception($"Display text mismatch for {name}.");

                    stage = $"custom-column-{name}-value";
                    Marshal.GetNativeVariantForObject(0UL, variant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, items[name], keyPointer, variant));
                    var value = Marshal.GetObjectForNativeVariant(variant);
                    if (value is not ulong number || number != expectedValue)
                        throw new Exception($"Typed numeric value mismatch for {name}: {value}.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(details);
                Marshal.FreeCoTaskMem(variant);
                Marshal.FreeCoTaskMem(keyPointer);
            }

            var missingVariant = Marshal.AllocCoTaskMem(24);
            var missingKey = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var missingDetails = Marshal.AllocCoTaskMem(shellDetailsSize);
            try
            {
                Marshal.StructureToPtr(expectedKey, missingKey, false);
                Marshal.GetNativeVariantForObject(0UL, missingVariant);
                var pendingHr = At<GetDetailsEx>(adapter, 17)(adapter, items["pending-a.bin"], missingKey, missingVariant);
                if (pendingHr < 0) throw new Exception($"Pending custom GetDetailsEx failed: 0x{pendingHr:X8}.");
                if (Marshal.ReadInt16(missingVariant) != 0)
                    throw new Exception("An uncomputed custom value must be VT_EMPTY rather than a fabricated zero.");
                Marshal.Copy(new byte[shellDetailsSize], 0, missingDetails, shellDetailsSize);
                Check(At<GetDetailsOf>(adapter, 18)(adapter, items["pending-a.bin"], (uint)columnIndex, missingDetails));
                if (ReadDetailsText(missingDetails) != string.Empty)
                    throw new Exception("An uncomputed custom value must have blank display text.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(missingDetails);
                Marshal.FreeCoTaskMem(missingKey);
                Marshal.FreeCoTaskMem(missingVariant);
            }
            var sizeKey = new ShellColumnManager.NativeKey { FormatId = SizeKeyFormat, PropertyId = 12 };
            var sizeKeyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var sizeVariant = Marshal.AllocCoTaskMem(24);
            // The first details buffer was freed after the custom-column checks; use a fresh buffer for the standard Size control.
            var sizeDetails = Marshal.AllocCoTaskMem(shellDetailsSize);
            try
            {
                Marshal.StructureToPtr(sizeKey, sizeKeyPointer, false);
                foreach (var (name, expectedValue) in new[]
                {
                    ("two.bin", 2UL), ("ten.bin", 10UL), ("hundred.bin", 100UL), ("ten-copy.bin", 10UL)
                })
                {
                    Marshal.Copy(new byte[shellDetailsSize], 0, sizeDetails, shellDetailsSize);
                    Check(At<GetDetailsOf>(adapter, 18)(adapter, items[name], 1, sizeDetails));
                    if (ReadDetailsText(sizeDetails) != expectedValue.ToString())
                        throw new Exception($"Standard Size display mismatch for {name}.");

                    Marshal.GetNativeVariantForObject(0UL, sizeVariant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, items[name], sizeKeyPointer, sizeVariant));
                    var value = Marshal.GetObjectForNativeVariant(sizeVariant);
                    if (value is not ulong number || number != expectedValue)
                        throw new Exception($"Standard PKEY_Size mismatch for {name}: {value}; expected {expectedValue}.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(sizeVariant);
                Marshal.FreeCoTaskMem(sizeKeyPointer);
                Marshal.FreeCoTaskMem(sizeDetails);
            }

            VerifyRecursiveFolderSizeProjection(fixture, nativeLibrary);

            var documentColumn = columnIndex + 2;
            var documentByteCountKey = new ShellColumnManager.NativeKey { FormatId = DocumentSummaryFormat, PropertyId = 4 };
            var documentKeyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var documentVariant = Marshal.AllocCoTaskMem(24);
            var documentDetails = Marshal.AllocCoTaskMem(shellDetailsSize);
            try
            {
                Marshal.StructureToPtr(documentByteCountKey, documentKeyPointer, false);
                Check(At<GetDefaultColumnState>(adapter, 16)(adapter, (uint)documentColumn, out var state));
                if (state != 0x12) throw new Exception($"Unexpected registered-schema column state 0x{state:X}.");
                Check(At<MapColumn>(adapter, 19)(adapter, (uint)documentColumn, documentKeyPointer));
                var mappedKey = Marshal.PtrToStructure<ShellColumnManager.NativeKey>(documentKeyPointer);
                if (mappedKey.FormatId != documentByteCountKey.FormatId || mappedKey.PropertyId != documentByteCountKey.PropertyId)
                    throw new Exception("Registered-schema control column did not map to System.Document.ByteCount.");

                Marshal.Copy(new byte[shellDetailsSize], 0, documentDetails, shellDetailsSize);
                Check(At<GetDetailsOf>(adapter, 18)(adapter, 0, (uint)documentColumn, documentDetails));
                if (ReadDetailsText(documentDetails) != "Registered schema control")
                    throw new Exception("Registered-schema column title mismatch.");
                foreach (var (name, expectedValue) in new[]
                {
                    ("two.bin", 2), ("ten.bin", 10), ("hundred.bin", 100), ("ten-copy.bin", 10)
                })
                {
                    Marshal.Copy(new byte[shellDetailsSize], 0, documentDetails, shellDetailsSize);
                    Check(At<GetDetailsOf>(adapter, 18)(adapter, items[name], (uint)documentColumn, documentDetails));
                    if (ReadDetailsText(documentDetails) != expectedValue.ToString())
                        throw new Exception($"Registered-schema display value mismatch for {name}.");
                    Marshal.GetNativeVariantForObject(0, documentVariant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, items[name], documentKeyPointer, documentVariant));
                    var value = Marshal.GetObjectForNativeVariant(documentVariant);
                    if (value is not int number || number != expectedValue)
                        throw new Exception($"Registered-schema value must be Int32 for {name}: {value}.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(documentDetails);
                Marshal.FreeCoTaskMem(documentVariant);
                Marshal.FreeCoTaskMem(documentKeyPointer);
            }

            var compare = At<CompareIDs>(adapter, 7);
            stage = "numeric-comparison-contracts";
            CheckCompare(compare(adapter, 1, items["two.bin"], items["ten.bin"]), -1, "standard Size 2 before 10");
            CheckCompare(compare(adapter, 1, items["ten.bin"], items["hundred.bin"]), -1, "standard Size 10 before 100");
            CheckCompare(compare(adapter, 1, items["ten.bin"], items["ten-copy.bin"]), 0, "standard Size equal values");
            CheckCompare(compare(adapter, 1, items["hundred.bin"], items["two.bin"]), 1, "standard Size 100 after 2");
            CheckCompare(compare(adapter, columnIndex, items["two.bin"], items["ten.bin"]), -1, "2 before 10");
            CheckCompare(compare(adapter, columnIndex, items["ten.bin"], items["hundred.bin"]), -1, "10 before 100");
            CheckCompare(compare(adapter, columnIndex, items["ten.bin"], items["ten-copy.bin"]), 0, "equal values");
            CheckCompare(compare(adapter, columnIndex, items["hundred.bin"], items["two.bin"]), 1, "100 after 2");
            CheckCompare(compare(adapter, documentColumn, items["two.bin"], items["ten.bin"]), -1, "registered-schema 2 before 10");
            CheckCompare(compare(adapter, documentColumn, items["ten.bin"], items["hundred.bin"]), -1, "registered-schema 10 before 100");
            CheckCompare(compare(adapter, documentColumn, items["ten.bin"], items["ten-copy.bin"]), 0, "registered-schema equal values");
            CheckCompare(compare(adapter, documentColumn, items["hundred.bin"], items["two.bin"]), 1, "registered-schema 100 after 2");

            CheckCompare(compare(adapter, columnIndex, items["pending-a.bin"], items["two.bin"]), -1, "pending before computed");
            CheckCompare(compare(adapter, columnIndex, items["two.bin"], items["pending-a.bin"]), 1, "computed after pending");
            CheckCompare(compare(adapter, columnIndex, items["pending-a.bin"], items["pending-a.bin"]), 0, "pending self equality");
            var nativeMissingOrder = At<CompareIDs>(adapter, 7)(adapter, 0, items["pending-a.bin"], items["pending-b.bin"]);
            Check(nativeMissingOrder);
            CheckCompare(compare(adapter, columnIndex, items["pending-a.bin"], items["pending-b.bin"]),
                unchecked((short)(nativeMissingOrder & 0xffff)), "pending values retain native name order");
            var canonical = compare(adapter, 0x10000000, items["ten.bin"], items["ten-copy.bin"]);
            Check(canonical);
            if (unchecked((short)(canonical & 0xffff)) == 0)
                throw new Exception("Distinct items with equal custom numbers must retain native canonical identity.");

            if (wrappedPidls)
            {
                stage = "wrapped-rename-round-trip";
                var newName = Marshal.StringToCoTaskMemUni("pending-renamed-b.bin");
                try
                {
                    var renameHr = At<SetNameOf>(adapter, 12)(adapter, 0, items["pending-b.bin"], newName, 0, out var renamed);
                    if (renameHr < 0) throw new Exception($"Wrapped SetNameOf failed: 0x{renameHr:X8}.");
                    try
                    {
                        var unwrap = Marshal.GetDelegateForFunctionPointer<TransformPidl>(NativeLibrary.GetExport(nativeLibrary, "UnwrapStaticShellChildPidl"));
                        Check(unwrap(renamed, out var restoredName));
                        ILFree(restoredName);
                        if (!File.Exists(Path.Combine(fixture, "pending-renamed-b.bin")))
                            throw new Exception("The wrapped Shell rename did not change the fixture file name.");
                    }
                    finally { if (renamed != 0) ILFree(renamed); }
                }
                finally { Marshal.FreeCoTaskMem(newName); }

                var moveSource = Path.Combine(fixture, "ten.bin");
                var moveDestinationPath = Path.Combine(fixture, "move-target");
                Directory.CreateDirectory(moveDestinationPath);
                var moveDestinationId = ShellItemId;
                Check(SHCreateItemFromParsingName(moveDestinationPath, 0, ref moveDestinationId, out var moveDestination));
                try
                {
                    stage = "wrapped-data-object-file-operation-move";
                    var expectedMove = File.ReadAllBytes(moveSource);
                    PerformWrappedDataObjectOperation(adapter, fixture, "ten.bin", "MoveItems",
                        (operation, dataObject) => Check(At<MoveItems>(operation, 15)(operation, dataObject, moveDestination)),
                        () => !File.Exists(moveSource) && File.Exists(Path.Combine(moveDestinationPath, "ten.bin")) &&
                              File.ReadAllBytes(Path.Combine(moveDestinationPath, "ten.bin")).SequenceEqual(expectedMove));
                }
                finally { Marshal.Release(moveDestination); }

                stage = "wrapped-data-object-file-operation-rename";
                var renameSource = Path.Combine(fixture, "hundred.bin");
                var renameTarget = Path.Combine(fixture, "hundred-renamed.bin");
                var expectedRename = File.ReadAllBytes(renameSource);
                PerformWrappedDataObjectOperation(adapter, fixture, "hundred.bin", "RenameItems",
                    (operation, dataObject) => Check(At<RenameItems>(operation, 13)(operation, dataObject, "hundred-renamed.bin")),
                    () => !File.Exists(renameSource) && File.Exists(renameTarget) &&
                          File.ReadAllBytes(renameTarget).SequenceEqual(expectedRename));

                stage = "wrapped-data-object-file-operation-delete";
                var deleteSource = Path.Combine(fixture, "pending-a.bin");
                PerformWrappedDataObjectOperation(adapter, fixture, "pending-a.bin", "DeleteItems",
                    (operation, dataObject) => Check(At<DeleteItems>(operation, 19)(operation, dataObject)),
                    () => !File.Exists(deleteSource));

                var multiCopyTargetPath = Path.Combine(fixture, "multi-copy-target");
                Directory.CreateDirectory(multiCopyTargetPath);
                var multiCopyNames = new[] { "two.bin", "hundred-renamed.bin" };
                var multiCopyContents = multiCopyNames.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(fixture, name)));
                stage = "wrapped-data-object-multiselect-copy";
                var multiCopyTargetId = ShellItemId;
                Check(SHCreateItemFromParsingName(multiCopyTargetPath, 0, ref multiCopyTargetId, out var multiCopyTarget));
                try
                {
                    PerformWrappedDataObjectOperation(adapter, fixture, multiCopyNames, "CopyItems",
                        (operation, dataObject) => Check(At<CopyItems>(operation, 17)(operation, dataObject, multiCopyTarget)),
                        () => multiCopyNames.All(name => File.Exists(Path.Combine(fixture, name)) &&
                            File.Exists(Path.Combine(multiCopyTargetPath, name)) &&
                            File.ReadAllBytes(Path.Combine(multiCopyTargetPath, name)).SequenceEqual(multiCopyContents[name])));
                }
                finally { Marshal.Release(multiCopyTarget); }

                var multiMoveNames = new[] { "ten-copy.bin", "pending-renamed-b.bin" };
                var multiMoveContents = multiMoveNames.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(fixture, name)));
                var multiMoveDestinationPath = Path.Combine(fixture, "move-target");
                var multiMoveDestinationId = ShellItemId;
                Check(SHCreateItemFromParsingName(multiMoveDestinationPath, 0, ref multiMoveDestinationId, out var multiMoveDestination));
                try
                {
                    stage = "wrapped-data-object-multiselect-move";
                    PerformWrappedDataObjectOperation(adapter, fixture, multiMoveNames, "MoveItems",
                        (operation, dataObject) => Check(At<MoveItems>(operation, 15)(operation, dataObject, multiMoveDestination)),
                        () => multiMoveNames.All(name => !File.Exists(Path.Combine(fixture, name)) &&
                            File.Exists(Path.Combine(multiMoveDestinationPath, name)) &&
                            File.ReadAllBytes(Path.Combine(multiMoveDestinationPath, name)).SequenceEqual(multiMoveContents[name])));
                }
                finally { Marshal.Release(multiMoveDestination); }

                var multiDeleteNames = multiCopyNames;
                stage = "wrapped-data-object-multiselect-delete";
                PerformWrappedDataObjectOperation(adapter, fixture, multiDeleteNames, "DeleteItems",
                    (operation, dataObject) => Check(At<DeleteItems>(operation, 19)(operation, dataObject)),
                    () => multiDeleteNames.All(name => !File.Exists(Path.Combine(fixture, name))));
            }

            var customSchema = GetPropertyDescriptionStatus(new ShellColumnManager.NativeKey
                { FormatId = property.FormatId, PropertyId = property.PropertyId });
            var sizeSchema = GetPropertyDescriptionStatus(new ShellColumnManager.NativeKey
                { FormatId = SizeKeyFormat, PropertyId = 12 });
            var dateCreatedSchema = GetPropertyDescriptionStatus(new ShellColumnManager.NativeKey
                { FormatId = SizeKeyFormat, PropertyId = 15 });
            // The Windows SDK declares System.Document.ByteCount as VT_I4. Its
            // registered schema is a useful system-property control, but not a
            // suitable production size property (folder sizes exceed Int32).
            var documentByteCount = GetPropertyDescriptionEvidence(new ShellColumnManager.NativeKey
                { FormatId = DocumentSummaryFormat, PropertyId = 4 });
            var sizePropertyDescription = GetPropertyDescriptionEvidence(new ShellColumnManager.NativeKey
                { FormatId = SizeKeyFormat, PropertyId = 12 });

            Console.WriteLine($"native-static-shell-folder-contract: adapter-column={columnIndex}; wrapped-pidls={wrappedPidls}; enum-clone={cloneBehavior}; absolute-path={absolutePidlBehavior}; " +
                "state=0x12; title-and-four-values=valid; standard-PKEY_Size=UInt64/text/sort-valid; variant=UInt64; " +
                $"compare-2-10=-1; compare-10-100=-1; compare-equal=0; compare-100-2=1; registered-schema-column={documentColumn}; " +
                $"property-description(custom/size/date-created/document-byte-count)=0x{customSchema:X8}/0x{sizeSchema:X8}/0x{dateCreatedSchema:X8}/0x{documentByteCount.HResult:X8}; " +
                $"document-byte-count-type=0x{documentByteCount.VariantType:X4}(hr=0x{documentByteCount.TypeHResult:X8}); " +
                $"document-byte-count-display-type=0x{documentByteCount.DisplayType:X8}(hr=0x{documentByteCount.DisplayTypeHResult:X8}); " +
                $"document-byte-count-sort-description=0x{documentByteCount.SortDescription:X8}(hr=0x{documentByteCount.SortDescriptionHResult:X8}); type-flags=0x{documentByteCount.TypeFlags:X8}(hr=0x{documentByteCount.TypeFlagsHResult:X8}); " +
                $"document-byte-count-sort-alias=[{documentByteCount.SortAliasEvidence}]; size-type=0x{sizePropertyDescription.VariantType:X4}; size-display-type=0x{sizePropertyDescription.DisplayType:X8}; size-sort-description=0x{sizePropertyDescription.SortDescription:X8}; size-type-flags=0x{sizePropertyDescription.TypeFlags:X8}; size-sort-alias=[{sizePropertyDescription.SortAliasEvidence}]; " +
                $"column-state=0x{documentByteCount.ColumnState:X8}(hr=0x{documentByteCount.ColumnStateHResult:X8}); window-created=False");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"native-static-shell-folder-contract failure at {stage}: " + ex);
            return 1;
        }
        finally
        {
            foreach (var item in items.Values) ILFree(item);
            if (adapter != 0) Marshal.Release(adapter);
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(fixture).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
        }
    }

    internal static int Run(bool strictInterfaces = false, bool prototypeClass = false, bool activateView = false, bool wrappedPidls = false, bool legacyDetailsCallbackProbe = false)
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        nint parent = 0, folderPidl = 0, folder = 0, adapter = 0, view = 0, folderView = 0, hostPointer = 0;
        nint nativeLibrary = 0;
        var createdView = false;
        StreamViewHost? host = null;
        var result = 0;
        try
        {
            Directory.CreateDirectory(fixture);
            WriteSortableFixtures(fixture, "a-ten-copy.bin");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs static Shell-column probe",
                0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            CheckBoundary(parent, foreground, "hidden parent creation");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolder2Id, out folder));

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var shellDetailsSize = GetShellDetailsSize(nativeLibrary);
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, wrappedPidls ? "CreateWrappedPidlShellFolderAdapter" : prototypeClass ? "CreateClassIdentityShellFolderAdapter" : strictInterfaces ? "CreateStrictShellFolderAdapter" : "CreateStaticShellFolderAdapter"));
            Check(createAdapter(folder, out adapter));
            if (legacyDetailsCallbackProbe)
            {
                Marshal.GetDelegateForFunctionPointer<SetLegacyDetailsCallbackProbe>(
                    NativeLibrary.GetExport(nativeLibrary, "SetStaticShellLegacyDetailsCallbackProbe"))(1);
            }

            var property = StaticShellColumnContract.PropertyKey;
            var nativeKey = new ShellColumnManager.NativeKey { FormatId = property.FormatId, PropertyId = property.PropertyId };
            var keyPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
            var variant = Marshal.AllocCoTaskMem(24);
            try
            {
                Marshal.StructureToPtr(nativeKey, keyPointer, false);
                var details = Marshal.AllocCoTaskMem(shellDetailsSize);
                try
                {
                    Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
                    var customColumn = GetColumnCount(folder, shellDetailsSize);
                    var titleHr = At<GetDetailsOf>(adapter, 18)(adapter, 0, (uint)customColumn, details);
                    if (legacyDetailsCallbackProbe)
                    {
                        if (titleHr != unchecked((int)0x80004001)) throw new Exception($"Legacy callback A/B expected E_NOTIMPL from GetDetailsOf, got 0x{titleHr:X8}.");
                    }
                    else
                    {
                        if (titleHr < 0) throw new COMException($"Custom column title query failed at index {customColumn}.", titleHr);
                        if (ReadDetailsText(details) != "Static numeric test") throw new Exception("Custom column title mismatch.");
                    }
                }
                finally { Marshal.FreeCoTaskMem(details); }

                var columnIndex = GetColumnCount(folder, shellDetailsSize);
                Check(At<GetDefaultColumnState>(adapter, 16)(adapter, (uint)columnIndex, out var columnState));
                if (columnState != 0x12) throw new Exception($"Unexpected custom column state 0x{columnState:X}.");
                Check(At<MapColumn>(adapter, 19)(adapter, (uint)columnIndex, keyPointer));
                var mapped = Marshal.PtrToStructure<ShellColumnManager.NativeKey>(keyPointer);
                if (mapped.FormatId != nativeKey.FormatId || mapped.PropertyId != nativeKey.PropertyId)
                    throw new Exception("Custom column did not map to its property key.");

                var child = ParseChild(adapter, "two.bin");
                try
                {
                    Marshal.GetNativeVariantForObject(0UL, variant);
                    Check(At<GetDetailsEx>(adapter, 17)(adapter, child, keyPointer, variant));
                    var value = Marshal.GetObjectForNativeVariant(variant);
                    if (value is not ulong number || number != 2) throw new Exception($"Typed value mismatch: {value}.");
                }
                finally { ILFree(child); }
            }
            finally { Marshal.FreeCoTaskMem(keyPointer); Marshal.FreeCoTaskMem(variant); }

            Marshal.GetDelegateForFunctionPointer<ResetProbeCounters>(
                NativeLibrary.GetExport(nativeLibrary, "ResetStaticShellColumnProbeCounters"))();
            Check(At<CreateViewObject>(adapter, 8)(adapter, parent, in ShellViewId, out view));
            host = new StreamViewHost(parent, []) { View = view };
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var createSettings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref createSettings, hostPointer, ref bounds, out _));
            createdView = true;
            _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE on the private desktop.
            Pump(800);
            CheckBoundary(parent, foreground, "DefView creation", expectVisible: true);
            if (activateView)
            {
                var activateHr = At<ActivateView>(view, 7)(view, 2); // SVUIA_ACTIVATE_NOFOCUS; confined to the private desktop.
                Console.WriteLine($"native-static-shell-column: UIActivate(SVUIA_ACTIVATE_NOFOCUS)=0x{activateHr:X8}");
                Check(activateHr);
                Pump(500);
                CheckBoundary(parent, foreground, "DefView no-focus activation", expectVisible: true);
            }
            var verifyViewFolder = Marshal.GetDelegateForFunctionPointer<VerifyViewFolder>(
                NativeLibrary.GetExport(nativeLibrary, "VerifyStaticShellViewFolder"));
            Check(verifyViewFolder(view, adapter, out var adapterBackedView));
            Console.WriteLine($"native-static-shell-column: view-uses-adapter={adapterBackedView}");
            if (adapterBackedView == 0) throw new Exception("DefView did not retain the native Shell-folder adapter.");
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<SetMode>(folderView, 35)(folderView, 4, 16)); // Details view.

            var columnIndexForView = GetColumnCount(folder, shellDetailsSize);
            var columns = new DisplayColumnState[]
            {
                new(new(NameKeyFormat, 10), 220),
                new(new(SizeKeyFormat, 12), 110),
                new(new(property.FormatId, property.PropertyId), 150),
                new(new(SizeKeyFormat, 15), 180),
                new(new(DocumentSummaryFormat, 4), 170)
            };
            Check(ShellColumnManager.Apply(folderView, columns));
            Check(ShellColumnManager.Capture(folderView, out var captured));
            if (captured is null || !captured.SequenceEqual(columns)) throw new Exception("Custom column visibility/order did not persist.");
            var refreshHr = At<RefreshView>(view, 8)(view);
            Console.WriteLine($"native-static-shell-column: refresh-after-column-change-hr=0x{refreshHr:X8}");
            Check(refreshHr);
            Pump(500);
            // Read the actual DefView ListView subitems to distinguish a missing
            // Shell column callback from an inactive view that cannot serve any text.
            var displayedColumns = new[] { 0, 1, 2, 3, 4 }
                .Select(subItem => (SubItem: subItem, Values: ReadDisplayedColumnValues(view, subItem)))
                .ToArray();
            foreach (var column in displayedColumns)
                Console.WriteLine($"native-static-shell-column: list-view-subitem-{column.SubItem}-text=[{string.Join(',', column.Values)}]");
            if (legacyDetailsCallbackProbe)
            {
                var getCount = Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(nativeLibrary, "GetStaticShellLegacyDetailsCallbackRequestCount"));
                var getHeaders = Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(nativeLibrary, "GetStaticShellLegacyDetailsCallbackHeaderRequestCount"));
                var getItems = Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(nativeLibrary, "GetStaticShellLegacyDetailsCallbackItemRequestCount"));
                var getLastColumn = Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(nativeLibrary, "GetStaticShellLegacyDetailsCallbackLastColumn"));
                Console.WriteLine($"native-static-shell-column: legacy-SFVM_GETDETAILSOF-requests={getCount()}; headers={getHeaders()}; items={getItems()}; last-column={getLastColumn()}; callback-fallback-probe=True");
                Console.WriteLine($"native-static-shell-column: legacy-callback-fallback-observed={getCount() > 0}; custom-column-values=[{string.Join(',', displayedColumns[2].Values)}]; legacy-callback-route-usable={getCount() > 0 && displayedColumns[2].Values.Any(value => value == "callback-item")}");
                // This A/B answers only whether the legacy callback supplies custom-column text.
                // The fallback is documented as deprecated, so do not run unrelated sort gates in this mode.
                return 0;
            }
            Console.WriteLine($"native-static-shell-column: callbacks-after-display-read={ReadNativeComparisonStats()}");
            VerifyLegacyColumnRearrange(view, columnIndexForView);
            var sizeKey = new ShellColumnManager.NativeKey { FormatId = SizeKeyFormat, PropertyId = 12 };
            var documentByteCountKey = new ShellColumnManager.NativeKey { FormatId = DocumentSummaryFormat, PropertyId = 4 };
            // A failed custom-schema check must not suppress either known-schema control.
            VerifySortMatrix((keyName, direction) => ReadSortedValues(view, folderView, adapter,
                keyName switch
                {
                    "custom" => nativeKey,
                    "PKEY_Size" => sizeKey,
                    "System.Document.ByteCount" => documentByteCountKey,
                    _ => throw new ArgumentOutOfRangeException(nameof(keyName), keyName, "Unknown sort control.")
                }, direction), Console.WriteLine);
            Console.WriteLine($"native-static-shell-column: base-columns={columnIndexForView}; custom-and-known-schema-columns-visible=True; " +
                "custom-and-PKEY_Size=UInt64; System.Document.ByteCount=Int32; ascending=[2,10,10,100]; descending=[100,10,10,2]; parent-visible=True; foreground-unchanged=True");
            CheckBoundary(parent, foreground, "static column sorting", expectVisible: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("native-static-shell-column failure: " + ex);
            result = 1;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (createdView && view != 0) Check(At<DestroyViewWindow>(view, 10)(view));
            }
            catch (Exception ex) { Console.WriteLine("native-static-shell-column cleanup: view: " + ex.Message); result = 1; }
            host?.Dispose();
            if (view != 0) Marshal.Release(view);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (adapter != 0) Marshal.Release(adapter);
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent))) result = 1;
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(fixture).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixture))
            {
                try { Directory.Delete(fixture, recursive: true); }
                catch (Exception ex) { Console.WriteLine("native-static-shell-column cleanup: fixture: " + ex.Message); result = 1; }
            }
            GC.KeepAlive(host);
        }
        if (GetForegroundWindow() != foreground) { Console.WriteLine("native-static-shell-column failure: foreground changed."); result = 1; }
        return result;
    }

    internal static int RunShellItemBoundView()
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        var childPath = Path.Combine(fixture, "nested-folder");
        var copyDestinationPath = Path.Combine(fixture, "copy-destination");
        nint parent = 0, folderPidl = 0, folder = 0, adapter = 0, childPidl = 0, childFolder = 0;
        nint item = 0, view = 0, folderView = 0, viewFolder = 0, hostPointer = 0, nativeLibrary = 0;
        var createdView = false;
        StreamViewHost? host = null;
        var stage = "create-fixture";
        var result = 0;
        var propertyStoreFactoryProbeEnabled = false;
        var defaultColumnSortProbeEnabled = false;
        var sortCallbackProbeEnabled = false;
        try
        {
            Directory.CreateDirectory(childPath);
            Directory.CreateDirectory(copyDestinationPath);
            File.WriteAllBytes(Path.Combine(childPath, "two.bin"), new byte[2]);
            File.WriteAllBytes(Path.Combine(childPath, "ten.bin"), new byte[10]);
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs Shell item bound-view probe",
                0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Private Desktop parent window creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolder2Id, out folder));

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var shellDetailsSize = GetShellDetailsSize(nativeLibrary);
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, "CreateWrappedPidlShellFolderAdapter"));
            Check(createAdapter(folder, out adapter));
            Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                NativeLibrary.GetExport(nativeLibrary, "SetStaticShellPropertyStoreFactoryProbe"))(1);
            propertyStoreFactoryProbeEnabled = true;
            Marshal.GetDelegateForFunctionPointer<ResetProbeCounters>(NativeLibrary.GetExport(nativeLibrary, "ResetStaticShellColumnProbeCounters"))();

            stage = "bind-child-folder-adapter";
            childPidl = ParseChild(adapter, "nested-folder");
            Check(At<BindToObject>(adapter, 5)(adapter, childPidl, 0, in ShellFolder2Id, out childFolder));
            var itemId = ShellItemId;
            Check(SHCreateItemWithParent(0, adapter, childPidl, in itemId, out item));
            var shellFolderId = ShellFolder2Id;
            Check(At<BindShellItemHandler>(item, 3)(item, 0, in ShellFolderObjectHandlerId, in shellFolderId, out var handlerFolder));
            try
            {
                if (!HasRegisteredSchemaColumn(handlerFolder, shellDetailsSize))
                    throw new Exception("BHID_SFObject did not return the wrapped child folder.");
            }
            finally { Marshal.Release(handlerFolder); }

            stage = "bind-shell-view-object";
            var shellViewId = ShellViewId;
            Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                NativeLibrary.GetExport(nativeLibrary, "SetStaticShellSortCallbackProbe"))(1);
            sortCallbackProbeEnabled = true;
            Check(At<BindShellItemHandler>(item, 3)(item, 0, in ShellViewObjectHandlerId, in shellViewId, out view));
            host = new StreamViewHost(parent, []) { View = view };
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                NativeLibrary.GetExport(nativeLibrary, "SetDefaultColumnSortProbe"))(1);
            defaultColumnSortProbeEnabled = true;
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out _));
            createdView = true;
            Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                NativeLibrary.GetExport(nativeLibrary, "SetStaticShellSortCallbackProbe"))(0);
            sortCallbackProbeEnabled = false;
            Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                NativeLibrary.GetExport(nativeLibrary, "SetDefaultColumnSortProbe"))(0);
            defaultColumnSortProbeEnabled = false;
            _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE; the caller confines this to Private Desktop.
            Pump(700);
            CheckBoundary(parent, foreground, "Shell item-bound view creation", expectVisible: true);

            stage = "verify-view-folder-identity";
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<SetMode>(folderView, 35)(folderView, 4, 16)); // FVM_DETAILS.
            Check(At<GetFolder>(folderView, 5)(folderView, ref shellFolderId, out viewFolder));
            var defaultColumnInitialNames = ReadDisplayedColumnValues(view, 0, expectedCount: 2);
            var defaultColumnInitialSort = ReadCurrentSortColumn(folderView);
            var defaultColumnInitialStats = ReadNativeComparisonStats();
            Console.WriteLine($"native-shell-item-bound-view: default-column-sort-probe-order=[{string.Join('|', defaultColumnInitialNames)}]; sort={defaultColumnInitialSort}; callbacks={defaultColumnInitialStats}");
            nint boundUnknown = 0, viewUnknown = 0;
            var sameFolderIdentity = false;
            var viewFolderHasCustomColumn = false;
            try
            {
                Check(Marshal.QueryInterface(childFolder, in UnknownId, out boundUnknown));
                Check(Marshal.QueryInterface(viewFolder, in UnknownId, out viewUnknown));
                sameFolderIdentity = boundUnknown == viewUnknown;
                viewFolderHasCustomColumn = HasRegisteredSchemaColumn(viewFolder, shellDetailsSize);
                Console.WriteLine($"native-shell-item-bound-view: same-folder-IUnknown={sameFolderIdentity}; view-folder-custom-column={viewFolderHasCustomColumn}");
            }
            finally
            {
                if (viewUnknown != 0) Marshal.Release(viewUnknown);
                if (boundUnknown != 0) Marshal.Release(boundUnknown);
            }

            stage = "enumerate-view-items";
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var itemCount)); // SVGIO_ALLVIEW
            if (itemCount != 2) throw new Exception($"Shell view should enumerate two children, got {itemCount}.");
            var names = ReadDisplayedColumnValues(view, 0, expectedCount: 2);
            if (!names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "ten.bin", "two.bin" }, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Unexpected initial Shell view item set: " + string.Join('|', names));

            stage = "verify-adapter-background-context-menu";
            var contextMenuId = ContextMenuId;
            Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var backgroundMenu)); // SVGIO_BACKGROUND.
            try
            {
                var menu = CreatePopupMenu();
                if (menu == 0) throw new Exception("Temporary background context menu creation failed.");
                try
                {
                    var queryResult = At<QueryContextMenu>(backgroundMenu, 3)(backgroundMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                    Check(queryResult);
                    var commandCount = queryResult & 0xffff;
                    var itemCountInMenu = GetMenuItemCount(menu);
                    if (commandCount == 0 || itemCountInMenu == 0)
                        throw new Exception($"Adapter-backed background menu was empty: commands={commandCount}, items={itemCountInMenu}.");
                    var verbs = ReadContextMenuVerbs(backgroundMenu, (uint)commandCount);
                    var expectedVerbs = new[] { "refresh", "newfolder", "viewlogicaldetails" };
                    if (expectedVerbs.Any(expected => !verbs.Contains(expected, StringComparer.OrdinalIgnoreCase)))
                        throw new Exception("Adapter-backed background menu is missing native verbs: " + string.Join('|', expectedVerbs.Except(verbs, StringComparer.OrdinalIgnoreCase)));
                    Console.WriteLine($"native-shell-item-bound-view: background-context-menu=available; commands={commandCount}; menu-items={itemCountInMenu}; verbs=[{string.Join('|', expectedVerbs)}]");
                }
                finally { DestroyMenu(menu); }
            }
            finally { Marshal.Release(backgroundMenu); }

            stage = "verify-adapter-selection-context-menu";
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u | 0x40000000u));
            Check(At<GetItemObject>(view, 15)(view, 1, ref contextMenuId, out var selectionMenu)); // SVGIO_SELECTION.
            try
            {
                var menu = CreatePopupMenu();
                if (menu == 0) throw new Exception("Temporary selection context menu creation failed.");
                try
                {
                    var queryResult = At<QueryContextMenu>(selectionMenu, 3)(selectionMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                    Check(queryResult);
                    var commandCount = queryResult & 0xffff;
                    var itemCountInMenu = GetMenuItemCount(menu);
                    if (commandCount == 0 || itemCountInMenu == 0)
                        throw new Exception($"Adapter-backed selection menu was empty: commands={commandCount}, items={itemCountInMenu}.");
                    var verbs = ReadContextMenuVerbs(selectionMenu, (uint)commandCount);
                    var expectedVerbs = new[] { "copy", "delete", "rename", "properties" };
                    if (expectedVerbs.Any(expected => !verbs.Contains(expected, StringComparer.OrdinalIgnoreCase)))
                        throw new Exception("Adapter-backed selection menu is missing native verbs: " + string.Join('|', expectedVerbs.Except(verbs, StringComparer.OrdinalIgnoreCase)));
                    Console.WriteLine($"native-shell-item-bound-view: selection-context-menu=available; commands={commandCount}; menu-items={itemCountInMenu}; verbs=[{string.Join('|', expectedVerbs)}]");
                }
                finally { DestroyMenu(menu); }
            }
            finally { Marshal.Release(selectionMenu); }

            stage = "verify-selected-item-file-operation-copy";
            var dataObjectId = DataObjectId;
            Check(At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var selectedDataObject)); // SVGIO_SELECTION, IDataObject.
            try
            {
                var operationClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
                var fileOperationId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
                Check(CoCreateInstance(ref operationClassId, 0, 1, ref fileOperationId, out var fileOperation));
                try
                {
                    Check(At<SetOperationFlags>(fileOperation, 5)(fileOperation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                    var destinationItemId = ShellItemId;
                    Check(SHCreateItemFromParsingName(copyDestinationPath, 0, ref destinationItemId, out var destinationItem));
                    try { Check(At<CopyItems>(fileOperation, 17)(fileOperation, selectedDataObject, destinationItem)); }
                    finally { Marshal.Release(destinationItem); }
                    destinationItemId = ShellItemId;
                    Check(SHCreateItemFromParsingName(childPath, 0, ref destinationItemId, out var viewDestinationItem));
                    try { Check(At<CopyItems>(fileOperation, 17)(fileOperation, selectedDataObject, viewDestinationItem)); }
                    finally { Marshal.Release(viewDestinationItem); }
                    Check(At<PerformOperations>(fileOperation, 21)(fileOperation));
                    Check(At<GetAnyOperationsAborted>(fileOperation, 22)(fileOperation, out var aborted));
                    var copiedPath = Path.Combine(copyDestinationPath, "ten.bin");
                    if (aborted != 0 || !File.Exists(copiedPath) || File.ReadAllBytes(copiedPath).Length != 10 ||
                        !File.Exists(Path.Combine(childPath, "ten.bin")))
                        throw new IOException("The wrapped view selection did not copy the expected file while preserving its source.");
                    var deadline = Environment.TickCount64 + 5000;
                    var observedCount = -1;
                    while (Environment.TickCount64 < deadline)
                    {
                        Pump(100);
                        Check(At<ItemCount>(folderView, 7)(folderView, 2, out observedCount));
                        if (observedCount == 3) break;
                    }
                    if (observedCount != 3 || Directory.EnumerateFiles(childPath).Count() != 3)
                        throw new IOException($"The adapter-backed view did not receive the copy notification: view-count={observedCount}, directory-files={Directory.EnumerateFiles(childPath).Count()}.");
                    itemCount = observedCount;
                    Console.WriteLine("native-shell-item-bound-view: selection-IDataObject-IFileOperation.CopyItems=True; copied=ten.bin; source-preserved=True; view-copy-notification=2->3");

                    // The copy-notification check creates a localized duplicate name; rename both equal-size
                    // rows into a non-monotonic name/value sequence before the independent sort comparison.
                    var copiedInFolder = Directory.EnumerateFiles(childPath)
                        .Single(path => !Path.GetFileName(path).Equals("two.bin", StringComparison.OrdinalIgnoreCase) &&
                                        !Path.GetFileName(path).Equals("ten.bin", StringComparison.OrdinalIgnoreCase));
                    File.Move(Path.Combine(childPath, "ten.bin"), Path.Combine(childPath, "a-ten.bin"));
                    File.Move(copiedInFolder, Path.Combine(childPath, "z-ten.bin"));
                    InvokeShellViewBackgroundVerb(view, parent, "refresh");
                    Pump(400);
                    var sortFixtureNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
                    if (!sortFixtureNames.SequenceEqual(new[] { "a-ten.bin", "two.bin", "z-ten.bin" }, StringComparer.OrdinalIgnoreCase))
                        throw new IOException("The isolated non-monotonic sort fixture did not refresh as expected: " + string.Join('|', sortFixtureNames));
                    Console.WriteLine("native-shell-item-bound-view: non-monotonic-sort-fixture=[a-ten.bin:10|two.bin:2|z-ten.bin:10]; source-files-reset-after-copy-notification=True");
                }
                finally { Marshal.Release(fileOperation); }
            }
            finally { Marshal.Release(selectedDataObject); }
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x40000000u));

            stage = "verify-native-details-and-refresh-menu-verbs";
            Check(At<SetMode>(folderView, 35)(folderView, 1, 16)); // FVM_ICON.
            InvokeShellViewBackgroundVerb(view, parent, "viewlogicaldetails");
            Check(At<GetCurrentViewMode>(folderView, 3)(folderView, out var viewMode));
            if (viewMode != 4) throw new Exception($"The native Details menu command left view mode {viewMode}, expected FVM_DETAILS (4).");
            InvokeShellViewBackgroundVerb(view, parent, "refresh");
            Pump(500);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var refreshedItemCount));
            if (refreshedItemCount != 3) throw new Exception($"Native Refresh changed item count unexpectedly: {refreshedItemCount}.");
            Console.WriteLine("native-shell-item-bound-view: native-menu-verbs=viewlogicaldetails,refresh; details-mode-readback=True; refresh-count=3");

            stage = "verify-standard-size-sort";
            using var listViewMessageObserver = new ListViewMessageObserver(FindListViewWindow(view));
            var nameColumn = new ShellColumnManager.NativeKey { FormatId = NameKeyFormat, PropertyId = 10 };
            var sizeColumn = new ShellColumnManager.NativeKey { FormatId = SizeKeyFormat, PropertyId = 12 };
            var overriddenSizeKey = new PropertyKeyState(SizeKeyFormat, 12);
            var sizeFactoryEvidence = string.Join(";", new[] { "a-ten.bin", "two.bin", "z-ten.bin" }
                .Select(childName => $"{childName}:{ReadShellFolderPropertyStoreFactoryValue(childFolder, childName, overriddenSizeKey)}"));
            if (!sizeFactoryEvidence.Contains("a-ten.bin:BindToObject=S_OK;GetPropertyStore=S_OK;GetValue=100", StringComparison.Ordinal) ||
                !sizeFactoryEvidence.Contains("two.bin:BindToObject=S_OK;GetPropertyStore=S_OK;GetValue=200", StringComparison.Ordinal) ||
                !sizeFactoryEvidence.Contains("z-ten.bin:BindToObject=S_OK;GetPropertyStore=S_OK;GetValue=10", StringComparison.Ordinal))
                throw new Exception("The diagnostic property-store factory did not expose the expected PKEY_Size overrides: " + sizeFactoryEvidence);
            Console.WriteLine("native-shell-item-bound-view: standard-size-property-store-overrides=[" + sizeFactoryEvidence + "]");
            listViewMessageObserver.Reset();
            var sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = sizeColumn, Direction = 1 }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var standardSizeSetSortMessages = listViewMessageObserver.Snapshot();
            names = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var standardSizeSortUsedOverride = names.SequenceEqual(new[] { "z-ten.bin", "a-ten.bin", "two.bin" }, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"native-shell-item-bound-view: standard-size-sort-source={(standardSizeSortUsedOverride ? "property-store-override" : "not-property-store-override")}; ascending-order=[{string.Join('|', names)}]; override-values=[a-ten.bin:100|two.bin:200|z-ten.bin:10]; disk-sizes=[a-ten.bin:10|two.bin:2|z-ten.bin:10]");
            var resetStandardSortProbeCounters = Marshal.GetDelegateForFunctionPointer<ResetProbeCounters>(
                NativeLibrary.GetExport(nativeLibrary, "ResetStaticShellColumnProbeCounters"));
            resetStandardSortProbeCounters();
            listViewMessageObserver.Reset();
            var standardSizeHeaderClickOne = SendHeaderItemClick(view, 1);
            Pump(300);
            var standardSizeHeaderClickOneMessages = listViewMessageObserver.Snapshot();
            var standardSizeHeaderClickOneNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var standardSizeHeaderClickOneSort = ReadCurrentSortColumn(folderView);
            listViewMessageObserver.Reset();
            var standardSizeHeaderClickTwo = SendHeaderItemClick(view, 1);
            Pump(300);
            var standardSizeHeaderClickTwoMessages = listViewMessageObserver.Snapshot();
            var standardSizeHeaderClickTwoNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var standardSizeHeaderClickTwoSort = ReadCurrentSortColumn(folderView);
            Console.WriteLine($"native-shell-item-bound-view: standard-size-header-sort-orders=[one:{string.Join('|', standardSizeHeaderClickOneNames)};two:{string.Join('|', standardSizeHeaderClickTwoNames)}]; overridden-size-order-would-be=[ascending:z-ten.bin|a-ten.bin|two.bin;descending:two.bin|a-ten.bin|z-ten.bin]");
            var standardSizeHeaderClickStats = ReadNativeComparisonStats();
            Console.WriteLine($"native-shell-item-bound-view: standard-size-header-clicks={standardSizeHeaderClickOne}/{standardSizeHeaderClickTwo}; one=[{string.Join('|', standardSizeHeaderClickOneNames)}] sort={standardSizeHeaderClickOneSort} listview-messages=[{standardSizeHeaderClickOneMessages}]; two=[{string.Join('|', standardSizeHeaderClickTwoNames)}] sort={standardSizeHeaderClickTwoSort} listview-messages=[{standardSizeHeaderClickTwoMessages}]; callbacks={standardSizeHeaderClickStats}; SetSortColumns-messages=[{standardSizeSetSortMessages}]");
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = sizeColumn, Direction = 1 }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(300);

            stage = "verify-custom-column-contract";
            var property = StaticShellColumnContract.PropertyKey;
            var customColumn = new PropertyKeyState(property.FormatId, property.PropertyId);
            var knownSchemaColumn = new PropertyKeyState(DocumentSummaryFormat, 4); // System.Document.ByteCount, VT_I4.
            var resetProbeCounters = Marshal.GetDelegateForFunctionPointer<ResetProbeCounters>(
                NativeLibrary.GetExport(nativeLibrary, "ResetStaticShellColumnProbeCounters"));

            resetProbeCounters();
            Check(ShellColumnManager.Apply(folderView,
            [
                new(new(nameColumn.FormatId, nameColumn.PropertyId), 220),
                new(new(sizeColumn.FormatId, sizeColumn.PropertyId), 100),
                new(knownSchemaColumn, 130)
            ]));
            Check(ShellColumnManager.Capture(folderView, out var knownSchemaColumns));
            resetProbeCounters();
            var knownSchemaInitialOrder = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaClickOneResult = SendColumnClickNotification(view, 2);
            Pump(300);
            var knownSchemaClickOneNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaClickOneValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaClickOneStats = ReadNativeComparisonStats();
            var knownSchemaClickTwoResult = SendColumnClickNotification(view, 2);
            Pump(300);
            var knownSchemaClickTwoNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaClickTwoValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaClickTwoStats = ReadNativeComparisonStats();
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = sizeColumn, Direction = 1 }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(300);
            listViewMessageObserver.Reset();
            var knownSchemaHeaderClickOneResult = SendHeaderItemClick(view, 2);
            Pump(300);
            var knownSchemaHeaderClickOneMessages = listViewMessageObserver.Snapshot();
            var knownSchemaHeaderClickOneNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaHeaderClickOneValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaHeaderClickOneSort = ReadCurrentSortColumn(folderView);
            listViewMessageObserver.Reset();
            var knownSchemaHeaderClickTwoResult = SendHeaderItemClick(view, 2);
            Pump(300);
            var knownSchemaHeaderClickTwoMessages = listViewMessageObserver.Snapshot();
            var knownSchemaHeaderClickTwoNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaHeaderClickTwoValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaHeaderClickTwoSort = ReadCurrentSortColumn(folderView);
            resetProbeCounters();
            listViewMessageObserver.Reset();
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = sizeColumn, Direction = 1 }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(300);
            var customColumnSizeResetMessages = listViewMessageObserver.Snapshot();
            listViewMessageObserver.Reset();
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn
                {
                    Key = new ShellColumnManager.NativeKey { FormatId = DocumentSummaryFormat, PropertyId = 4 },
                    Direction = 1
                }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var knownSchemaSetSortMessages = listViewMessageObserver.Snapshot();
            var knownSchemaSortNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaSortValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaAscendingNumericOrder = knownSchemaSortValues.SequenceEqual(new[] { "2", "10", "10" }, StringComparer.Ordinal);
            Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var knownSchemaSortCount));
            var knownSchemaSortReadback = "none";
            if (knownSchemaSortCount > 0)
            {
                var sortReadbackPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>() * knownSchemaSortCount);
                try
                {
                    Check(At<GetSortColumns>(folderView, 28)(folderView, sortReadbackPointer, knownSchemaSortCount));
                    var firstSort = Marshal.PtrToStructure<NativeSortColumn>(sortReadbackPointer);
                    knownSchemaSortReadback = $"{firstSort.Key.FormatId}/{firstSort.Key.PropertyId}:{firstSort.Direction}";
                }
                finally { Marshal.FreeCoTaskMem(sortReadbackPointer); }
            }
            var knownSchemaSortStats = ReadNativeComparisonStats();
            listViewMessageObserver.Reset();
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn
                {
                    Key = new ShellColumnManager.NativeKey { FormatId = DocumentSummaryFormat, PropertyId = 4 },
                    Direction = -1
                }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var knownSchemaDescendingSetSortMessages = listViewMessageObserver.Snapshot();
            var knownSchemaDescendingNames = ReadDisplayedColumnValues(view, 0, expectedCount: 3);
            var knownSchemaDescendingValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaDescendingNumericOrder = knownSchemaDescendingValues.SequenceEqual(new[] { "10", "10", "2" }, StringComparer.Ordinal);
            var knownSchemaDirectionsReorderedRows = !knownSchemaSortNames.SequenceEqual(knownSchemaDescendingNames, StringComparer.OrdinalIgnoreCase);
            Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var knownSchemaDescendingSortCount));
            var knownSchemaDescendingSortReadback = "none";
            if (knownSchemaDescendingSortCount > 0)
            {
                var sortReadbackPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>() * knownSchemaDescendingSortCount);
                try
                {
                    Check(At<GetSortColumns>(folderView, 28)(folderView, sortReadbackPointer, knownSchemaDescendingSortCount));
                    var firstSort = Marshal.PtrToStructure<NativeSortColumn>(sortReadbackPointer);
                    knownSchemaDescendingSortReadback = $"{firstSort.Key.FormatId}/{firstSort.Key.PropertyId}:{firstSort.Direction}";
                }
                finally { Marshal.FreeCoTaskMem(sortReadbackPointer); }
            }
            var knownSchemaDescendingStats = ReadNativeComparisonStats();
            sortColumns = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = sizeColumn, Direction = 1 }, sortColumns, false);
                Check(At<SetSortColumns>(folderView, 27)(folderView, sortColumns, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumns); }
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var knownSchemaValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var knownSchemaHeaders = ReadDisplayedColumnHeaders(view);
            var knownSchemaStats = ReadNativeComparisonStats();
            if (!knownSchemaValues.SequenceEqual(new[] { "2", "10", "10" }, StringComparer.Ordinal))
                throw new Exception("The registered-schema property-store control column did not display file byte counts: " + string.Join('|', knownSchemaValues));
            var knownSchemaDirectEvidence = VerifyViewFolderDocumentByteCount(viewFolder, childFolder, shellDetailsSize, "a-ten.bin");
            resetProbeCounters();
            var shellItemStoreEvidence = ReadShellItemPropertyStoreValue(childFolder, "a-ten.bin", knownSchemaColumn);
            var shellItemStoreStats = ReadNativeComparisonStats();
            resetProbeCounters();
            var shellFolderStoreFactoryEvidence = ReadShellFolderPropertyStoreFactoryValue(childFolder, "a-ten.bin", knownSchemaColumn);
            var shellFolderStoreFactoryStats = ReadNativeComparisonStats();

            resetProbeCounters();
            Check(ShellColumnManager.Apply(folderView,
            [
                new(new(nameColumn.FormatId, nameColumn.PropertyId), 220),
                new(new(sizeColumn.FormatId, sizeColumn.PropertyId), 100),
                new(customColumn, 130)
            ]));
            Check(ShellColumnManager.Capture(folderView, out var appliedColumns));
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var customValues = ReadDisplayedColumnValues(view, 2, expectedCount: 3);
            var customHeaders = ReadDisplayedColumnHeaders(view);
            var customStats = ReadNativeComparisonStats();
            Console.WriteLine($"native-shell-item-bound-view: known-schema-key={knownSchemaColumn.FormatId}/{knownSchemaColumn.PropertyId}; known-schema-columns=[{string.Join('|', knownSchemaColumns?.Select(column => $"{column.Key.FormatId}/{column.Key.PropertyId}:{column.Width}") ?? [])}]; known-schema-headers=[{string.Join('|', knownSchemaHeaders)}]; known-schema-values=[{string.Join('|', knownSchemaValues)}]; direct-folder-values=[{knownSchemaDirectEvidence}]; shell-item-property-store=[{shellItemStoreEvidence}]; shell-item-store-callbacks={shellItemStoreStats}; shell-folder-property-store-factory=[{shellFolderStoreFactoryEvidence}]; shell-folder-factory-callbacks={shellFolderStoreFactoryStats}; synthetic-column-click-results={knownSchemaClickOneResult}/{knownSchemaClickTwoResult}; synthetic-column-click-one=[{string.Join('|', knownSchemaClickOneNames)}] values=[{string.Join('|', knownSchemaClickOneValues)}] callbacks={knownSchemaClickOneStats}; synthetic-column-click-two=[{string.Join('|', knownSchemaClickTwoNames)}] values=[{string.Join('|', knownSchemaClickTwoValues)}] callbacks={knownSchemaClickTwoStats}; header-click-results={knownSchemaHeaderClickOneResult}/{knownSchemaHeaderClickTwoResult}; header-click-one=[{string.Join('|', knownSchemaHeaderClickOneNames)}] values=[{string.Join('|', knownSchemaHeaderClickOneValues)}] sort={knownSchemaHeaderClickOneSort} listview-messages=[{knownSchemaHeaderClickOneMessages}]; header-click-two=[{string.Join('|', knownSchemaHeaderClickTwoNames)}] values=[{string.Join('|', knownSchemaHeaderClickTwoValues)}] sort={knownSchemaHeaderClickTwoSort} listview-messages=[{knownSchemaHeaderClickTwoMessages}]; size-reset-messages=[{customColumnSizeResetMessages}]; known-schema-sort-count={knownSchemaSortCount}; known-schema-sort-readback={knownSchemaSortReadback}; known-schema-sort-initial-order=[{string.Join('|', knownSchemaInitialOrder)}]; known-schema-sort-order=[{string.Join('|', knownSchemaSortNames)}] values=[{string.Join('|', knownSchemaSortValues)}] numeric-ascending={knownSchemaAscendingNumericOrder} listview-messages=[{knownSchemaSetSortMessages}]; known-schema-descending-order=[{string.Join('|', knownSchemaDescendingNames)}] values=[{string.Join('|', knownSchemaDescendingValues)}] numeric-descending={knownSchemaDescendingNumericOrder} listview-messages=[{knownSchemaDescendingSetSortMessages}]; directions-reordered-rows={knownSchemaDirectionsReorderedRows}; known-schema-descending-sort-readback={knownSchemaDescendingSortReadback}; known-schema-descending-callbacks={knownSchemaDescendingStats}");
            Console.WriteLine($"native-shell-item-bound-view: rows={itemCount}; sorted-size-order=[{string.Join('|', names)}]; columns-readback=[{string.Join('|', appliedColumns?.Select(column => $"{column.Key.FormatId}/{column.Key.PropertyId}:{column.Width}") ?? [])}]; headers=[{string.Join('|', customHeaders)}]; custom-column-count={GetColumnCount(viewFolder, shellDetailsSize)}; custom-column-values=[{string.Join('|', customValues)}]; custom-column-callbacks={customStats}; window-created=True; private-desktop-only=True");
            var operationChild = ParseChild(childFolder, "two.bin");
            try { VerifyWrappedShellItemObjects(childFolder, operationChild); }
            finally { ILFree(operationChild); }
            Console.WriteLine("native-shell-item-bound-view: Shell IDataObject CF_HDROP+CIDA formats=valid");
            if (!viewFolderHasCustomColumn ||
                !customValues.SequenceEqual(new[] { "2", "10", "10" }, StringComparer.Ordinal))
                throw new Exception("The Shell item-bound view did not display its custom column values: " + string.Join('|', customValues));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"native-shell-item-bound-view failure at {stage}: {ex}");
            result = 1;
        }
        finally
        {
            if (sortCallbackProbeEnabled && nativeLibrary != 0)
            {
                Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                    NativeLibrary.GetExport(nativeLibrary, "SetStaticShellSortCallbackProbe"))(0);
            }
            if (defaultColumnSortProbeEnabled && nativeLibrary != 0)
            {
                Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                    NativeLibrary.GetExport(nativeLibrary, "SetDefaultColumnSortProbe"))(0);
            }
            if (propertyStoreFactoryProbeEnabled && nativeLibrary != 0)
            {
                Marshal.GetDelegateForFunctionPointer<SetPropertyStoreFactoryProbe>(
                    NativeLibrary.GetExport(nativeLibrary, "SetStaticShellPropertyStoreFactoryProbe"))(0);
            }
            if (createdView && view != 0)
            {
                try { Check(At<DestroyViewWindow>(view, 10)(view)); }
                catch (Exception ex) { Console.WriteLine("native-shell-item-bound-view cleanup: DestroyViewWindow: " + ex.Message); result = 1; }
            }
            host?.Dispose();
            if (folderView != 0) Marshal.Release(folderView);
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (view != 0) Marshal.Release(view);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (item != 0) Marshal.Release(item);
            if (childFolder != 0) Marshal.Release(childFolder);
            if (childPidl != 0) ILFree(childPidl);
            if (adapter != 0) Marshal.Release(adapter);
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent))) result = 1;
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(fixture).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
            GC.KeepAlive(host);
        }
        if (GetForegroundWindow() != foreground)
        {
            Console.WriteLine("native-shell-item-bound-view failure: foreground changed.");
            result = 1;
        }
        return result;
    }

    private static void VerifyForwardedIdentity(nint adapter, bool strictInterfaces)
    {
        var parentId = new Guid("b3a4b685-b685-4805-99d9-5dead2873236");
        var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
        nint parent = 0, owner = 0, parentOwner = 0;
        try
        {
            var testedId = strictInterfaces ? ShellFolder2Id : parentId;
            Check(Marshal.QueryInterface(adapter, in testedId, out parent));
            Check(Marshal.QueryInterface(adapter, in unknownId, out owner));
            Check(Marshal.QueryInterface(parent, in unknownId, out parentOwner));
            Console.WriteLine($"native-static-shell-folder-identity: strict={strictInterfaces}; same-IUnknown={owner == parentOwner}; window-created=False");
            if (strictInterfaces)
            {
                var persistId = new Guid("1AC3D9F0-175C-11d1-95BE-00609797EA4F");
                nint persist = 0, persistOwner = 0;
                try
                {
                    Check(Marshal.QueryInterface(adapter, in persistId, out persist));
                    Check(Marshal.QueryInterface(persist, in unknownId, out persistOwner));
                    if (persistOwner != owner) throw new Exception("Wrapped persistence exposed another COM identity.");
                }
                finally
                {
                    if (persistOwner != 0) Marshal.Release(persistOwner);
                    if (persist != 0) Marshal.Release(persist);
                }                var result = Marshal.QueryInterface(adapter, in parentId, out var unsupported);
                if (unsupported != 0) Marshal.Release(unsupported);
                if (result != unchecked((int)0x80004002) || unsupported != 0)
                    throw new Exception("Strict policy exposed an unimplemented raw inner interface.");
            }            if (owner != parentOwner)
                throw new InvalidOperationException("The forwarded interface exposes a different COM identity from the adapter.");
        }
        finally
        {
            if (parentOwner != 0) Marshal.Release(parentOwner);
            if (owner != 0) Marshal.Release(owner);
            if (parent != 0) Marshal.Release(parent);
        }
    }

    private static void VerifyShellItemParentIdentity(nint adapter, nint childPidl, string expectedPath)
    {
        nint item = 0, displayName = 0, parsingName = 0, parentAndItem = 0, returnedParent = 0;
        nint parentPidl = 0, returnedChild = 0, adapterUnknown = 0, parentUnknown = 0;
        var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
        try
        {
            var itemId = ShellItemId;
            Check(SHCreateItemWithParent(0, adapter, childPidl, in itemId, out item));
            var displayNameHr = At<GetShellItemDisplayName>(item, 5)(item, 0x80058000, out displayName);
            var fileSystemPath = displayNameHr >= 0 && displayName != 0 ? Marshal.PtrToStringUni(displayName) : null;
            if (displayName != 0) { Marshal.FreeCoTaskMem(displayName); displayName = 0; }
            var parsingNameHr = At<GetShellItemDisplayName>(item, 5)(item, 0x80028000, out parsingName);
            var absoluteParsingName = parsingNameHr >= 0 && parsingName != 0 ? Marshal.PtrToStringUni(parsingName) : null;
            if (parsingName != 0) { Marshal.FreeCoTaskMem(parsingName); parsingName = 0; }
            var expectedFullPath = Path.GetFullPath(expectedPath);
            if (!string.Equals(expectedFullPath, fileSystemPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expectedFullPath, absoluteParsingName, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Parent-bound Shell item did not expose both absolute filesystem and parsing names: path='{fileSystemPath}', parsing='{absoluteParsingName}', expected='{expectedFullPath}', HRESULTs=0x{displayNameHr:X8}/0x{parsingNameHr:X8}.");

            Check(Marshal.QueryInterface(item, in ParentAndItemId, out parentAndItem));
            Check(At<GetParentAndItem>(parentAndItem, 4)(parentAndItem, out parentPidl, out returnedParent, out returnedChild));
            if (returnedParent == 0 || returnedChild == 0)
                throw new Exception("IParentAndItem returned a null parent folder or child PIDL.");

            Check(Marshal.QueryInterface(adapter, in unknownId, out adapterUnknown));
            Check(Marshal.QueryInterface(returnedParent, in unknownId, out parentUnknown));
            var sameParentIdentity = adapterUnknown == parentUnknown;
            var sameChildIdentity = ILIsEqual(childPidl, returnedChild);
            Console.WriteLine($"native-shell-item-parent-probe: create=SHCreateItemWithParent; filesystem-path={fileSystemPath}; absolute-parsing-name={absoluteParsingName}; same-parent-IUnknown={sameParentIdentity}; same-child-PIDL={sameChildIdentity}; window-created=False");
            if (!sameParentIdentity || !sameChildIdentity)
                throw new Exception("The Shell item did not preserve both the wrapped parent adapter identity and child PIDL.");

            VerifyShellItemArrayIdentity(item, adapter, childPidl);
            VerifyDataObjectShellItemArrayRoundTrip(adapter, childPidl, expectedPath);
        }
        finally
        {
            if (parentUnknown != 0) Marshal.Release(parentUnknown);
            if (adapterUnknown != 0) Marshal.Release(adapterUnknown);
            if (parentPidl != 0) ILFree(parentPidl);
            if (returnedChild != 0) ILFree(returnedChild);
            if (returnedParent != 0) Marshal.Release(returnedParent);
            if (parentAndItem != 0) Marshal.Release(parentAndItem);
            if (displayName != 0) Marshal.FreeCoTaskMem(displayName);
            if (parsingName != 0) Marshal.FreeCoTaskMem(parsingName);
            if (item != 0) Marshal.Release(item);
        }
    }

    private static void VerifyShellItemFolderHandlers(nint adapter, int shellDetailsSize)
    {
        nint childPidl = 0, item = 0, folder = 0, view = 0, folderView = 0, viewFolder = 0;
        nint folderUnknown = 0, viewFolderUnknown = 0;
        try
        {
            childPidl = ParseChild(adapter, "nested-folder");
            var itemId = ShellItemId;
            Check(SHCreateItemWithParent(0, adapter, childPidl, in itemId, out item));

            var shellFolderId = ShellFolder2Id;
            Check(At<BindShellItemHandler>(item, 3)(item, 0, in ShellFolderObjectHandlerId, in shellFolderId, out folder));
            if (folder == 0 || !HasRegisteredSchemaColumn(folder, shellDetailsSize))
                throw new Exception("BHID_SFObject did not bind to the wrapped child folder adapter.");

            var shellViewId = ShellViewId;
            Check(At<BindShellItemHandler>(item, 3)(item, 0, in ShellViewObjectHandlerId, in shellViewId, out view));
            if (view == 0) throw new Exception("BHID_SFViewObject returned a null IShellView.");
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<GetFolder>(folderView, 5)(folderView, ref shellFolderId, out viewFolder));
            Check(Marshal.QueryInterface(folder, in UnknownId, out folderUnknown));
            Check(Marshal.QueryInterface(viewFolder, in UnknownId, out viewFolderUnknown));
            if (folderUnknown != viewFolderUnknown)
                throw new Exception("The Shell view created by BHID_SFViewObject did not retain the bound child folder identity.");

            Console.WriteLine("native-shell-item-folder-handlers: BHID_SFObject=wrapped-folder-adapter; BHID_SFViewObject=IShellView-retains-same-folder; window-created=False");
        }
        finally
        {
            if (viewFolderUnknown != 0) Marshal.Release(viewFolderUnknown);
            if (folderUnknown != 0) Marshal.Release(folderUnknown);
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (folderView != 0) Marshal.Release(folderView);
            if (view != 0) Marshal.Release(view);
            if (folder != 0) Marshal.Release(folder);
            if (item != 0) Marshal.Release(item);
            if (childPidl != 0) ILFree(childPidl);
        }
    }

    private static bool HasRegisteredSchemaColumn(nint folder, int shellDetailsSize)
    {
        var details = Marshal.AllocCoTaskMem(shellDetailsSize);
        try
        {
            for (uint column = 0; column < 64; column++)
            {
                Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
                if (At<GetDetailsOf>(folder, 18)(folder, 0, column, details) >= 0 &&
                    ReadDetailsText(details) == "Registered schema control")
                    return true;
            }
            return false;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static void VerifyShellItemArrayIdentity(nint item, nint adapter, nint childPidl)
    {
        nint array = 0, selectedItem = 0, selectedParentAndItem = 0, selectedParent = 0;
        nint selectedParentUnknown = 0, adapterUnknown = 0, selectedParentPidl = 0, selectedChild = 0;
        var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
        try
        {
            var arrayId = ShellItemArrayId;
            Check(SHCreateShellItemArrayFromShellItem(item, in arrayId, out array));
            if (At<GetShellItemArrayCount>(array, 7)(array, out var count) < 0 || count != 1)
                throw new Exception($"Shell item array did not contain exactly one item: {count}.");
            Check(At<GetShellItemArrayItemAt>(array, 8)(array, 0, out selectedItem));
            Check(Marshal.QueryInterface(selectedItem, in ParentAndItemId, out selectedParentAndItem));
            Check(At<GetParentAndItem>(selectedParentAndItem, 4)(selectedParentAndItem, out selectedParentPidl, out selectedParent, out selectedChild));
            Check(Marshal.QueryInterface(selectedParent, in unknownId, out selectedParentUnknown));
            Check(Marshal.QueryInterface(adapter, in unknownId, out adapterUnknown));
            if (selectedParentUnknown != adapterUnknown || !ILIsEqual(childPidl, selectedChild))
                throw new Exception("Shell item array enumeration changed the wrapped parent or child identity.");

            Console.WriteLine("native-shell-item-array-probe: source=SHCreateShellItemArrayFromShellItem; count=1; same-parent-IUnknown=True; same-child-PIDL=True; window-created=False");
        }
        finally
        {
            if (adapterUnknown != 0) Marshal.Release(adapterUnknown);
            if (selectedParentUnknown != 0) Marshal.Release(selectedParentUnknown);
            if (selectedParentPidl != 0) ILFree(selectedParentPidl);
            if (selectedChild != 0) ILFree(selectedChild);
            if (selectedParent != 0) Marshal.Release(selectedParent);
            if (selectedParentAndItem != 0) Marshal.Release(selectedParentAndItem);
            if (selectedItem != 0) Marshal.Release(selectedItem);
            if (array != 0) Marshal.Release(array);
        }
    }

    private static void VerifyDataObjectShellItemArrayRoundTrip(nint adapter, nint childPidl, string expectedPath)
    {
        nint childArray = 0, dataObject = 0, itemArray = 0, selectedItem = 0;
        nint parentAndItem = 0, parentFolder = 0, parentPidl = 0, selectedChild = 0;
        nint parentUnknown = 0, adapterUnknown = 0, returnedDataObject = 0;
        var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
        try
        {
            childArray = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, childPidl);
            Check(At<GetUIObjectOf>(adapter, 10)(adapter, 0, 1, childArray, in DataObjectId, 0, out dataObject));
            if (dataObject == 0) throw new Exception("Wrapped GetUIObjectOf returned a null IDataObject.");
            VerifyWrappedShellItemDataObject(dataObject);

            var arrayId = ShellItemArrayId;
            Check(SHCreateShellItemArrayFromDataObject(dataObject, in arrayId, out itemArray));
            if (At<GetShellItemArrayCount>(itemArray, 7)(itemArray, out var count) < 0 || count != 1)
                throw new Exception($"IDataObject conversion did not contain exactly one item: {count}.");
            Check(At<GetShellItemArrayItemAt>(itemArray, 8)(itemArray, 0, out selectedItem));
            var displayNameHr = At<GetShellItemDisplayName>(selectedItem, 5)(selectedItem, 0x80058000, out var selectedPathPointer);
            var selectedPath = displayNameHr >= 0 && selectedPathPointer != 0 ? Marshal.PtrToStringUni(selectedPathPointer) : null;
            if (selectedPathPointer != 0) Marshal.FreeCoTaskMem(selectedPathPointer);
            if (!string.Equals(Path.GetFullPath(expectedPath), selectedPath, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"IDataObject-to-Shell-item-array conversion did not preserve the filesystem target: path='{selectedPath}', expected='{Path.GetFullPath(expectedPath)}', HRESULT=0x{displayNameHr:X8}.");
            Check(Marshal.QueryInterface(selectedItem, in ParentAndItemId, out parentAndItem));
            Check(At<GetParentAndItem>(parentAndItem, 4)(parentAndItem, out parentPidl, out parentFolder, out selectedChild));
            Check(Marshal.QueryInterface(parentFolder, in unknownId, out parentUnknown));
            Check(Marshal.QueryInterface(adapter, in unknownId, out adapterUnknown));
            var sameParent = parentUnknown == adapterUnknown;
            var sameChild = ILIsEqual(childPidl, selectedChild);

            var handlerId = DataObjectHandlerId;
            var dataObjectId = DataObjectId;
            Check(At<BindShellItemArrayHandler>(itemArray, 3)(itemArray, 0, in handlerId, in dataObjectId, out returnedDataObject));
            if (returnedDataObject == 0) throw new Exception("Supported IDataObject-created array returned a null IDataObject.");
            VerifyWrappedShellItemDataObject(returnedDataObject);
            Console.WriteLine($"native-shell-item-array-dataobject-roundtrip: count=1; filesystem-path-preserved=True; same-parent-IUnknown={sameParent}; same-child-PIDL={sameChild}; roundtrip-data-object=valid; window-created=False");
        }
        finally
        {
            if (returnedDataObject != 0) Marshal.Release(returnedDataObject);
            if (adapterUnknown != 0) Marshal.Release(adapterUnknown);
            if (parentUnknown != 0) Marshal.Release(parentUnknown);
            if (parentPidl != 0) ILFree(parentPidl);
            if (selectedChild != 0) ILFree(selectedChild);
            if (parentFolder != 0) Marshal.Release(parentFolder);
            if (parentAndItem != 0) Marshal.Release(parentAndItem);
            if (selectedItem != 0) Marshal.Release(selectedItem);
            if (itemArray != 0) Marshal.Release(itemArray);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (childArray != 0) Marshal.FreeCoTaskMem(childArray);
        }
    }

    private static void VerifySortMatrix(Func<string, int, IReadOnlyList<ulong>> read, Action<string> report)
    {
        var failures = new List<Exception>();
        foreach (var keyName in new[] { "custom", "PKEY_Size", "System.Document.ByteCount" })
        foreach (var direction in new[] { 1, -1 })
        {
            var label = $"{keyName}/{(direction == 1 ? "ascending" : "descending")}";
            try
            {
                var values = read(keyName, direction);
                var expected = direction == 1 ? new ulong[] { 2, 10, 10, 100 } : [100, 10, 10, 2];
                if (!values.SequenceEqual(expected))
                    throw new Exception("Unexpected numeric order: " + string.Join(',', values));
                report($"native-static-shell-column: sort-check={label}; passed=True");
            }
            catch (Exception ex)
            {
                report($"native-static-shell-column: sort-check={label}; passed=False; {ex.GetType().Name}: {ex.Message}");
                failures.Add(new InvalidOperationException(label, ex));
            }
        }
        if (failures.Count > 0) throw new AggregateException("Static column sort matrix did not pass.", failures);
    }

    private static void VerifySortMatrixIsolation()
    {
        var calls = new List<(string KeyName, int Direction)>();
        var reported = new List<string>();
        try
        {
            VerifySortMatrix((keyName, direction) =>
            {
                calls.Add((keyName, direction));
                if (calls.Count == 1) throw new InvalidOperationException("synthetic initial failure");
                return [];
            }, reported.Add);
            throw new Exception("A failing matrix was accepted.");
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count != 6 || reported.Count != 6 ||
                !calls.SequenceEqual(new[]
                {
                    ("custom", 1), ("custom", -1),
                    ("PKEY_Size", 1), ("PKEY_Size", -1),
                    ("System.Document.ByteCount", 1), ("System.Document.ByteCount", -1)
                }))
                throw new Exception("Sort failure skipped a later direction or standard-schema control.");
        }
        VerifySortMatrix((_, direction) => direction == 1 ? [2, 10, 10, 100] : [100, 10, 10, 2], _ => { });
    }
    private static int GetColumnCount(nint adapter, int shellDetailsSize)
    {
        var details = Marshal.AllocCoTaskMem(shellDetailsSize);
        try
        {
            var count = 0;
            for (uint index = 0; index < 64; index++)
            {
                Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
                if (At<GetDetailsOf>(adapter, 18)(adapter, 0, index, details) < 0) break;
                if (ReadDetailsText(details).Length == 0) break;
                count++;
            }
            if (count == 0 || count == 64) throw new Exception("Could not bound native Shell columns.");
            return count;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static void WriteSortableFixtures(string fixture, string tenByteAlias)
    {
        File.WriteAllBytes(Path.Combine(fixture, "two.bin"), new byte[2]);
        File.WriteAllBytes(Path.Combine(fixture, "ten.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(fixture, "hundred.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(fixture, tenByteAlias), new byte[10]);
    }

    private static int GetShellDetailsSize(nint module)
    {
        // SHELLDETAILS embeds STRRET, whose x64 alignment makes the full structure larger than 272 bytes.
        var size = Marshal.GetDelegateForFunctionPointer<GetShellDetailsSizeNative>(
            NativeLibrary.GetExport(module, "GetStaticShellDetailsSize"))();
        if (size < 272 || size > 512) throw new Exception($"Unexpected native SHELLDETAILS size: {size}.");
        return checked((int)size);
    }

    private static ulong[] ReadSortedValues(nint view, nint folderView, nint adapter, ShellColumnManager.NativeKey key, int direction)
    {
        Console.WriteLine($"native-static-shell-column: callbacks-before-sort={ReadNativeComparisonStats()}");
        var sortPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
        var setSortHr = 0;
        try
        {
            Marshal.StructureToPtr(new NativeSortColumn { Key = key, Direction = direction }, sortPointer, false);
            setSortHr = At<SetSortColumns>(folderView, 27)(folderView, sortPointer, 1);
            Check(setSortHr);
        }
        finally { Marshal.FreeCoTaskMem(sortPointer); }
        // SetSortColumns updates view state; explicitly request the DefView refresh before checking its item order.
        var refreshHr = At<RefreshView>(view, 8)(view);
        Console.WriteLine($"native-static-shell-column: refresh-after-sort-hr=0x{refreshHr:X8}");
        Check(refreshHr);
        Pump(300);
        var sortCountHr = At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCount);
        Console.WriteLine($"native-static-shell-column: SetSortColumns hr=0x{setSortHr:X8}; GetSortColumnCount hr=0x{sortCountHr:X8}; count={sortCount}");
        if (sortCountHr >= 0 && sortCount > 0)
        {
            var currentSort = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>() * sortCount);
            try
            {
                var readSort = At<GetSortColumns>(folderView, 28)(folderView, currentSort, sortCount);
                if (readSort >= 0)
                {
                    var value = Marshal.PtrToStructure<NativeSortColumn>(currentSort);
                    Console.WriteLine($"native-static-shell-column: sort-readback={value.Key.FormatId}/{value.Key.PropertyId}; direction={value.Direction}");
                }
            }
            finally { Marshal.FreeCoTaskMem(currentSort); }
        }
        Console.WriteLine($"native-static-shell-column: callbacks-after-sort-before-probe-read={ReadNativeComparisonStats()}");
        var names = ReadDisplayedColumnValues(view, 0);
        Console.WriteLine($"native-static-shell-column: visible-name-order=[{string.Join(',', names)}]");
        var valuesByName = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["a-ten-copy.bin"] = 10,
            ["hundred.bin"] = 100,
            ["ten.bin"] = 10,
            ["two.bin"] = 2
        };
        if (names.Length != valuesByName.Count || names.Any(name => !valuesByName.ContainsKey(name)))
            throw new Exception($"Unexpected visible fixture rows: [{string.Join(',', names)}].");
        return names.Select(name => valuesByName[name]).ToArray();
    }

    private static void VerifyLegacyColumnRearrange(nint view, int customColumnIndex)
    {
        Check(Marshal.QueryInterface(view, in ShellFolderViewId, out var shellFolderView));
        try
        {
            foreach (var candidateIndex in new[] { customColumnIndex, 2 })
            {
                var before = ReadNativeComparisonStats();
                var hr = At<Rearrange>(shellFolderView, 3)(shellFolderView, candidateIndex);
                Check(hr);
                Pump(300);
                var names = ReadDisplayedColumnValues(view, 0);
                var after = ReadNativeComparisonStats();
                Console.WriteLine($"native-static-shell-column: legacy-Rearrange hr=0x{hr:X8}; " +
                    $"candidate-index={candidateIndex}; visible-name-order=[{string.Join(',', names)}]; " +
                    $"callbacks-before=[{before}]; callbacks-after=[{after}]");
            }
        }
        finally { Marshal.Release(shellFolderView); }
    }

    private static string[] ReadDisplayedColumnValues(nint view, int subItem, int expectedCount = 4)
    {
        var listView = FindListViewWindow(view);

        var count = unchecked((int)SendMessageW(listView, 0x1004, 0, 0)); // LVM_GETITEMCOUNT
        if (count != expectedCount) throw new InvalidOperationException($"Expected {expectedCount} displayed rows, got {count}.");
        var textBuffer = Marshal.AllocHGlobal(512 * sizeof(char));
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            var values = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(textBuffer, 0);
                var item = new NativeListViewItem
                {
                    Item = index,
                    SubItem = subItem,
                    Text = textBuffer,
                    TextCapacity = 512
                };
                Marshal.StructureToPtr(item, itemPointer, false);
                var length = unchecked((int)SendMessageW(listView, 0x1073, (nint)index, itemPointer)); // LVM_GETITEMTEXTW
                if (length < 0 || length >= 512)
                    throw new InvalidOperationException($"ListView returned invalid text length {length} for row {index}.");
                values[index] = Marshal.PtrToStringUni(textBuffer, length) ?? string.Empty;
            }
            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(itemPointer);
            Marshal.FreeHGlobal(textBuffer);
        }
    }

    private static nint FindListViewWindow(nint view)
    {
        Check(At<GetViewWindow>(view, 3)(view, out var viewWindow));
        nint listView = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            var className = new System.Text.StringBuilder(32);
            if (GetClassNameW(child, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                listView = child;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("DefView has no SysListView32 child to observe.");
        return listView;
    }

    private sealed class ListViewMessageObserver : IDisposable
    {
        private const nuint SubclassId = 0xED120129;
        private readonly nint _window;
        private readonly SubclassProcedure _procedure;
        private readonly List<uint> _messages = [];
        private bool _disposed;

        public ListViewMessageObserver(nint window)
        {
            _window = window;
            _procedure = Observe;
            if (!SetWindowSubclass(_window, _procedure, SubclassId, 0))
                throw new InvalidOperationException("Could not install the isolated ListView message observer.");
        }

        public void Reset() => _messages.Clear();

        public string Snapshot() => string.Join(",", _messages.Select(message => $"0x{message:X4}"));

        private nint Observe(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
        {
            if (window == _window && message is >= 0x1000 and <= 0x10FF)
                _messages.Add(message);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (IsWindow(_window)) _ = RemoveWindowSubclass(_window, _procedure, SubclassId);
            GC.KeepAlive(_procedure);
        }
    }

    private static string[] ReadDisplayedColumnHeaders(nint view)
    {
        Check(At<GetViewWindow>(view, 3)(view, out var viewWindow));
        nint listView = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            var className = new System.Text.StringBuilder(32);
            if (GetClassNameW(child, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                listView = child;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("DefView has no SysListView32 child to query headers.");

        var header = unchecked((nint)SendMessageW(listView, 0x101F, 0, 0));
        if (header == 0) throw new InvalidOperationException("DefView ListView has no header control.");
        var columnCount = unchecked((int)SendMessageW(header, 0x1200, 0, 0)); // HDM_GETITEMCOUNT.
        var textBuffer = Marshal.AllocHGlobal(512 * sizeof(char));
        var columnPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewColumn>());
        try
        {
            var headers = new string[columnCount];
            for (var index = 0; index < columnCount; index++)
            {
                Marshal.WriteInt16(textBuffer, 0);
                var column = new NativeListViewColumn
                {
                    Mask = 0x0004, // LVCF_TEXT.
                    Text = textBuffer,
                    TextCapacity = 512
                };
                Marshal.StructureToPtr(column, columnPointer, false);
                if (SendMessageW(listView, 0x105F, (nint)index, columnPointer) == 0)
                    throw new InvalidOperationException($"Could not read DefView ListView header at index {index}.");
                headers[index] = Marshal.PtrToStringUni(textBuffer) ?? string.Empty;
            }
            return headers;
        }
        finally
        {
            Marshal.FreeHGlobal(columnPointer);
            Marshal.FreeHGlobal(textBuffer);
        }
    }

    private static string SendColumnClickNotification(nint view, int subItem)
    {
        Check(At<GetViewWindow>(view, 3)(view, out var viewWindow));
        nint listView = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            var className = new System.Text.StringBuilder(32);
            if (GetClassNameW(child, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                listView = child;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("DefView has no SysListView32 child to send a column-click notification.");

        var parent = GetParentWindow(listView);
        if (parent == 0) throw new InvalidOperationException("DefView ListView has no notification parent.");
        var controlId = GetDlgCtrlID(listView);
        var notification = new NativeListViewNotification
        {
            Header = new NativeNotificationHeader { Window = listView, Id = unchecked((nuint)controlId), Code = -108 }, // LVN_COLUMNCLICK.
            Item = -1,
            SubItem = subItem
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewNotification>());
        try
        {
            Marshal.StructureToPtr(notification, pointer, false);
            var result = SendMessageW(parent, 0x004E, unchecked((nint)controlId), pointer); // WM_NOTIFY.
            return $"parent=0x{parent:X};subitem={subItem};result=0x{result:X}";
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static string SendHeaderItemClick(nint view, int itemIndex)
    {
        Check(At<GetViewWindow>(view, 3)(view, out var viewWindow));
        nint listView = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            var className = new System.Text.StringBuilder(32);
            if (GetClassNameW(child, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                listView = child;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("DefView has no SysListView32 child for a native header click.");
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER.
        if (header == 0) throw new InvalidOperationException("DefView ListView has no native header control.");

        var rectPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            if (SendMessageW(header, 0x1207, (nint)itemIndex, rectPointer) == 0) // HDM_GETITEMRECT.
                throw new InvalidOperationException($"Could not get native header item rectangle {itemIndex}.");
            var rect = Marshal.PtrToStructure<NativeRect>(rectPointer);
            var x = (rect.Left + rect.Right) / 2;
            var y = (rect.Top + rect.Bottom) / 2;
            var coordinates = unchecked((nint)((y << 16) | (x & 0xffff)));
            SendMessageW(header, 0x0200, 0, coordinates); // WM_MOUSEMOVE.
            SendMessageW(header, 0x0201, 1, coordinates); // WM_LBUTTONDOWN.
            var result = SendMessageW(header, 0x0202, 0, coordinates); // WM_LBUTTONUP.
            return $"header=0x{header:X};item={itemIndex};xy={x},{y};up-result=0x{result:X}";
        }
        finally { Marshal.FreeHGlobal(rectPointer); }
    }

    private static string ReadCurrentSortColumn(nint folderView)
    {
        Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var count));
        if (count <= 0) return "none";
        var pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>() * count);
        try
        {
            Check(At<GetSortColumns>(folderView, 28)(folderView, pointer, count));
            var first = Marshal.PtrToStructure<NativeSortColumn>(pointer);
            return $"{first.Key.FormatId}/{first.Key.PropertyId}:{first.Direction}";
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static nint ParseChild(nint adapter, string name)
    {
        var text = Marshal.StringToCoTaskMemUni(name);
        try { Check(At<ParseDisplayName>(adapter, 3)(adapter, 0, 0, text, 0, out var pidl, out _)); return pidl; }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private static string DescribeFileSystemPath(nint pidl)
    {
        var hr = SHGetNameFromIDList(pidl, 0x80058000, out var text); // SIGDN_FILESYSPATH
        if (hr < 0) return $"unresolved-0x{hr:X8}";
        try { return Marshal.PtrToStringUni(text) ?? "empty"; }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private static void VerifyWrappedEnumeratorBatch(nint enumerator, uint requested, bool expectPartial)
    {
        var buffer = Marshal.AllocCoTaskMem(checked((int)requested * IntPtr.Size));
        try
        {
            for (var index = 0; index < requested; index++) Marshal.WriteIntPtr(buffer, checked((int)index * IntPtr.Size), 0);
            var hr = At<EnumNextMany>(enumerator, 3)(enumerator, requested, buffer, out var fetched);
            if (fetched > requested || (expectPartial ? hr != 1 || fetched == 0 : hr != 0 || fetched != requested))
                throw new Exception($"Wrapped IEnumIDList.Next({requested}) returned unexpected batch semantics: hr=0x{hr:X8}, fetched={fetched}.");

            for (var index = 0; index < fetched; index++)
            {
                var item = Marshal.ReadIntPtr(buffer, checked((int)index * IntPtr.Size));
                if (item == 0) throw new Exception($"Wrapped IEnumIDList.Next({requested}) returned a null item at index {index}.");
                ILFree(item);
                Marshal.WriteIntPtr(buffer, checked((int)index * IntPtr.Size), 0);
            }
        }
        finally
        {
            for (var index = 0; index < requested; index++)
            {
                var item = Marshal.ReadIntPtr(buffer, checked((int)index * IntPtr.Size));
                if (item != 0) ILFree(item);
            }
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static void VerifyWrappedEnumeratorCacheRetry(EnumObjects enumerate, nint adapter, nint nativeLibrary)
    {
        nint baselineEnumerator = 0, retryEnumerator = 0, baselineChild = 0, retryChild = 0;
        try
        {
            Check(enumerate(adapter, 0, 0x60, out baselineEnumerator));
            var baselineHr = At<EnumNext>(baselineEnumerator, 3)(baselineEnumerator, 1, out baselineChild, out var baselineFetched);
            if (baselineHr < 0 || baselineFetched != 1 || baselineChild == 0)
                throw new Exception($"Could not capture baseline wrapped PIDL before cache failure injection: hr=0x{baselineHr:X8}, fetched={baselineFetched}.");

            Check(enumerate(adapter, 0, 0x60, out retryEnumerator));
            Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(nativeLibrary, "FailNextWrappedEnumeratorCacheInsertForProbe"))();
            var injectedHr = At<EnumNext>(retryEnumerator, 3)(retryEnumerator, 1, out var injectedChild, out var injectedFetched);
            if (injectedChild != 0) ILFree(injectedChild);
            if (injectedHr != unchecked((int)0x8007000E) || injectedFetched != 0)
                throw new Exception($"Wrapped enumerator cache failure injection did not fail before delivering an item: hr=0x{injectedHr:X8}, fetched={injectedFetched}.");

            var retryHr = At<EnumNext>(retryEnumerator, 3)(retryEnumerator, 1, out retryChild, out var retryFetched);
            if (retryHr < 0 || retryFetched != 1 || retryChild == 0 || !ILIsEqual(baselineChild, retryChild))
                throw new Exception($"Wrapped enumerator lost or changed the child after a cache insertion failure: hr=0x{retryHr:X8}, fetched={retryFetched}.");
            Console.WriteLine("wrapped enumerator allocation retry: first child preserved after injected cache insertion failure");
        }
        finally
        {
            if (retryChild != 0) ILFree(retryChild);
            if (baselineChild != 0) ILFree(baselineChild);
            if (retryEnumerator != 0) Marshal.Release(retryEnumerator);
            if (baselineEnumerator != 0) Marshal.Release(baselineEnumerator);
        }
    }

    private static void VerifyWrappedDataObjectCopy(nint adapter, nint child, string sourcePath, string destinationPath)
    {
        var foreground = GetForegroundWindow();
        nint childArray = 0, dataObject = 0, operation = 0, destination = 0;
        try
        {
            childArray = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, child);
            Check(At<GetUIObjectOf>(adapter, 10)(adapter, 0, 1, childArray, in DataObjectId, 0, out dataObject));
            if (dataObject == 0) throw new Exception("Wrapped file selection did not return IDataObject for IFileOperation.");

            var operationClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var fileOperationId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Check(CoCreateInstance(ref operationClassId, 0, 1, ref fileOperationId, out operation));
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            var itemId = ShellItemId;
            Check(SHCreateItemFromParsingName(destinationPath, 0, ref itemId, out destination));
            Check(At<CopyItems>(operation, 17)(operation, dataObject, destination));
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));

            var copiedPath = Path.Combine(destinationPath, Path.GetFileName(sourcePath));
            if (aborted != 0 || !File.Exists(sourcePath) || !File.Exists(copiedPath) ||
                !File.ReadAllBytes(sourcePath).SequenceEqual(File.ReadAllBytes(copiedPath)))
                throw new IOException("IFileOperation did not copy the wrapped IDataObject target without altering the source.");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("Wrapped IDataObject IFileOperation changed the foreground window.");
            Console.WriteLine("wrapped IDataObject IFileOperation.CopyItems: source-preserved=True; target-content-matched=True; foreground-unchanged=True; view-window-created=False");
        }
        finally
        {
            if (destination != 0) Marshal.Release(destination);
            if (operation != 0) Marshal.Release(operation);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (childArray != 0) Marshal.FreeCoTaskMem(childArray);
        }
    }

    private static void PerformWrappedDataObjectOperation(nint adapter, string fixturePath, string childName, string operationName,
        Action<nint, nint> queueOperation, Func<bool> verifyResult)
        => PerformWrappedDataObjectOperation(adapter, fixturePath, new[] { childName }, operationName, queueOperation, verifyResult);

    private static void PerformWrappedDataObjectOperation(nint adapter, string fixturePath, IReadOnlyList<string> childNames, string operationName,
        Action<nint, nint> queueOperation, Func<bool> verifyResult)
    {
        var foreground = GetForegroundWindow();
        nint childArray = 0, dataObject = 0, operation = 0;
        var children = new List<nint>(childNames.Count);
        try
        {
            if (childNames.Count == 0) throw new ArgumentException("At least one wrapped child is required.", nameof(childNames));
            foreach (var childName in childNames) children.Add(ParseChild(adapter, childName));
            childArray = Marshal.AllocCoTaskMem(IntPtr.Size * children.Count);
            for (var index = 0; index < children.Count; index++) Marshal.WriteIntPtr(childArray, index * IntPtr.Size, children[index]);
            Check(At<GetUIObjectOf>(adapter, 10)(adapter, 0, (uint)children.Count, childArray, in DataObjectId, 0, out dataObject));
            if (dataObject == 0) throw new Exception($"Wrapped selection [{string.Join(", ", childNames)}] did not return IDataObject for {operationName}.");
            if (children.Count > 1)
                VerifyWrappedMultiSelectionContextMenu(adapter, childArray, (uint)children.Count);
            VerifyWrappedShellItemDataObject(dataObject, childNames.Select(name => Path.Combine(fixturePath, name)).ToArray());

            var operationClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var fileOperationId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Check(CoCreateInstance(ref operationClassId, 0, 1, ref fileOperationId, out operation));
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            queueOperation(operation, dataObject);
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || !verifyResult())
                throw new IOException($"IFileOperation.{operationName} did not produce the expected isolated filesystem result.");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException($"Wrapped IDataObject IFileOperation.{operationName} changed the foreground window.");
            Console.WriteLine($"wrapped IDataObject IFileOperation.{operationName}: filesystem-result=matched; foreground-unchanged=True");
        }
        finally
        {
            if (operation != 0) Marshal.Release(operation);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (childArray != 0) Marshal.FreeCoTaskMem(childArray);
            foreach (var child in children) ILFree(child);
        }
    }

    private static void VerifyWrappedShellItemObjects(nint adapter, nint child)
    {
        var childArray = Marshal.AllocCoTaskMem(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(childArray, child);
            uint attributes = 0x40000000; // SFGAO_FILESYSTEM; request one ordinary filesystem property only.
            var attributesHr = At<GetAttributesOf>(adapter, 9)(adapter, 1, childArray, ref attributes);
            if (attributesHr < 0) throw new Exception($"Wrapped GetAttributesOf failed: 0x{attributesHr:X8}.");

            var dataObjectHr = At<GetUIObjectOf>(adapter, 10)(adapter, 0, 1, childArray, in DataObjectId, 0, out var dataObject);
            if (dataObjectHr < 0) throw new Exception($"Wrapped GetUIObjectOf(IDataObject) failed: 0x{dataObjectHr:X8}.");
            if (dataObject == 0) throw new Exception("Wrapped GetUIObjectOf(IDataObject) returned a null interface.");
            try
            {
                VerifyWrappedShellItemDataObject(dataObject);
            }
            finally { Marshal.Release(dataObject); }
        }
        finally { Marshal.FreeCoTaskMem(childArray); }
    }

    private static string[] ReadContextMenuVerbs(nint contextMenu, uint commandCount)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            var getCommandString = At<GetCommandString>(contextMenu, 5);
            var verbs = new List<string>();
            for (uint offset = 0; offset < commandCount; offset++)
            {
                Marshal.WriteInt16(buffer, 0);
                if (getCommandString(contextMenu, offset, 4, 0, buffer, 512) != 0) continue; // GCS_VERBW.
                var verb = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrWhiteSpace(verb)) verbs.Add(verb);
            }
            return verbs.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string VerifyViewFolderDocumentByteCount(nint viewFolder, nint childFolder, int shellDetailsSize, string fileName)
    {
        var child = ParseChild(childFolder, fileName);
        var key = Marshal.AllocCoTaskMem(Marshal.SizeOf<ShellColumnManager.NativeKey>());
        var value = Marshal.AllocCoTaskMem(24);
        var details = Marshal.AllocCoTaskMem(shellDetailsSize);
        try
        {
            Marshal.StructureToPtr(new ShellColumnManager.NativeKey { FormatId = DocumentSummaryFormat, PropertyId = 4 }, key, false);
            Marshal.GetNativeVariantForObject(0, value);
            Check(At<GetDetailsEx>(viewFolder, 17)(viewFolder, child, key, value));
            var propertyValue = Marshal.GetObjectForNativeVariant(value);
            Marshal.Copy(new byte[shellDetailsSize], 0, details, shellDetailsSize);
            var column = GetColumnCount(viewFolder, shellDetailsSize) - 1;
            Check(At<GetDetailsOf>(viewFolder, 18)(viewFolder, child, (uint)column, details));
            var detailsValue = ReadDetailsText(details);
            if (propertyValue is not int number || number != 10 || detailsValue != "10")
                throw new Exception($"The view folder did not directly return its registered-schema values: property={propertyValue}, details={detailsValue}.");
            return $"System.Document.ByteCount({fileName})={number};GetDetailsOf={detailsValue}";
        }
        finally
        {
            ILFree(child);
            Marshal.FreeCoTaskMem(details);
            Marshal.FreeCoTaskMem(value);
            Marshal.FreeCoTaskMem(key);
        }
    }

    private static string ReadShellItemPropertyStoreValue(nint folder, string childName, PropertyKeyState propertyKey)
    {
        var child = ParseChild(folder, childName);
        nint item = 0, item2 = 0, store = 0;
        var value = Marshal.AllocCoTaskMem(24);
        try
        {
            var shellItemId = ShellItemId;
            Check(SHCreateItemWithParent(0, folder, child, in shellItemId, out item));
            Check(Marshal.QueryInterface(item, in ShellItem2Id, out item2));
            var propertyStoreId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var getPropertyStore = At<GetShellItemPropertyStore>(item2, 8);
            var storeResult = getPropertyStore(item2, 0, in propertyStoreId, out store); // GPS_DEFAULT.
            if (storeResult < 0 || store == 0) return $"GetPropertyStore=0x{storeResult:X8}";
            try
            {
                var key = new ShellColumnManager.NativeKey { FormatId = propertyKey.FormatId, PropertyId = propertyKey.PropertyId };
                Marshal.GetNativeVariantForObject(0, value);
                var valueResult = At<GetPropertyStoreValue>(store, 5)(store, in key, value);
                if (valueResult < 0) return $"GetValue=0x{valueResult:X8}";
                var propertyValue = Marshal.GetObjectForNativeVariant(value);
                return $"GetPropertyStore=S_OK;GetValue={propertyValue ?? "VT_EMPTY"}";
            }
            finally { Marshal.Release(store); }
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
            if (item2 != 0) Marshal.Release(item2);
            if (item != 0) Marshal.Release(item);
            ILFree(child);
        }
    }

    private static string ReadShellFolderPropertyStoreFactoryValue(nint folder, string childName, PropertyKeyState propertyKey)
    {
        var child = ParseChild(folder, childName);
        nint factory = 0, store = 0;
        var value = Marshal.AllocCoTaskMem(24);
        try
        {
            var factoryId = new Guid("BC110B6D-57E8-4148-A9C6-91015AB2F3A5");
            var bindResult = At<BindFolderChild>(folder, 5)(folder, child, 0, in factoryId, out factory);
            if (bindResult < 0 || factory == 0) return $"BindToObject(IPropertyStoreFactory)=0x{bindResult:X8}";

            var propertyStoreId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var getPropertyStore = At<GetFactoryPropertyStore>(factory, 3);
            var storeResult = getPropertyStore(factory, 0, 0, in propertyStoreId, out store); // GPS_DEFAULT.
            if (storeResult < 0 || store == 0) return $"GetPropertyStore=0x{storeResult:X8}";
            try
            {
                var key = new ShellColumnManager.NativeKey { FormatId = propertyKey.FormatId, PropertyId = propertyKey.PropertyId };
                Marshal.GetNativeVariantForObject(0, value);
                var valueResult = At<GetPropertyStoreValue>(store, 5)(store, in key, value);
                if (valueResult < 0) return $"GetValue=0x{valueResult:X8}";
                var propertyValue = Marshal.GetObjectForNativeVariant(value);
                return $"BindToObject=S_OK;GetPropertyStore=S_OK;GetValue={propertyValue ?? "VT_EMPTY"}";
            }
            finally { Marshal.Release(store); }
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
            if (factory != 0) Marshal.Release(factory);
            ILFree(child);
        }
    }

    private static void InvokeShellViewBackgroundVerb(nint view, nint owner, string expectedVerb)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND.
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Temporary Shell command menu creation failed.");
            try
            {
                var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryResult);
                var commandCount = Math.Min(queryResult & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(1024);
                uint? commandOffset = null;
                try
                {
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 512) == 0 &&
                            string.Equals(Marshal.PtrToStringUni(buffer), expectedVerb, StringComparison.OrdinalIgnoreCase))
                        {
                            commandOffset = offset;
                            break;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
                if (commandOffset is null) throw new Exception($"Native Shell background menu did not expose '{expectedVerb}'.");
                var invoke = new NativeInvokeInfo
                {
                    Size = Marshal.SizeOf<NativeInvokeInfo>(),
                    Window = owner,
                    Verb = (nint)commandOffset.Value,
                    Show = 1
                };
                Check(At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke));
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static void VerifyRecursiveFolderSizeProjection(string fixture, nint nativeLibrary)
    {
        var shellFixture = Path.Combine(Path.GetDirectoryName(fixture)!, "recursive-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shellFixture);
        Directory.CreateDirectory(Path.Combine(shellFixture, "child", "nested-folder"));
        File.WriteAllBytes(Path.Combine(shellFixture, "root.txt"), "root"u8.ToArray());
        File.WriteAllBytes(Path.Combine(shellFixture, "second.txt"), "second"u8.ToArray());
        var hiddenFile = Path.Combine(shellFixture, "hidden.txt");
        File.WriteAllBytes(hiddenFile, "hidden"u8.ToArray());
        File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(shellFixture, "child", "nested.txt"), "nested"u8.ToArray());
        File.WriteAllBytes(Path.Combine(shellFixture, "child", "nested-folder", "deep.txt"), "deep"u8.ToArray());

        var childPath = Path.Combine(shellFixture, "child");
        var projectionFixture = Path.Combine(Path.GetDirectoryName(fixture)!, "recursive-size-projection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scan = HandleBoundRecursiveDirectoryScanner.Scan([childPath], CancellationToken.None);
            var productionScan = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                shellFixture, [childPath], CancellationToken.None);
            if (!scan.TryGetValue(Path.GetFullPath(childPath), out var handleBoundRecursive) ||
                !productionScan.TryGetValue(Path.GetFullPath(childPath), out var recursive) ||
                handleBoundRecursive.Summary != recursive || recursive.Bytes != 10 ||
                recursive.Files != 2 || recursive.Directories != 1 || recursive.Unreadable != 0)
                throw new InvalidOperationException("The recursive-size fixture scanners did not agree on 10 bytes, 2 files, and 1 nested folder.");

            Check(Marshal.GetDelegateForFunctionPointer<RunSyntheticFolderTreeContractSmoke>(
                NativeLibrary.GetExport(nativeLibrary, "RunSyntheticFolderTreeContractSmoke"))(shellFixture));

            Directory.CreateDirectory(Path.Combine(projectionFixture, "child", "nested-folder"));
            Directory.CreateDirectory(Path.Combine(projectionFixture, "other-child"));
            var nestedFile = Path.Combine(projectionFixture, "child", "nested-folder", "deep.txt");
            File.WriteAllBytes(Path.Combine(projectionFixture, "child", "direct.txt"), "direct"u8.ToArray());
            File.WriteAllBytes(nestedFile, "deep"u8.ToArray());
            File.WriteAllBytes(Path.Combine(projectionFixture, "other-child", "other.txt"), "other-value!"u8.ToArray());
            File.WriteAllBytes(Path.Combine(projectionFixture, "root.txt"), "12345678"u8.ToArray());
            var projectionChildPath = Path.Combine(projectionFixture, "child");
            var projectionOtherChildPath = Path.Combine(projectionFixture, "other-child");
            var projectionScan = HandleBoundRecursiveDirectoryScanner.Scan(
                [projectionChildPath, projectionOtherChildPath], CancellationToken.None);
            var productionProjectionScan = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                projectionFixture, [projectionChildPath, projectionOtherChildPath], CancellationToken.None);
            if (!projectionScan.TryGetValue(Path.GetFullPath(projectionChildPath), out var handleBoundSnapshot) ||
                !projectionScan.TryGetValue(Path.GetFullPath(projectionOtherChildPath), out var handleBoundOtherSnapshot) ||
                !productionProjectionScan.TryGetValue(Path.GetFullPath(projectionChildPath), out var snapshot) ||
                !productionProjectionScan.TryGetValue(Path.GetFullPath(projectionOtherChildPath), out var otherSnapshot) ||
                handleBoundSnapshot.Summary != snapshot || handleBoundOtherSnapshot.Summary != otherSnapshot ||
                snapshot.Bytes != 10 || otherSnapshot.Bytes != 12)
                throw new InvalidOperationException("The production and handle-bound scans did not agree on the 10/12 byte projection.");
            var childParsingName = GetAbsoluteParsingName(projectionChildPath);
            var otherChildParsingName = GetAbsoluteParsingName(projectionOtherChildPath);

            // Re-scan after the file change so the updated native values come from the production scanner too.
            File.WriteAllBytes(nestedFile, "x"u8.ToArray());
            var updatedScan = HandleBoundRecursiveDirectoryScanner.Scan(
                [projectionChildPath, projectionOtherChildPath], CancellationToken.None);
            var productionUpdatedScan = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                projectionFixture, [projectionChildPath, projectionOtherChildPath], CancellationToken.None);
            if (!updatedScan.TryGetValue(Path.GetFullPath(projectionChildPath), out var updatedHandleBoundSnapshot) ||
                !updatedScan.TryGetValue(Path.GetFullPath(projectionOtherChildPath), out var updatedHandleBoundOtherSnapshot) ||
                !productionUpdatedScan.TryGetValue(Path.GetFullPath(projectionChildPath), out var updatedSnapshot) ||
                !productionUpdatedScan.TryGetValue(Path.GetFullPath(projectionOtherChildPath), out var updatedOtherSnapshot) ||
                updatedHandleBoundSnapshot.Summary != updatedSnapshot ||
                updatedHandleBoundOtherSnapshot.Summary != updatedOtherSnapshot ||
                updatedSnapshot.Bytes != 7 || updatedOtherSnapshot.Bytes != 12)
                throw new InvalidOperationException("The production and handle-bound rescans did not agree on the 7/12 byte projection.");
            Check(Marshal.GetDelegateForFunctionPointer<RunSyntheticFolderRecursiveSizeIdentityProjectionSmoke>(
                NativeLibrary.GetExport(nativeLibrary, "RunSyntheticFolderRecursiveSizeIdentityProjectionSmoke"))(
                    projectionFixture, childParsingName, checked((long)snapshot.Bytes)));
            Check(Marshal.GetDelegateForFunctionPointer<RunSyntheticFolderRecursiveSizeIdentityCacheSmoke>(
                NativeLibrary.GetExport(nativeLibrary, "RunSyntheticFolderRecursiveSizeIdentityCacheSmoke"))(
                    projectionFixture, childParsingName, otherChildParsingName, checked((long)snapshot.Bytes),
                    checked((long)otherSnapshot.Bytes), checked((long)updatedSnapshot.Bytes)));
            Console.WriteLine($"recursive-folder-size-projection: scanner-bytes={snapshot.Bytes}/{otherSnapshot.Bytes}; " +
                $"rescanned-bytes={updatedSnapshot.Bytes}/{updatedOtherSnapshot.Bytes}; disk-child-bytes=7; " +
                "native-column=text/VT_UI8/numeric-sort=passed; absolute-parsing-name-cache=independent; " +
                "same-pidl-update=rescan-value/text/property/sort-passed; re-enumerated-pidl-retained=passed; " +
                "provider-recreation=retained; window-created=False");
        }
        finally
        {
            if (Directory.Exists(projectionFixture)) Directory.Delete(projectionFixture, recursive: true);
            Directory.Delete(shellFixture, recursive: true);
        }
    }

    private static void VerifyWrappedShellItemDataObject(nint dataObject, IReadOnlyList<string>? expectedPaths = null)
    {
        var expectedCount = expectedPaths?.Count ?? 1;
        var format = new NativeFormatEtc { Format = 15, Aspect = 1, Index = -1, Tymed = 1 };
        var getDataHr = At<GetData>(dataObject, 3)(dataObject, ref format, out var medium);
        if (getDataHr < 0) throw new Exception($"Wrapped IDataObject.GetData(CF_HDROP) failed: 0x{getDataHr:X8}.");
        try
        {
            if (medium.Tymed != 1 || medium.Value == 0) throw new Exception("Wrapped CF_HDROP did not use a valid TYMED_HGLOBAL.");
            var dropFiles = GlobalLock(medium.Value);
            if (dropFiles == 0) throw new Exception("Could not lock the wrapped CF_HDROP memory.");
            try
            {
                var pathOffset = Marshal.ReadInt32(dropFiles);
                var isWide = Marshal.ReadInt32(dropFiles, 16);
                if (pathOffset < 20 || isWide != 1) throw new Exception("Wrapped CF_HDROP did not contain a valid Unicode DROPFILES block.");
                var bytes = GlobalSize(medium.Value).ToUInt64();
                if ((ulong)pathOffset >= bytes) throw new Exception("Wrapped CF_HDROP path offset was outside its storage medium.");
                var paths = new List<string>();
                var cursor = dropFiles + pathOffset;
                while ((ulong)(cursor - dropFiles) + sizeof(char) <= bytes)
                {
                    var path = Marshal.PtrToStringUni(cursor);
                    if (string.IsNullOrEmpty(path)) break;
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new Exception("Wrapped CF_HDROP did not preserve an existing filesystem source path: " + path);
                    paths.Add(path);
                    cursor += checked((path.Length + 1) * sizeof(char));
                }
                if (paths.Count != expectedCount || !MatchesExpectedPaths(paths, expectedPaths))
                    throw new Exception($"Wrapped CF_HDROP contained {paths.Count} paths, expected {expectedCount} matching paths.");
            }
            finally { GlobalUnlock(medium.Value); }
        }
        finally { ReleaseStgMedium(ref medium); }

        var shellIdListFormat = checked((ushort)RegisterClipboardFormatW("Shell IDList Array"));
        var shellIdListEtc = new NativeFormatEtc { Format = shellIdListFormat, Aspect = 1, Index = -1, Tymed = 1 };
        var shellIdListHr = At<GetData>(dataObject, 3)(dataObject, ref shellIdListEtc, out var shellIdListMedium);
        if (shellIdListHr < 0) throw new Exception($"Wrapped IDataObject.GetData(Shell IDList Array) failed: 0x{shellIdListHr:X8}.");
        try
        {
            if (shellIdListMedium.Tymed != 1 || shellIdListMedium.Value == 0) throw new Exception("Shell IDList Array did not use a valid TYMED_HGLOBAL.");
            var cida = GlobalLock(shellIdListMedium.Value);
            if (cida == 0) throw new Exception("Could not lock the Shell IDList Array memory.");
            try
            {
                var cidaBytes = GlobalSize(shellIdListMedium.Value).ToUInt64();
                var childCount = unchecked((uint)Marshal.ReadInt32(cida));
                var minimumCidaBytes = checked(((ulong)childCount + 2) * sizeof(uint));
                if (childCount != expectedCount || cidaBytes < minimumCidaBytes)
                    throw new Exception($"Expected {expectedCount} CIDA children, got {childCount} in {cidaBytes} bytes.");
                var parentOffset = unchecked((uint)Marshal.ReadInt32(cida, 4));
                if (parentOffset >= cidaBytes) throw new Exception("CIDA parent offset was outside the storage medium.");
                var paths = new List<string>(checked((int)childCount));
                for (var index = 0; index < childCount; index++)
                {
                    var offsetPosition = checked(4 + ((int)index + 1) * sizeof(uint));
                    var childOffset = unchecked((uint)Marshal.ReadInt32(cida, offsetPosition));
                    if (childOffset >= cidaBytes) throw new Exception("CIDA child offset was outside the storage medium.");
                    var absolute = ILCombine(cida + checked((int)parentOffset), cida + checked((int)childOffset));
                    if (absolute == 0) throw new Exception("Could not combine CIDA parent and child PIDLs.");
                    try
                    {
                        var path = DescribeFileSystemPath(absolute);
                        if (path.StartsWith("unresolved-", StringComparison.Ordinal) ||
                            (!File.Exists(path) && !Directory.Exists(path)))
                            throw new Exception("CIDA did not resolve to an existing filesystem item: " + path);
                        paths.Add(path);
                    }
                    finally { ILFree(absolute); }
                }
                if (!MatchesExpectedPaths(paths, expectedPaths))
                    throw new Exception("CIDA did not preserve the expected filesystem item paths.");
            }
            finally { GlobalUnlock(shellIdListMedium.Value); }
        }
        finally { ReleaseStgMedium(ref shellIdListMedium); }
        Console.WriteLine($"wrapped IDataObject clipboard formats: items={expectedCount}; CF_HDROP-paths=matched; CIDA-paths=matched");
    }

    private static bool MatchesExpectedPaths(IReadOnlyList<string> actualPaths, IReadOnlyList<string>? expectedPaths)
    {
        if (expectedPaths is null) return true;
        var expected = expectedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expected.Count == actualPaths.Count && actualPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected);
    }

    private static void VerifyWrappedMultiSelectionContextMenu(nint adapter, nint children, uint childCount)
    {
        var foreground = GetForegroundWindow();
        var contextMenuId = ContextMenuId;
        nint contextMenu = 0;
        var menu = CreatePopupMenu();
        if (menu == 0) throw new Exception("Could not create the in-memory menu used for wrapped multi-selection inspection.");
        try
        {
            Check(At<GetUIObjectOf>(adapter, 10)(adapter, 0, childCount, children, in contextMenuId, 0, out contextMenu));
            if (contextMenu == 0) throw new Exception("Wrapped multi-selection did not return an IContextMenu.");
            var contextMenu3Result = Marshal.QueryInterface(contextMenu, in ContextMenu3Id, out var contextMenu3);
            if (contextMenu3 != 0) Marshal.Release(contextMenu3);
            var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
            Check(queryResult);
            var commandCount = (uint)(queryResult & 0xffff);
            var itemCount = GetMenuItemCount(menu);
            if (commandCount == 0 || itemCount == 0)
                throw new Exception($"Wrapped multi-selection context menu was empty: commands={commandCount}; items={itemCount}.");
            var verbs = ReadContextMenuVerbs(contextMenu, commandCount);
            var expectedVerbs = new[] { "copy", "delete" };
            if (expectedVerbs.Any(expected => !verbs.Contains(expected, StringComparer.OrdinalIgnoreCase)))
                throw new Exception("Wrapped multi-selection context menu is missing native verbs: " +
                    string.Join('|', expectedVerbs.Except(verbs, StringComparer.OrdinalIgnoreCase)));
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("Wrapped multi-selection menu construction changed the foreground window.");
            Console.WriteLine($"wrapped multi-selection native context menu: commands={commandCount}; menu-items={itemCount}; copy/delete=True; IContextMenu3={(contextMenu3Result >= 0 ? "available" : $"HRESULT=0x{contextMenu3Result:X8}")}; displayed=False");
        }
        finally
        {
            if (contextMenu != 0) Marshal.Release(contextMenu);
            DestroyMenu(menu);
        }
    }

    private static string GetAbsoluteParsingName(string path)
    {
        Check(SHParseDisplayName(Path.GetFullPath(path), 0, out var pidl, 0, out _));
        try { return ShellListViewRowIdentity.GetAbsoluteParsingName(pidl); }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static string ReadDetailsText(nint details)
    {
        if (Marshal.ReadInt32(details, 8) != 0) throw new Exception("Expected STRRET_WSTR.");
        var pointer = Marshal.ReadIntPtr(details, 8 + (IntPtr.Size == 8 ? 8 : 4));
        try { return pointer == 0 ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { if (pointer != 0) Marshal.FreeCoTaskMem(pointer); }
    }

    private static int GetPropertyDescriptionStatus(ShellColumnManager.NativeKey key)
    {
        var interfaceId = new Guid("6F79D558-3E96-4549-A1D1-7D75D2288814");
        var result = PSGetPropertyDescription(in key, in interfaceId, out var description);
        if (description != 0) Marshal.Release(description);
        return result;
    }

    private static PropertyDescriptionEvidence GetPropertyDescriptionEvidence(ShellColumnManager.NativeKey key)
    {
        var interfaceId = new Guid("6F79D558-3E96-4549-A1D1-7D75D2288814");
        var result = PSGetPropertyDescription(in key, in interfaceId, out var description);
        if (result < 0 || description == 0) return new(result, 0, result, 0, result, 0, result, 0, result, 0, result, "unavailable");
        try
        {
            var typeResult = At<GetPropertyType>(description, 5)(description, out var variantType);
            var typeFlagsResult = At<GetPropertyTypeFlags>(description, 8)(description, 0x80001fff, out var typeFlags); // PDTF_MASK_ALL.
            var displayTypeResult = At<GetPropertyDisplayType>(description, 11)(description, out var displayType);
            var columnStateResult = At<GetPropertyColumnState>(description, 12)(description, out var columnState);
            var sortDescriptionResult = At<GetPropertySortDescription>(description, 16)(description, out var sortDescription);
            var sortAliasEvidence = GetPropertySortAliasEvidence(description);
            return new(result, variantType, typeResult, typeFlags, typeFlagsResult, displayType, displayTypeResult, columnState, columnStateResult, sortDescription, sortDescriptionResult, sortAliasEvidence);
        }
        finally { Marshal.Release(description); }
    }

    private static string GetPropertySortAliasEvidence(nint description)
    {
        var aliasInfoIid = new Guid("F67104FC-2AF9-46FD-B32D-243C1404F3D1");
        var propertyDescriptionIid = new Guid("6F79D558-3E96-4549-A1D1-7D75D2288814");
        var queryResult = Marshal.QueryInterface(description, in aliasInfoIid, out var aliasInfo);
        if (queryResult < 0 || aliasInfo == 0) return $"QI=0x{queryResult:X8}";
        try
        {
            var result = At<GetSortByAlias>(aliasInfo, 23)(aliasInfo, in propertyDescriptionIid, out var aliasDescription);
            if (result == 1 && aliasDescription == 0) return "no-primary-alias (S_FALSE)";
            if (result < 0 || aliasDescription == 0) return $"GetSortByAlias=0x{result:X8}";
            try
            {
                var keyResult = At<GetPropertyKey>(aliasDescription, 3)(aliasDescription, out var key);
                return $"GetSortByAlias=0x{result:X8};GetPropertyKey=0x{keyResult:X8};key={key.FormatId:B}/{key.PropertyId}";
            }
            finally { Marshal.Release(aliasDescription); }
        }
        finally { Marshal.Release(aliasInfo); }
    }

    private static void CheckCompare(int hr, short expected, string label)
    {
        if (hr < 0 || unchecked((short)(hr & 0xffff)) != expected)
            throw new Exception($"CompareIDs {label}: HRESULT=0x{hr:X8}; expected comparison={expected}.");
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private static string ReadNativeComparisonStats()
    {
        var module = NativeLibrary.Load(FindAdapterLibrary());
        try
        {
            var custom = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellColumnComparisonCount"))();
            var total = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetAllShellColumnComparisonCount"))();
            var index = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetLastShellColumnComparisonIndex"))();
            var detailsEx = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetCustomShellDetailsExCount"))();
            var detailsOf = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetCustomShellDetailsOfCount"))();
            var customItemDetailsOf = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetCustomShellItemDetailsOfCount"))();
            var customHeaderDetailsOf = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetCustomShellHeaderDetailsOfCount"))();
            var documentCompare = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetDocumentByteCountShellComparisonCount"))();
            var mapColumnToScid = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetMapColumnToScidRequestCount"))();
            var defaultColumnRequests = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetDefaultColumnRequestCount"))();
            var sortCallbackClicks = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellSortCallbackColumnClickCount"))();
            var sortCallbackLastColumn = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellSortCallbackLastColumn"))();
            var sortCallbackDefaults = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellSortCallbackGetDefaultsCount"))();
            var documentDetailsEx = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetDocumentByteCountShellDetailsExCount"))();
            var documentDetailsOf = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetDocumentByteCountShellDetailsOfCount"))();
            var sizeDetailsEx = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStandardSizeShellDetailsExCount"))();
            var sizeCompare = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStandardSizeShellComparisonCount"))();
            var sizeDetailsOf = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStandardSizeShellDetailsOfCount"))();
            var propertyStoreQuery = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreQueryInterfaceRequestCount"))();
            var propertyStoreBindObject = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreBindToObjectRequestCount"))();
            var propertyStoreBindStorage = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreBindToStorageRequestCount"))();
            var propertyStoreUiObject = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreUiObjectRequestCount"))();
            var propertyStoreFactoryBind = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreFactoryBindRequestCount"))();
            var propertyStoreFactoryCreate = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreFactoryCreateRequestCount"))();
            var propertyStoreGetValue = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetPropertyStoreValueRequestCount"))();
            var persistClass = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellPersistClassRequestCount"))();
            var persistRoot = Marshal.GetDelegateForFunctionPointer<GetCompareCount>(NativeLibrary.GetExport(module, "GetStaticShellPersistRootRequestCount"))();
            return $"compare={total}/{custom}/{index}; Persistence(GetClassID/GetCurFolder)={persistClass}/{persistRoot}; size-CompareIDs={sizeCompare}; " +
                $"document-byte-count(CompareIDs/GetDetailsEx/GetDetailsOf)={documentCompare}/{documentDetailsEx}/{documentDetailsOf}; MapColumnToSCID={mapColumnToScid}; GetDefaultColumn={defaultColumnRequests}; SFVM_COLUMNCLICK={sortCallbackClicks}(last={sortCallbackLastColumn}); SFVM_GETSORTDEFAULTS={sortCallbackDefaults}; " +
                $"GetDetailsEx(custom/size)={detailsEx}/{sizeDetailsEx}; GetDetailsOf(custom total/header/item, size-item)={detailsOf}/{customHeaderDetailsOf}/{customItemDetailsOf}/{sizeDetailsOf}; IPropertyStore(QI/BindObject/BindStorage/UIObject)=" +
                $"{propertyStoreQuery}/{propertyStoreBindObject}/{propertyStoreBindStorage}/{propertyStoreUiObject}; IPropertyStoreFactory(Bind/Create/GetValue)=" +
                $"{propertyStoreFactoryBind}/{propertyStoreFactoryCreate}/{propertyStoreGetValue}";
        }
        finally { NativeLibrary.Free(module); }
    }

    private static void CheckBoundary(nint parent, nint foreground, string stage, bool expectVisible = false)
    { if (IsWindowVisible(parent) != expectVisible || GetForegroundWindow() != foreground) throw new Exception($"{stage} crossed the isolated-window/foreground boundary."); }
    private static void Pump(int milliseconds)
    { var until = Environment.TickCount64 + milliseconds; while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); } }
    private static T At<T>(nint value, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct NativeSortColumn { public ShellColumnManager.NativeKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeFormatEtc
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeStorageMedium
    {
        public uint Tymed;
        public nint Value;
        public nint ReleaseUnknown;
    }
    private readonly record struct PropertyDescriptionEvidence(int HResult, ushort VariantType, int TypeHResult, uint TypeFlags, int TypeFlagsHResult, uint DisplayType, int DisplayTypeHResult,
        uint ColumnState, int ColumnStateHResult, uint SortDescription, int SortDescriptionHResult, string SortAliasEvidence);
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextCapacity;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint ColumnCount;
        public nint Columns;
        public nint ColumnFormats;
        public int Group;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public nint Text;
        public int TextCapacity;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinimumWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeNotificationHeader
    {
        public nint Window;
        public nuint Id;
        public int Code;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewNotification
    {
        public NativeNotificationHeader Header;
        public int Item;
        public int SubItem;
        public uint NewState;
        public uint OldState;
        public uint Changed;
        public Point ActionPoint;
        public nint Parameter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbW;
        public nint ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public Point Point;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateAdapter(nint folder, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BindToObject(nint self, nint item, nint bind, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumNext(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumNextMany(nint self, uint count, nint items, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumSkip(nint self, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumClone(nint self, out nint clone);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumReset(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetProbeFlag();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint items, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUIObjectOf(nint self, nint owner, uint count, nint items, in Guid iid, nint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetData(nint self, ref NativeFormatEtc format, out NativeStorageMedium medium);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetNameOf(nint self, nint owner, nint item, nint name, uint flags, out nint renamed);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TransformPidl(nint source, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VerifyViewFolder(nint view, nint adapter, out int matches);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCompareCount();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDefaultColumnState(nint self, uint column, out uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CompareIDs(nint self, nint sortColumn, nint left, nint right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsEx(nint self, nint item, nint key, nint variant);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyType(nint self, out ushort variantType);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyTypeFlags(nint self, uint mask, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyDisplayType(nint self, out uint displayType);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyColumnState(nint self, out uint columnState);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertySortDescription(nint self, out uint description);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortByAlias(nint self, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyKey(nint self, out ShellColumnManager.NativeKey key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ResetProbeCounters();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetLegacyDetailsCallbackProbe(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint GetShellDetailsSizeNative();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MapColumn(nint self, uint column, nint key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ParseDisplayName(nint self, nint hwnd, nint bind, nint name, nint eaten, out nint item, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Rearrange(nint self, nint sortColumn);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellItemDisplayName(nint self, uint nameType, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetParentAndItem(nint self, out nint parentPidl, out nint parentFolder, out nint childPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellItemArrayCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellItemArrayItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BindShellItemArrayHandler(nint self, nint bindContext, in Guid handler, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BindShellItemHandler(nint self, nint bindContext, in Guid handler, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint flags, nint reserved, nint buffer, uint bufferLength);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentViewMode(nint self, out uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MoveItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItems(nint self, nint dataObject, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItems(nint self, nint dataObject);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellItemPropertyStore(nint self, uint flags, in Guid iid, out nint store);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BindFolderChild(nint self, nint item, nint bindContext, in Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFactoryPropertyStore(nint self, uint flags, nint factory, in Guid iid, out nint store);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyStoreValue(nint self, in ShellColumnManager.NativeKey key, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetPropertyStoreFactoryProbe(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RunSyntheticFolderTreeContractSmoke([MarshalAs(UnmanagedType.LPWStr)] string backingPath);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RunSyntheticFolderRecursiveSizeIdentityProjectionSmoke([MarshalAs(UnmanagedType.LPWStr)] string backingPath, [MarshalAs(UnmanagedType.LPWStr)] string parsingName, long scannedBytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RunSyntheticFolderRecursiveSizeIdentityCacheSmoke([MarshalAs(UnmanagedType.LPWStr)] string backingPath, [MarshalAs(UnmanagedType.LPWStr)] string childParsingName, [MarshalAs(UnmanagedType.LPWStr)] string otherParsingName, long initialBytes, long otherRowBytes, long updatedBytes);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint left, nint right);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateItemWithParent(nint parentPidl, nint parentFolder, nint childPidl, in Guid iid, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out nint item);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateShellItemArrayFromShellItem(nint item, in Guid iid, out nint array);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateShellItemArrayFromDataObject(nint dataObject, in Guid iid, out nint array);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern nint GetParentWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetDlgCtrlID")] private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int maxCount);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint GlobalSize(nint memory);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref NativeStorageMedium medium);
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string formatName);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    private delegate bool EnumChildProc(nint window, nint parameter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);
    [DllImport("propsys.dll", PreserveSig = true)] private static extern int PSGetPropertyDescription(
        in ShellColumnManager.NativeKey key, in Guid interfaceId, out nint description);
}
