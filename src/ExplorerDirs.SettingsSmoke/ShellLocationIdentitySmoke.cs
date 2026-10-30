using ExplorerDirs.App;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellLocationIdentitySmoke
{
    internal static void RunShellNameSyntaxOnly()
    {
        Check(ShellLocationIdentity.TryNormalize("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                out var absoluteShellName, out var isFileSystem) &&
            absoluteShellName == "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" && !isFileSystem,
            "absolute Shell parsing names are retained for later Shell folder validation");
        Check(!ShellLocationIdentity.TryNormalize("https://example.invalid/::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", out _, out _),
            "absolute Shell parsing names do not make protocol URIs acceptable");
        const string shellName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        var imported = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=0\nFolder1=%02%00%00%00\n",
            (value, encoded) => encoded && value == "%02%00%00%00" ? shellName : null);
        Check(imported.TabCount == 1 && imported.Panes[0][0].Path == shellName,
            "an encoded Q-Dir PIDL resolver may preserve any absolute Shell folder identity");
    }

    private static void RunDirectChildPidlSmoke()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
        var resolved = ShellSelectionPidl.TryGetDirectChild("shell:MyComputerFolder", drive, out var child);
        try
        {
            Check(resolved && child != IntPtr.Zero,
                "a drive selected under This PC resolves to a relative Shell child PIDL");
        }
        finally { if (child != IntPtr.Zero) Marshal.FreeCoTaskMem(child); }

        resolved = ShellSelectionPidl.TryGetDirectChild("shell:MyComputerFolder",
            Path.Combine(Environment.SystemDirectory, "not-a-drive-child"), out child);
        try
        {
            Check(!resolved && child == IntPtr.Zero,
                "an item that is not a direct child of the current Shell folder is rejected");
        }
        finally { if (child != IntPtr.Zero) Marshal.FreeCoTaskMem(child); }
    }

    internal static void Run()
    {
        Check(ShellLocationIdentity.TryNormalize("C:\\Users\\Marvin", out var directory, out var isFileSystem) &&
            directory == "C:\\Users\\Marvin" && isFileSystem,
            "ordinary absolute paths remain filesystem session identities");
        Check(ShellLocationIdentity.TryNormalize("shell:MyComputerFolder", out var computer, out isFileSystem) &&
            computer == "shell:MyComputerFolder" && !isFileSystem,
            "This PC uses a textual Shell identity");
        Check(ShellLocationIdentity.TryNormalize("SHELL:recyclebinfolder", out var recycleBin, out isFileSystem) &&
            recycleBin == "shell:RecycleBinFolder" && !isFileSystem,
            "Recycle Bin aliases normalize to a canonical Shell identity");
        Check(ShellLocationIdentity.TryNormalize("shell:Libraries", out var libraries, out isFileSystem) &&
            libraries == "shell:Libraries" && !isFileSystem,
            "Libraries uses a textual Shell identity");
        Check(!ShellLocationIdentity.TryNormalize("https://example.invalid", out _, out _),
            "protocol handlers are not accepted as persisted folders");
        Check(ShellLocationIdentity.TryNormalize("shell:UnknownNamespace", out var unknownAlias, out isFileSystem) &&
            unknownAlias == "shell:UnknownNamespace" && !isFileSystem,
            "unknown Shell aliases remain candidates until native folder resolution confirms availability");
        RunShellNameSyntaxOnly();
        RunDirectChildPidlSmoke();
        Check(!ShellLocationIdentity.TryNormalize("https://example.invalid/::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", out _, out _),
            "absolute Shell parsing names do not make protocol URIs acceptable");
        Check(ShellLocationIdentity.TryNormalize("C:\\archive.zip", out var archive, out isFileSystem) &&
            archive == "C:\\archive.zip" && isFileSystem,
            "a ZIP suffix alone does not convert an ordinary path into a Shell folder");
        Check(ShellLocationIdentity.IsZipParsingPath(@"C:\archive.zip") &&
            ShellLocationIdentity.IsZipParsingPath(@"C:\archive.zip\folder") &&
            !ShellLocationIdentity.IsZipParsingPath(@"C:\archive.zip.old\folder") &&
            !ShellLocationIdentity.IsZipParsingPath(@"C:\folder.zip-not-an-archive"),
            "ZIP parsing candidates require a complete .zip path component");
        var breadcrumb = BreadcrumbPath.Build("shell:RecycleBinFolder");
        Check(breadcrumb.Count == 1 && breadcrumb[0].Label == "回收站" &&
            breadcrumb[0].Path == "shell:RecycleBinFolder",
            "virtual folder breadcrumbs show a friendly label while preserving the Shell identity");
        var navigation = new ShellNavigationState(@"C:\start");
        navigation.CompleteDirectory("shell:RecycleBinFolder", isFileSystemLocation: false);
        Check(!navigation.IsFileSystemLocation && navigation.DirectoryPath == "shell:RecycleBinFolder",
            "settled namespace locations remain distinct from filesystem directories");
        navigation.CompleteDirectory(@"C:\start");
        Check(navigation.IsFileSystemLocation && navigation.DirectoryPath == @"C:\start",
            "returning from a namespace location restores filesystem search scope");

        foreach (var alias in new[] { "shell:MyComputerFolder", "shell:RecycleBinFolder", "shell:Libraries" })
        {
            nint pidl = 0;
            try
            {
                Check(ShellLocationIdentity.TryGetFolderPidl(alias, out pidl),
                    $"Windows Shell resolves {alias} as a folder without creating an ExplorerBrowser");
                Check(ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var resolved, out var isFilesystem) &&
                    resolved == alias && !isFilesystem,
                    $"Windows Shell folder identity round-trips for {alias}");
            }
            finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
        }

        const string thisPcParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        const string qDirThisPcParsingName = "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        var thisPcIdentity = string.Empty;
        nint thisPcPidl = 0;
        try
        {
            Check(ShellLocationIdentity.TryGetFolderPidl(thisPcParsingName, out thisPcPidl),
                "Windows Shell resolves an absolute namespace parsing name as a folder without creating an ExplorerBrowser");
            var displayName = ShellLocationIdentity.GetDisplayName(thisPcParsingName);
            Check(!displayName.StartsWith("::", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(displayName),
                "absolute Shell parsing names display the Shell folder's friendly name instead of raw syntax");
            Check(ShellLocationIdentity.TryGetPersistedIdentity(thisPcPidl, out thisPcIdentity, out var isFilesystem) &&
                ShellLocationIdentity.TryNormalize(thisPcIdentity, out var normalizedThisPc, out var normalizedIsFilesystem) &&
                normalizedThisPc == thisPcIdentity && !isFilesystem && !normalizedIsFilesystem,
                "absolute namespace folder PIDLs persist as Shell parsing names");
            var encodedThisPc = EncodePidl(thisPcPidl);
            Check(QdrImporter.ResolveLocation(encodedThisPc, encoded: true) == thisPcIdentity,
                "Q-Dir encoded absolute namespace PIDL resolves to its canonical virtual identity");
            nint qDirThisPcPidl = 0;
            try
            {
                Check(ShellLocationIdentity.TryGetFolderPidl(qDirThisPcParsingName, out qDirThisPcPidl),
                    "Q-Dir's documented shell:::{GUID} text syntax resolves as a Shell folder");
                Check(ShellLocationIdentity.TryGetPersistedIdentity(qDirThisPcPidl, out var qDirIdentity, out var qDirIsFilesystem) &&
                    qDirIdentity == thisPcIdentity && !qDirIsFilesystem &&
                    ShellLocationIdentity.GetDisplayName(qDirThisPcParsingName) == displayName,
                    "Q-Dir's documented virtual folder syntax normalizes to the same identity and friendly label");
            }
            finally { if (qDirThisPcPidl != 0) Marshal.FreeCoTaskMem(qDirThisPcPidl); }
            var importedThisPc = QdrImporter.Parse(
                $"Q-Dir Version=12.79\nViewMode=0\nFolder1={encodedThisPc}\n", QdrImporter.ResolveLocation);
            Check(importedThisPc.TabCount == 1 && importedThisPc.Panes[0][0].Path == thisPcIdentity,
                "an actual Q-Dir encoded virtual-folder PIDL imports as its stable Shell identity");
            var importedTextThisPc = QdrImporter.Parse(
                $"Q-Dir Version=7.84\nViewMode=0\nDir1={qDirThisPcParsingName}\n", QdrImporter.ResolveLocation);
            Check(importedTextThisPc.TabCount == 1 && importedTextThisPc.Panes[0][0].Path == thisPcIdentity,
                "the documented Q-Dir shell:::{GUID} text path imports as the same virtual folder");
            var exportFallbackPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var exportState = new SessionState(1, 3,
                [[new(thisPcParsingName, ViewState.Default())],
                 [new(exportFallbackPath, ViewState.Default())],
                 [new(exportFallbackPath, ViewState.Default())],
                 [new(exportFallbackPath, ViewState.Default())]], 0, [0, 0, 0, 0]);
            var exported = QdrExporter.Render(exportState, []);
            var importedExport = QdrImporter.Parse(exported, QdrImporter.ResolveLocation);
            var exportedBase = string.Join('\n', exported.Split('\n').Where(line =>
                !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
            var importedExportBase = QdrImporter.Parse(exportedBase, QdrImporter.ResolveLocation);
            Check(exported.Contains("Dir1=shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
                "exported virtual Shell paths use Q-Dir's documented text syntax");
            Check(importedExport.Panes[0][0].Path == thisPcParsingName,
                "an exported virtual Shell path preserves its app-session identity through the extension");
            Check(importedExportBase.Panes[0][0].Path == thisPcIdentity,
                "an exported virtual Shell path imports from base fields without the app extension");
        }
        finally { if (thisPcPidl != 0) Marshal.FreeCoTaskMem(thisPcPidl); }

        const string networkParsingName = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        nint networkPidl = 0;
        nint roundTripPidl = 0;
        try
        {
            Check(ShellLocationIdentity.TryGetFolderPidl(networkParsingName, out networkPidl),
                "an inbox Shell namespace outside the fixed aliases resolves as a folder");
            Check(ShellLocationIdentity.TryGetPersistedIdentity(networkPidl, out var networkIdentity, out var networkIsFilesystem) &&
                !networkIsFilesystem && ShellLocationIdentity.TryGetFolderPidl(networkIdentity, out roundTripPidl),
                "an arbitrary resolved Shell folder persists to a re-resolvable absolute identity");
            try
            {
                var importedNetwork = QdrImporter.Parse(
                    $"Q-Dir Version=12.79\nViewMode=0\nFolder1={EncodePidl(networkPidl)}\n", QdrImporter.ResolveLocation);
                Check(importedNetwork.TabCount == 1 && importedNetwork.Panes[0][0].Path == networkIdentity,
                    "an encoded Q-Dir PIDL imports a Shell folder outside the fixed aliases");

                var fallbackPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var exportState = new SessionState(1, 3,
                    [[new(networkParsingName, ViewState.Default())],
                     [new(fallbackPath, ViewState.Default())],
                     [new(fallbackPath, ViewState.Default())],
                     [new(fallbackPath, ViewState.Default())]], 0, [0, 0, 0, 0]);
                var exportedNetwork = QdrExporter.Render(exportState, []);
                var exportedBase = string.Join('\n', exportedNetwork.Split('\n').Where(line =>
                    !line.StartsWith(QdrExporter.Prefix, StringComparison.OrdinalIgnoreCase)));
                var importedExport = QdrImporter.Parse(exportedBase, QdrImporter.ResolveLocation);
                Check(exportedNetwork.Contains("Dir1=shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}") &&
                    importedExport.Panes[0][0].Path == networkIdentity,
                    "an arbitrary Shell folder exports with Q-Dir text syntax and imports without our extension");
            }
            finally { if (roundTripPidl != 0) Marshal.FreeCoTaskMem(roundTripPidl); }
        }
        finally { if (networkPidl != 0) Marshal.FreeCoTaskMem(networkPidl); }

        var ordinaryFile = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-not-folder-{Guid.NewGuid():N}.txt");
        File.WriteAllText(ordinaryFile, "not a folder");
        nint ordinaryFilePidl = 0;
        try
        {
            var missingDirectory = Path.Combine(Path.GetDirectoryName(ordinaryFile)!, $"ExplorerDirs-missing-{Guid.NewGuid():N}");
            var validDirectory = Path.GetTempPath();
            var plainPathImport = QdrImporter.Parse(
                $"Q-Dir Version=12.79\nViewMode=0\nDir1={ordinaryFile}\nDir2={missingDirectory}\n" +
                $"Dir3={thisPcParsingName}\nDir4={validDirectory}\n",
                QdrImporter.ResolveLocation);
            Check(plainPathImport.TabCount == 2 && plainPathImport.Panes[2][0].Path == thisPcIdentity &&
                string.Equals(plainPathImport.Panes[3][0].Path, Path.TrimEndingDirectorySeparator(validDirectory), StringComparison.OrdinalIgnoreCase) &&
                plainPathImport.Warnings.Any(warning => warning.Contains("无效")),
                "plain Q-Dir paths keep real folders and skip files or missing folders independently");

            Marshal.ThrowExceptionForHR(SHParseDisplayName(ordinaryFile, IntPtr.Zero, out ordinaryFilePidl, 0, out _));
            var notFolder = QdrImporter.Parse(
                $"Q-Dir Version=12.79\nViewMode=0\nFolder1={EncodePidl(ordinaryFilePidl)}\nDir2={thisPcParsingName}\n",
                QdrImporter.ResolveLocation);
            Check(notFolder.TabCount == 1 && notFolder.Panes[1][0].Path == thisPcIdentity &&
                notFolder.Warnings.Any(warning => warning.Contains("无效")),
                "an encoded Q-Dir PIDL for an ordinary file is skipped while a valid Shell folder is preserved");
        }
        finally
        {
            if (ordinaryFilePidl != 0) Marshal.FreeCoTaskMem(ordinaryFilePidl);
            File.Delete(ordinaryFile);
        }

        var zipNamedDirectory = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-folder-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(zipNamedDirectory);
        nint directoryPidl = 0;
        try
        {
            Check(ShellLocationIdentity.TryGetFolderPidl(zipNamedDirectory, out directoryPidl) &&
                ShellLocationIdentity.TryGetPersistedIdentity(directoryPidl, out var identity, out isFileSystem) &&
                isFileSystem && string.Equals(identity, zipNamedDirectory, StringComparison.OrdinalIgnoreCase),
                "a real filesystem directory whose name ends in .zip remains a filesystem location");
        }
        finally
        {
            if (directoryPidl != 0) Marshal.FreeCoTaskMem(directoryPidl);
            Directory.Delete(zipNamedDirectory);
        }

        var archiveDirectory = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-zip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(archiveDirectory);
        var archivePath = Path.Combine(archiveDirectory, "archive.zip");
        nint archivePidl = 0, archiveChildPidl = 0;
        try
        {
            using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("folder/inside.txt").Open()))
                writer.Write("shell location smoke");

            if (!ShellLocationIdentity.TryGetFolderPidl(archivePath, out archivePidl))
            {
                Console.WriteLine("ZIP Shell namespace unavailable; skipped native archive PIDL smoke");
                return;
            }
            Check(ShellLocationIdentity.TryGetPersistedIdentity(archivePidl, out var archiveIdentity, out isFileSystem) &&
                !isFileSystem && ShellLocationIdentity.IsZipParsingPath(archiveIdentity),
                "a ZIP archive root resolves and persists as a Shell namespace location");
            var archiveChild = Path.Combine(archivePath, "folder");
            Check(ShellLocationIdentity.TryGetFolderPidl(archiveChild, out archiveChildPidl) &&
                ShellLocationIdentity.TryGetPersistedIdentity(archiveChildPidl, out var childIdentity, out isFileSystem) &&
                !isFileSystem && ShellLocationIdentity.IsZipParsingPath(childIdentity),
                "a folder inside a ZIP archive resolves and persists as a Shell namespace location");
            Console.WriteLine("ZIP Shell archive root and child PIDL smoke: PASS");
        }
        finally
        {
            if (archiveChildPidl != 0) Marshal.FreeCoTaskMem(archiveChildPidl);
            if (archivePidl != 0) Marshal.FreeCoTaskMem(archivePidl);
            if (File.Exists(archivePath)) File.Delete(archivePath);
            Directory.Delete(archiveDirectory);
        }
    }

    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception("Failed: " + message); }

    private static string EncodePidl(nint pidl)
    {
        var bytes = new byte[ILGetSize(pidl)];
        Marshal.Copy(pidl, bytes, 0, bytes.Length);
        return string.Concat(bytes.Select(value => "%" + value.ToString("X2")));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl,
        uint attributes, out uint receivedAttributes);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(nint pidl);
}
