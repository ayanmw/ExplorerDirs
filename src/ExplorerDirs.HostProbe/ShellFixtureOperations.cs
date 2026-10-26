using System.IO;
using System.Runtime.InteropServices;

// Diagnostic-only IFileOperation producer. Operates exclusively on the four
// generated fixtures, without clipboard, undo records, copy hooks or dialogs.
internal static class ShellFixtureOperations
{
    internal static void MoveBetweenFixtures(string root, string sourceFolder, string sourceName,
        string destinationFolder, string destinationName)
    {
        root = Path.GetFullPath(root);
        sourceFolder = Path.GetFullPath(sourceFolder);
        destinationFolder = Path.GetFullPath(destinationFolder);
        if (!IsFixtureFolder(root, sourceFolder) || !IsFixtureFolder(root, destinationFolder) ||
            string.Equals(sourceFolder, destinationFolder, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourceName) != sourceName || Path.GetFileName(destinationName) != destinationName)
            throw new ArgumentException("Cross-pane move paths must stay in two distinct generated fixture folders.");

        var sourcePath = Path.Combine(sourceFolder, sourceName);
        var destinationPath = Path.Combine(destinationFolder, destinationName);
        if ((File.GetAttributes(sourcePath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
            File.ReadAllText(sourcePath) != "initial" || File.Exists(destinationPath))
            throw new InvalidOperationException("Cross-pane move fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            // FOF_SILENT | NOCONFIRMATION | NOCONFIRMMKDIR | NOERRORUI,
            // FOFX_EARLYFAILURE | NOCOPYHOOKS. No elevation/undo/recycle flags.
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var source = Item(sourcePath);
            var destination = Item(destinationFolder);
            try { Check(At<Move>(operation, 14)(operation, source, destination, destinationName, 0)); }
            finally
            {
                Marshal.Release(destination);
                Marshal.Release(source);
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("Cross-pane Shell move was aborted.");
            if (File.Exists(sourcePath) || !File.Exists(destinationPath) || File.ReadAllText(destinationPath) != "initial")
                throw new IOException("Cross-pane Shell move returned without the expected fixture result.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void CopyAcrossVolumes(string sourceRoot, string sourceFolder, string sourceName,
        string destinationRoot, string destinationFolder, string destinationName) =>
        OperateAcrossVolumes(sourceRoot, sourceFolder, sourceName, destinationRoot, destinationFolder, destinationName, copy: true);

    internal static void MoveAcrossVolumes(string sourceRoot, string sourceFolder, string sourceName,
        string destinationRoot, string destinationFolder, string destinationName) =>
        OperateAcrossVolumes(sourceRoot, sourceFolder, sourceName, destinationRoot, destinationFolder, destinationName, copy: false);

    internal static void DeleteAcrossVolumeFixture(string root, string folder, string name)
    {
        root = Path.GetFullPath(root);
        folder = Path.GetFullPath(folder);
        if (!IsFixtureFolder(root, folder) || Path.GetFileName(name) != name)
            throw new ArgumentException("Cross-volume fixture delete paths must stay inside the generated root.");
        var path = Path.Combine(folder, name);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            File.ReadAllText(path) != "initial")
            throw new InvalidOperationException("Cross-volume delete fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var item = Item(path);
            try { Check(At<Delete>(operation, 18)(operation, item, 0)); }
            finally { Marshal.Release(item); }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || File.Exists(path)) throw new IOException("Cross-volume fixture delete did not complete.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void CreateFolder(string root, string parentFolder, string name)
    {
        root = Path.GetFullPath(root);
        parentFolder = Path.GetFullPath(parentFolder);
        if (!IsFixtureFolder(root, parentFolder) || Path.GetFileName(name) != name ||
            Directory.Exists(Path.Combine(parentFolder, name)) || File.Exists(Path.Combine(parentFolder, name)))
            throw new ArgumentException("New-folder fixture must be a new child of a generated fixture folder.");
        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var destination = Item(parentFolder);
            try { Check(At<NewItem>(operation, 20)(operation, destination, (uint)FileAttributes.Directory, name, null, 0)); }
            finally { Marshal.Release(destination); }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            var createdPath = Path.Combine(parentFolder, name);
            if (aborted != 0 || (File.GetAttributes(createdPath) & FileAttributes.Directory) == 0)
                throw new IOException("Shell new-folder operation returned without the expected fixture folder.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void DeleteEmptyFolder(string root, string parentFolder, string name)
    {
        root = Path.GetFullPath(root);
        parentFolder = Path.GetFullPath(parentFolder);
        if (!IsFixtureFolder(root, parentFolder) || Path.GetFileName(name) != name)
            throw new ArgumentException("Folder delete fixture must stay inside a generated fixture root.");
        var path = Path.Combine(parentFolder, name);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory ||
            Directory.EnumerateFileSystemEntries(path).Any())
            throw new InvalidOperationException("Shell folder-delete fixture must be an empty ordinary directory.");
        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var item = Item(path);
            try { Check(At<Delete>(operation, 18)(operation, item, 0)); }
            finally { Marshal.Release(item); }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || Directory.Exists(path)) throw new IOException("Shell empty-folder delete did not complete.");
        }
        finally { Marshal.Release(operation); }
    }

    private static void OperateAcrossVolumes(string sourceRoot, string sourceFolder, string sourceName,
        string destinationRoot, string destinationFolder, string destinationName, bool copy)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        sourceFolder = Path.GetFullPath(sourceFolder);
        destinationRoot = Path.GetFullPath(destinationRoot);
        destinationFolder = Path.GetFullPath(destinationFolder);
        var sourceVolume = Path.GetPathRoot(sourceFolder);
        var destinationVolume = Path.GetPathRoot(destinationFolder);
        if (!IsFixtureFolder(sourceRoot, sourceFolder) || !IsFixtureFolder(destinationRoot, destinationFolder) ||
            string.Equals(sourceVolume, destinationVolume, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourceName) != sourceName || Path.GetFileName(destinationName) != destinationName)
            throw new ArgumentException("Cross-volume Shell operations require distinct generated fixture roots on different volumes.");

        var sourcePath = Path.Combine(sourceFolder, sourceName);
        var destinationPath = Path.Combine(destinationFolder, destinationName);
        if ((File.GetAttributes(sourcePath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            File.ReadAllText(sourcePath) != "initial" || File.Exists(destinationPath))
            throw new InvalidOperationException("Cross-volume Shell operation fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var source = Item(sourcePath);
            var destination = Item(destinationFolder);
            try
            {
                if (copy) Check(At<Copy>(operation, 16)(operation, source, destination, destinationName, 0));
                else Check(At<Move>(operation, 14)(operation, source, destination, destinationName, 0));
            }
            finally
            {
                Marshal.Release(destination);
                Marshal.Release(source);
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || !File.Exists(destinationPath) || File.ReadAllText(destinationPath) != "initial" ||
                (copy ? !File.Exists(sourcePath) : File.Exists(sourcePath)))
                throw new IOException("Cross-volume Shell operation returned without the expected fixture result.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void CopyBetweenFixtures(string root, string sourceFolder, string sourceName,
        string destinationFolder, string destinationName)
    {
        root = Path.GetFullPath(root);
        sourceFolder = Path.GetFullPath(sourceFolder);
        destinationFolder = Path.GetFullPath(destinationFolder);
        if (!IsFixtureFolder(root, sourceFolder) || !IsFixtureFolder(root, destinationFolder) ||
            string.Equals(sourceFolder, destinationFolder, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourceName) != sourceName || Path.GetFileName(destinationName) != destinationName)
            throw new ArgumentException("Cross-pane copy paths must stay in two distinct generated fixture folders.");

        var sourcePath = Path.Combine(sourceFolder, sourceName);
        var destinationPath = Path.Combine(destinationFolder, destinationName);
        if ((File.GetAttributes(sourcePath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
            File.ReadAllText(sourcePath) != "initial" || File.Exists(destinationPath))
            throw new InvalidOperationException("Cross-pane copy fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            // FOF_SILENT | NOCONFIRMATION | NOCONFIRMMKDIR | NOERRORUI,
            // FOFX_EARLYFAILURE | NOCOPYHOOKS. No elevation/undo/recycle flags.
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var source = Item(sourcePath);
            var destination = Item(destinationFolder);
            try { Check(At<Copy>(operation, 16)(operation, source, destination, destinationName, 0)); }
            finally
            {
                Marshal.Release(destination);
                Marshal.Release(source);
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("Cross-pane Shell copy was aborted.");
            if (!File.Exists(sourcePath) || File.ReadAllText(sourcePath) != "initial" ||
                !File.Exists(destinationPath) || File.ReadAllText(destinationPath) != "initial")
                throw new IOException("Cross-pane Shell copy returned without the expected fixture result.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void CopyMany(string root, string folder, string sourceName, IReadOnlyList<string> destinationNames)
    {
        root = Path.GetFullPath(root);
        folder = Path.GetFullPath(folder);
        if (!IsFixtureFolder(root, folder) || Path.GetFileName(sourceName) != sourceName ||
            destinationNames.Count is < 2 or > 32 ||
            destinationNames.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) ||
            destinationNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinationNames.Count)
            throw new ArgumentException("Batch copy names must be distinct files inside the generated fixture folder.");

        var sourcePath = Path.Combine(folder, sourceName);
        if ((File.GetAttributes(sourcePath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
            File.ReadAllText(sourcePath) != "initial" ||
            destinationNames.Any(name => File.Exists(Path.Combine(folder, name)) || Directory.Exists(Path.Combine(folder, name))))
            throw new InvalidOperationException("Batch copy source or destination fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var source = Item(sourcePath);
            var destination = Item(folder);
            try
            {
                foreach (var name in destinationNames)
                    Check(At<Copy>(operation, 16)(operation, source, destination, name, 0));
            }
            finally
            {
                Marshal.Release(destination);
                Marshal.Release(source);
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || destinationNames.Any(name =>
                    !File.Exists(Path.Combine(folder, name)) || File.ReadAllText(Path.Combine(folder, name)) != "initial"))
                throw new IOException("Batch Shell copy returned without every expected fixture file.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void DeleteMany(string root, string folder, IReadOnlyList<string> names)
    {
        root = Path.GetFullPath(root);
        folder = Path.GetFullPath(folder);
        if (!IsFixtureFolder(root, folder) || names.Count is < 1 or > 32 ||
            names.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new ArgumentException("Batch delete names must be distinct files inside the generated fixture folder.");

        var paths = names.Select(name => Path.Combine(folder, name)).ToArray();
        if (paths.Any(path => !File.Exists(path) ||
                (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                File.ReadAllText(path) != "initial"))
            throw new InvalidOperationException("Batch Shell delete fixture was modified unexpectedly.");

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            var items = paths.Select(Item).ToArray();
            try
            {
                foreach (var item in items) Check(At<Delete>(operation, 18)(operation, item, 0));
            }
            finally
            {
                foreach (var item in items) Marshal.Release(item);
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0 || paths.Any(File.Exists))
                throw new IOException("Batch Shell delete returned before removing every fixture file.");
        }
        finally { Marshal.Release(operation); }
    }

    internal static void Run(string root, IReadOnlyList<string> folders, bool copy, string copyName = "created.txt",
        string? moveFrom = null, string? moveName = null, string? deleteFrom = null)
    {
        if (copy && (moveFrom is not null || deleteFrom is not null) ||
            moveFrom is not null && deleteFrom is not null ||
            (moveFrom is null) != (moveName is null) ||
            moveFrom is not null && (Path.GetFileName(moveFrom) != moveFrom || Path.GetFileName(moveName) != moveName) ||
            deleteFrom is not null && Path.GetFileName(deleteFrom) != deleteFrom)
            throw new ArgumentException("Choose one fixture operation with file-name-only source paths.");
        root = Path.GetFullPath(root);
        foreach (var folder in folders)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)), root, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Shell operation target is outside the generated fixture.");
            var initial = Path.Combine(folder, "initial.txt");
            if ((File.GetAttributes(initial) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                File.ReadAllText(initial) != "initial" || (copy && File.Exists(Path.Combine(folder, copyName))))
                throw new InvalidOperationException("Shell operation fixture was modified unexpectedly.");
            var operationSourceName = moveFrom ?? deleteFrom;
            if (operationSourceName is not null)
            {
                var source = Path.Combine(folder, operationSourceName);
                if ((File.GetAttributes(source) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                    File.ReadAllText(source) != "initial" ||
                    (moveFrom is not null && File.Exists(Path.Combine(folder, moveName!))))
                    throw new InvalidOperationException("Shell operation fixture was modified unexpectedly.");
            }
        }

        var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var iid = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out var operation));
        try
        {
            // FOF_SILENT | NOCONFIRMATION | NOCONFIRMMKDIR | NOERRORUI,
            // FOFX_EARLYFAILURE | NOCOPYHOOKS. No elevation/undo/recycle flags.
            Check(At<SetFlags>(operation, 5)(operation, 0x614 | 0x00100000 | 0x00800000));
            foreach (var folder in folders)
            {
                var sourceName = moveFrom ?? deleteFrom ?? "initial.txt";
                var item = Item(Path.Combine(folder, sourceName));
                try
                {
                    if (moveFrom is not null)
                    {
                        var destination = Item(folder);
                        try { Check(At<Move>(operation, 14)(operation, item, destination, moveName!, 0)); }
                        finally { Marshal.Release(destination); }
                    }
                    else if (copy)
                    {
                        var destination = Item(folder);
                        try { Check(At<Copy>(operation, 16)(operation, item, destination, copyName, 0)); }
                        finally { Marshal.Release(destination); }
                    }
                    else Check(At<Delete>(operation, 18)(operation, item, 0));
                }
                finally { Marshal.Release(item); }
            }
            Check(At<Perform>(operation, 21)(operation));
            Check(At<Aborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("Shell fixture operation was aborted.");
            foreach (var folder in folders)
                if (moveFrom is not null
                    ? File.Exists(Path.Combine(folder, moveFrom)) || File.ReadAllText(Path.Combine(folder, moveName!)) != "initial"
                    : deleteFrom is not null
                        ? File.Exists(Path.Combine(folder, deleteFrom)) || !File.Exists(Path.Combine(folder, "initial.txt"))
                        : File.Exists(Path.Combine(folder, "initial.txt")) != copy ||
                          (copy && File.ReadAllText(Path.Combine(folder, copyName)) != "initial"))
                    throw new IOException("Shell operation returned without the expected fixture result.");
        }
        finally { Marshal.Release(operation); }
    }

    private static nint Item(string path)
    {
        var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        Check(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        return item;
    }

    private static bool IsFixtureFolder(string root, string folder) =>
        string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase) &&
        (File.GetAttributes(folder) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == FileAttributes.Directory;

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Copy(nint self, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Move(nint self, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Delete(nint self, nint item, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int NewItem(nint self, nint destination, uint attributes,
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string? templateName, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Perform(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Aborted(nint self, out int aborted);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, out nint item);
}
