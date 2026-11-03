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
            VerifyOwnedRegistration(classId, adapterPath, profileRoot);
            var recoveredOwner = ProfileShellNamespaceOwner.Start(classId, profileRoot, adapterPath);
            VerifyOwnedRegistration(classId, adapterPath, profileRoot);
            recoveredOwner.Dispose();
            firstOwner.Dispose();
            VerifyRegistrationAbsent(classId);

            var editedId = Guid.NewGuid();
            var editedOwner = ProfileShellNamespaceOwner.Start(editedId, profileRoot, adapterPath);
            using (var editedClass = Registry.CurrentUser.OpenSubKey(ClassKeyPath(editedId), writable: true)!)
                editedClass.SetValue("ExplorerDirsProfileRoot", profileRoot + "-external", RegistryValueKind.String);
            try
            {
                var refused = false;
                try { editedOwner.Dispose(); }
                catch (InvalidOperationException error) when (error.Message.Contains("changed ownership", StringComparison.Ordinal)) { refused = true; }
                if (!refused) throw new InvalidOperationException("External runtime-root metadata was not protected during cleanup.");
            }
            finally
            {
                using var editedClass = Registry.CurrentUser.OpenSubKey(ClassKeyPath(editedId), writable: true);
                editedClass?.SetValue("ExplorerDirsProfileRoot", profileRoot, RegistryValueKind.String);
            }
            var inprocPath = $"{ClassKeyPath(editedId)}\\InprocServer32";
            using (var inproc = Registry.CurrentUser.OpenSubKey(inprocPath, writable: true)
                   ?? throw new InvalidOperationException("Could not open the owned InprocServer32 fixture."))
                inproc.SetValue("ThreadingModel", "ExternalEdit", RegistryValueKind.String);
            var refusedModifiedValueCleanup = false;
            try { editedOwner.Dispose(); }
            catch (InvalidOperationException exception) when (exception.Message.Contains("changed ownership", StringComparison.Ordinal))
            { refusedModifiedValueCleanup = true; }
            if (!refusedModifiedValueCleanup)
                throw new InvalidOperationException("Profile namespace cleanup removed an externally modified owned value.");
            using (var inproc = Registry.CurrentUser.OpenSubKey(inprocPath))
            {
                if (inproc?.GetValue("ThreadingModel") as string != "ExternalEdit")
                    throw new InvalidOperationException("The modified InprocServer32 value was not preserved.");
            }
            using (var inproc = Registry.CurrentUser.OpenSubKey(inprocPath, writable: true))
                inproc?.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);

            using (var editedClass = Registry.CurrentUser.OpenSubKey(ClassKeyPath(editedId), writable: true)
                   ?? throw new InvalidOperationException("Could not open the owned CLSID fixture."))
            {
                editedClass.SetValue("ExternalValue", "keep-me", RegistryValueKind.String);
                using var externalChild = editedClass.CreateSubKey("ExternalChild",
                    RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                    ?? throw new InvalidOperationException("Could not create the external CLSID child fixture.");
                externalChild.SetValue(null, "external-child-data", RegistryValueKind.String);
            }
            var refusedExternalEditCleanup = false;
            try { editedOwner.Dispose(); }
            catch (InvalidOperationException exception) when (exception.Message.Contains("changed ownership", StringComparison.Ordinal))
            { refusedExternalEditCleanup = true; }
            if (!refusedExternalEditCleanup)
                throw new InvalidOperationException("Profile namespace cleanup removed externally added CLSID values or subkeys.");
            using (var preservedClass = Registry.CurrentUser.OpenSubKey(ClassKeyPath(editedId)))
            using (var preservedChild = preservedClass?.OpenSubKey("ExternalChild"))
            {
                if (preservedClass?.GetValue("ExternalValue") as string != "keep-me" ||
                    preservedChild?.GetValue(null) as string != "external-child-data")
                    throw new InvalidOperationException("The external CLSID values or child key were not preserved.");
            }
            using (var editedClass = Registry.CurrentUser.OpenSubKey(ClassKeyPath(editedId), writable: true))
            {
                editedClass?.DeleteValue("ExternalValue", throwOnMissingValue: false);
                editedClass?.DeleteSubKeyTree("ExternalChild", throwOnMissingSubKey: false);
            }
            editedOwner.Dispose();
            VerifyRegistrationAbsent(editedId);

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

    private static void VerifyOwnedRegistration(Guid classId, string adapterPath, string profileRoot)
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
            namespaceKey?.GetValue(null) as string != OwnerName ||
            !string.Equals(classKey?.GetValue("ExplorerDirsProfileRoot") as string, profileRoot, StringComparison.OrdinalIgnoreCase))
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
