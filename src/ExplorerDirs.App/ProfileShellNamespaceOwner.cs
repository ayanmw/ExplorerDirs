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
        var module = Interlocked.Exchange(ref _adapterModule, 0);
        if (module == 0) return;
        try { DeleteOwnedRegistration(_classId, _adapterPath); }
        finally { NativeLibrary.Free(module); }
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
        using var nameSpace = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId));
        if (classKey is null && nameSpace is null) return false;
        var classOwned = classKey is null || classKey.GetValue(null) is string classOwner && classOwner == OwnerName &&
                         classKey.GetValue(AdapterPathValue) is string registeredAdapter && IsSamePath(registeredAdapter, adapterPath) &&
                         (inproc is null || inproc.GetValue(null) is string registeredPath && IsSamePath(registeredPath, adapterPath));
        var namespaceOwned = nameSpace is null || nameSpace.GetValue(null) is string namespaceOwner && namespaceOwner == OwnerName;
        return classOwned && namespaceOwned;
    }

    private static void DeleteOwnedRegistration(Guid classId, string adapterPath)
    {
        using var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId));
        using var inproc = classKey?.OpenSubKey("InprocServer32");
        using var nameSpace = Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId));
        if (classKey is null && nameSpace is null) return;

        var classOwned = classKey?.GetValue(null) is string classOwner && classOwner == OwnerName &&
                         classKey.GetValue(AdapterPathValue) is string registeredAdapter && IsSamePath(registeredAdapter, adapterPath) &&
                         (inproc is null || inproc.GetValue(null) is string registeredPath && IsSamePath(registeredPath, adapterPath));
        var namespaceOwned = nameSpace?.GetValue(null) is string namespaceOwner && namespaceOwner == OwnerName;
        if ((classKey is not null && !classOwned) || (nameSpace is not null && !namespaceOwned))
            throw new InvalidOperationException($"The profile Shell registration for {classId:D} changed ownership; it was left untouched.");
        if (nameSpace is not null)
            Registry.CurrentUser.DeleteSubKeyTree(NamespaceKeyPath(classId), throwOnMissingSubKey: false);
        if (classKey is not null)
            Registry.CurrentUser.DeleteSubKeyTree(ClassKeyPath(classId), throwOnMissingSubKey: false);
    }

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
