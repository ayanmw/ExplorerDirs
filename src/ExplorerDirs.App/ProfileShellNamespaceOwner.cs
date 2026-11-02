using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ExplorerDirs.App;

/// <summary>
/// Owns the per-profile volatile registration used to activate the native Shell folder.
/// The caller must hold the profile single-instance lock for the full lifetime.
/// </summary>
internal sealed class ProfileShellNamespaceOwner : IDisposable
{
    private const string OwnerName = "Explorer Dirs profile Shell namespace";
    private const string AdapterPathValue = "ExplorerDirsAdapterPath";
    private const int ShellFolderAttributes = unchecked((int)0xA0100000);
    private readonly Guid _classId;
    private readonly string _adapterPath;
    private nint _adapterModule;
    private FolderSizeNativeBridge? _folderSizeBridge;

    private ProfileShellNamespaceOwner(Guid classId, string adapterPath, nint adapterModule)
    {
        _classId = classId;
        _adapterPath = adapterPath;
        _adapterModule = adapterModule;
    }

    public static ProfileShellNamespaceOwner Start(Guid classId, string profileDirectory, string adapterPath)
    {
        if (classId == Guid.Empty) throw new ArgumentException("A non-empty profile CLSID is required.", nameof(classId));
        var root = Path.GetFullPath(profileDirectory);
        var library = Path.GetFullPath(adapterPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (!File.Exists(library)) throw new FileNotFoundException("The native Shell namespace adapter is missing.", library);

        var module = NativeLibrary.Load(library);
        try
        {
            var configure = Marshal.GetDelegateForFunctionPointer<ConfigureShellNamespaceRoot>(
                NativeLibrary.GetExport(module, "ConfigureShellNamespaceRoot"));
            Marshal.ThrowExceptionForHR(configure(ref classId, root));
            RegisterVolatileNamespace(classId, library);
            return new ProfileShellNamespaceOwner(classId, library, module);
        }
        catch
        {
            NativeLibrary.Free(module);
            throw;
        }
    }

    public void Dispose()
    {
        var module = Volatile.Read(ref _adapterModule);
        if (module == 0) return;
        // Retire managed callbacks even if external registration edits prevent cleanup;
        // otherwise an abandoned owner could leave native pointers to collected delegates.
        _folderSizeBridge?.Dispose();
        _folderSizeBridge = null;
        // Keep the module owner retryable if an external registry edit makes cleanup unsafe.
        DeleteOwnedRegistration(_classId, _adapterPath);
        module = Interlocked.Exchange(ref _adapterModule, 0);
        if (module != 0) NativeLibrary.Free(module);
    }

    internal void SetFolderSizeSource(FolderSizeSource source)
    {
        var module = Volatile.Read(ref _adapterModule);
        ObjectDisposedException.ThrowIf(module == 0, this);
        if (_folderSizeBridge is null) _folderSizeBridge = new FolderSizeNativeBridge(module, source);
        else _folderSizeBridge.SetSource(source);
    }

    private static void RegisterVolatileNamespace(Guid classId, string adapterPath)
    {
        if (HasRegistration(classId))
        {
            if (!HasOwnedRegistration(classId, adapterPath))
                throw new InvalidOperationException($"Refusing to overwrite an unowned Shell registration for profile {classId:D}.");
            DeleteOwnedRegistration(classId, adapterPath);
        }

        try
        {
            using (var clsid = Registry.CurrentUser.CreateSubKey(ClassKeyPath(classId),
                       RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                   ?? throw new InvalidOperationException("Could not create the profile Shell CLSID key."))
            {
                clsid.SetValue(null, OwnerName, RegistryValueKind.String);
                clsid.SetValue(AdapterPathValue, adapterPath, RegistryValueKind.String);
                using var inproc = clsid.CreateSubKey("InprocServer32",
                    RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                    ?? throw new InvalidOperationException("Could not create the profile Shell InprocServer32 key.");
                inproc.SetValue(null, adapterPath, RegistryValueKind.String);
                inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                using var shellFolder = clsid.CreateSubKey("ShellFolder",
                    RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                    ?? throw new InvalidOperationException("Could not create the profile ShellFolder key.");
                shellFolder.SetValue("Attributes", ShellFolderAttributes, RegistryValueKind.DWord);
            }

            using var nameSpace = Registry.CurrentUser.CreateSubKey(NamespaceKeyPath(classId),
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the profile Desktop namespace key.");
            nameSpace.SetValue(null, OwnerName, RegistryValueKind.String);
        }
        catch
        {
            DeleteOwnedRegistration(classId, adapterPath);
            throw;
        }
    }

    private static bool HasRegistration(Guid classId)
    {
        using var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId));
        using var nameSpace = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId));
        return classKey is not null || nameSpace is not null;
    }

    private static bool HasOwnedRegistration(Guid classId, string adapterPath)
    {
        using var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId));
        using var inproc = classKey?.OpenSubKey("InprocServer32");
        using var shellFolder = classKey?.OpenSubKey("ShellFolder");
        using var nameSpace = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId));
        if (classKey is null && nameSpace is null) return false;
        var classOwned = classKey is null || classKey.GetValue(null) is string classOwner && classOwner == OwnerName &&
                         classKey.GetValue(AdapterPathValue) is string registeredAdapter && IsSamePath(registeredAdapter, adapterPath) &&
                         HasOnlyRegistryContent(classKey, [string.Empty, AdapterPathValue], ["InprocServer32", "ShellFolder"]) &&
                         (inproc is null || inproc.GetValue(null) is string registeredPath && IsSamePath(registeredPath, adapterPath) &&
                             inproc.GetValue("ThreadingModel") as string == "Apartment" &&
                             HasOnlyRegistryContent(inproc, [string.Empty, "ThreadingModel"], [])) &&
                         (shellFolder is null || shellFolder.GetValue("Attributes") is int attributes &&
                             attributes == ShellFolderAttributes &&
                             HasOnlyRegistryContent(shellFolder, ["Attributes"], []));
        var namespaceOwned = nameSpace is null || nameSpace.GetValue(null) is string namespaceOwner && namespaceOwner == OwnerName &&
                             HasOnlyRegistryContent(nameSpace, [string.Empty], []);
        return classOwned && namespaceOwned;
    }

    private static void DeleteOwnedRegistration(Guid classId, string adapterPath)
    {
        using var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId), writable: true);
        using var nameSpace = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId), writable: true);
        if (classKey is null && nameSpace is null) return;
        if (!HasOwnedRegistration(classId, adapterPath))
            throw new InvalidOperationException($"The profile Shell registration for {classId:D} changed ownership; it was left untouched.");

        if (classKey is not null)
        {
            if (classKey.GetSubKeyNames().Contains("InprocServer32", StringComparer.OrdinalIgnoreCase))
                classKey.DeleteSubKey("InprocServer32", throwOnMissingSubKey: false);
            if (classKey.GetSubKeyNames().Contains("ShellFolder", StringComparer.OrdinalIgnoreCase))
                classKey.DeleteSubKey("ShellFolder", throwOnMissingSubKey: false);
            classKey.DeleteValue(AdapterPathValue, throwOnMissingValue: false);
            classKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            if (classKey.ValueCount == 0 && classKey.SubKeyCount == 0)
                Registry.CurrentUser.DeleteSubKey(ClassKeyPath(classId), throwOnMissingSubKey: false);
        }
        if (nameSpace is not null)
        {
            nameSpace.DeleteValue(string.Empty, throwOnMissingValue: false);
            if (nameSpace.ValueCount == 0 && nameSpace.SubKeyCount == 0)
                Registry.CurrentUser.DeleteSubKey(NamespaceKeyPath(classId), throwOnMissingSubKey: false);
        }
    }

    private static bool HasOnlyRegistryContent(RegistryKey key, string[] valueNames, string[] subKeyNames) =>
        key.GetValueNames().All(name => valueNames.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
        key.GetSubKeyNames().All(name => subKeyNames.Contains(name, StringComparer.OrdinalIgnoreCase));

    private static bool IsSamePath(string first, string second)
    {
        try { return Path.GetFullPath(first).Equals(second, StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
    }

    private static string ClassKeyPath(Guid classId) => $@"Software\Classes\CLSID\{{{classId:D}}}";
    private static string NamespaceKeyPath(Guid classId) =>
        $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ConfigureShellNamespaceRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string path);
}
