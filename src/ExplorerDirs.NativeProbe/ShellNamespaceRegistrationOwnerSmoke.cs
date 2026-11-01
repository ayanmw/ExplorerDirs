using ExplorerDirs.App;
using Microsoft.Win32;

internal static class ShellNamespaceRegistrationOwnerSmoke
{
    private const string OwnerName = "Explorer Dirs profile Shell namespace";

    internal static int Run()
    {
        var profileRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-NamespaceOwner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profileRoot);
        var classId = Guid.NewGuid();
        var adapterPath = FindProductAdapter();
        try
        {
            var firstOwner = ProfileShellNamespaceOwner.Start(classId, profileRoot, adapterPath);
            VerifyOwnedRegistration(classId, adapterPath);
            var recoveredOwner = ProfileShellNamespaceOwner.Start(classId, profileRoot, adapterPath);
            VerifyOwnedRegistration(classId, adapterPath);
            recoveredOwner.Dispose();
            firstOwner.Dispose();
            VerifyRegistrationAbsent(classId);

            var conflictId = Guid.NewGuid();
            var foreignNamespacePath = NamespaceKeyPath(conflictId);
            using (var foreignNamespace = Registry.CurrentUser.CreateSubKey(foreignNamespacePath)
                   ?? throw new InvalidOperationException("Could not create the foreign registration fixture."))
                foreignNamespace.SetValue(null, "External owner", RegistryValueKind.String);
            try
            {
                try
                {
                    using var unexpectedOwner = ProfileShellNamespaceOwner.Start(conflictId, profileRoot, adapterPath);
                    throw new InvalidOperationException("The profile owner overwrote an external namespace registration.");
                }
                catch (InvalidOperationException exception) when (exception.Message.StartsWith(
                           "Refusing to overwrite an unowned Shell registration", StringComparison.Ordinal)) { }
                using var preservedNamespace = Registry.CurrentUser.OpenSubKey(foreignNamespacePath);
                if (preservedNamespace?.GetValue(null) as string != "External owner")
                    throw new InvalidOperationException("The external registration fixture was not preserved.");
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(foreignNamespacePath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(ClassKeyPath(conflictId), throwOnMissingSubKey: false);
            }

            Console.WriteLine("profile Shell namespace registration owner smoke: PASS (create, stale-owned recovery, cleanup, external conflict preserved; no Shell view created)");
            return 0;
        }
        finally
        {
            VerifyRegistrationAbsent(classId);
            Directory.Delete(profileRoot, recursive: true);
        }
    }

    private static void VerifyOwnedRegistration(Guid classId, string adapterPath)
    {
        using var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId));
        using var inproc = classKey?.OpenSubKey("InprocServer32");
        using var shellFolder = classKey?.OpenSubKey("ShellFolder");
        using var namespaceKey = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId));
        if (classKey?.GetValue(null) as string != OwnerName ||
            !string.Equals(classKey?.GetValue("ExplorerDirsAdapterPath") as string, adapterPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inproc?.GetValue(null) as string, adapterPath, StringComparison.OrdinalIgnoreCase) ||
            inproc?.GetValue("ThreadingModel") as string != "Apartment" ||
            shellFolder?.GetValue("Attributes") is not int attributes || attributes != unchecked((int)0xA0100000) ||
            namespaceKey?.GetValue(null) as string != OwnerName)
            throw new InvalidOperationException("The profile Shell registration did not match its owned values.");
    }

    private static void VerifyRegistrationAbsent(Guid classId)
    {
        if (Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId)) is not null ||
            Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId)) is not null)
            throw new InvalidOperationException("The profile Shell namespace registration was not cleaned up.");
    }

    private static string FindProductAdapter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-x64",
                "ExplorerDirs.ShellNamespace.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build the product Shell namespace adapter before running this smoke.");
    }

    private static string ClassKeyPath(Guid classId) => $@"Software\Classes\CLSID\{{{classId:D}}}";
    private static string NamespaceKeyPath(Guid classId) =>
        $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
}
