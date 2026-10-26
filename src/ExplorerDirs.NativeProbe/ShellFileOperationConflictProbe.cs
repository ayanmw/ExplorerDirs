using System.IO;
using System.Runtime.InteropServices;

// Exercises native copy collision behavior using only a fresh temporary fixture.
// It creates no ExplorerBrowser and never displays progress or confirmation UI.
internal static class ShellFileOperationConflictProbe
{
    private const uint Silent = 0x0004;
    private const uint NoConfirmation = 0x0010;
    private const uint NoErrorUi = 0x0400;
    private const uint RenameOnCollision = 0x0008;
    private const uint EarlyFailure = 0x00100000;
    private const uint NoCopyHooks = 0x00800000;

    internal static int Run()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NativeProbe", Guid.NewGuid().ToString("N"));
        var sourceFolder = Path.Combine(fixtureRoot, "source");
        var moveSourceFolder = Path.Combine(fixtureRoot, "move-source");
        var destinationFolder = Path.Combine(fixtureRoot, "destination");
        nint operation = 0;
        try
        {
            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(moveSourceFolder);
            Directory.CreateDirectory(destinationFolder);
            var sourcePath = Path.Combine(sourceFolder, "copy-target.txt");
            var destinationPath = Path.Combine(destinationFolder, "copy-target.txt");
            var moveSourcePath = Path.Combine(moveSourceFolder, "move-target.txt");
            var moveDestinationPath = Path.Combine(destinationFolder, "move-target.txt");
            File.WriteAllText(sourcePath, "source-content");
            File.WriteAllText(destinationPath, "existing-destination");
            File.WriteAllText(moveSourcePath, "move-source-content");
            File.WriteAllText(moveDestinationPath, "existing-move-destination");

            var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Check(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out operation));
            Check(At<SetOperationFlags>(operation, 5)(operation,
                Silent | NoConfirmation | NoErrorUi | RenameOnCollision | EarlyFailure | NoCopyHooks));

            var source = CreateItem(sourcePath);
            var moveSource = CreateItem(moveSourcePath);
            var destination = CreateItem(destinationFolder);
            try
            {
                Check(At<CopyItem>(operation, 16)(operation, source, destination, "copy-target.txt", 0));
                Check(At<MoveItem>(operation, 14)(operation, moveSource, destination, "move-target.txt", 0));
            }
            finally
            {
                Marshal.Release(destination);
                Marshal.Release(moveSource);
                Marshal.Release(source);
            }

            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            var copied = Directory.EnumerateFiles(destinationFolder)
                .Where(path => Path.GetFileName(path).StartsWith("copy-target", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path, destinationPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var moved = Directory.EnumerateFiles(destinationFolder)
                .Where(path => Path.GetFileName(path).StartsWith("move-target", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path, moveDestinationPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (aborted != 0 || File.ReadAllText(destinationPath) != "existing-destination" ||
                copied.Length != 1 || File.ReadAllText(copied[0]) != "source-content" ||
                File.ReadAllText(sourcePath) != "source-content" ||
                File.ReadAllText(moveDestinationPath) != "existing-move-destination" ||
                moved.Length != 1 || File.ReadAllText(moved[0]) != "move-source-content" ||
                File.Exists(moveSourcePath))
                throw new IOException("Shell copy/move collision did not preserve existing destinations and produce the expected renamed items.");

            Console.WriteLine("file-operation-collision: copy-existing-preserved=True; renamed-copy=True; copy-source-preserved=True; move-existing-preserved=True; renamed-move=True; move-source-removed=True; ui-shown=False");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"file-operation-collision: {exception.GetType().Name}");
            return 1;
        }
        finally
        {
            if (operation != 0) Marshal.Release(operation);
            CleanupFixture(fixtureRoot);
        }
    }

    private static nint CreateItem(string path)
    {
        var interfaceId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        Check(SHCreateItemFromParsingName(path, 0, ref interfaceId, out var item));
        return item;
    }

    private static void CleanupFixture(string fixtureRoot)
    {
        var basePath = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NativeProbe");
        if (!string.Equals(Path.GetDirectoryName(fixtureRoot), basePath, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(fixtureRoot), "N", out _) ||
            !Directory.Exists(fixtureRoot) ||
            (File.GetAttributes(fixtureRoot) & FileAttributes.ReparsePoint) != 0)
            return;

        Directory.Delete(fixtureRoot, recursive: true);
        if (Directory.Exists(basePath) &&
            (File.GetAttributes(basePath) & FileAttributes.ReparsePoint) == 0 &&
            !Directory.EnumerateFileSystemEntries(basePath).Any())
            Directory.Delete(basePath);
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int CopyItem(nint self, nint item, nint destination,
        [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int MoveItem(nint self, nint item, nint destination,
        [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context,
        ref Guid interfaceId, out nint instance);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext,
        ref Guid interfaceId, out nint item);
}
