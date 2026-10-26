using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

namespace ExplorerDirs.App;

// A search-ms URI is runtime request metadata; parse it as a native folder,
// not a protocol launch item. Never save it as the tab's file-system path.
internal static class ShellSearchLocation
{
    private const uint DesktopAbsoluteParsing = 0x80028000;

    public static string BuildUri(string scopePath, string query)
    {
        if (string.IsNullOrWhiteSpace(scopePath) || !IsSupportedScope(scopePath))
            throw new ArgumentException("Search scope must be a filesystem path or supported Shell folder identity.", nameof(scopePath));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));

        return "search-ms:query=" + Uri.EscapeDataString(query.Trim()) +
               "&crumb=location:" + Uri.EscapeDataString(scopePath) +
               "&displayname=" + Uri.EscapeDataString(query.Trim() + " — " + scopePath);
    }

    public static bool TryReadScopeAndQuery(string? uri, out string scopePath, out string query)
    {
        scopePath = string.Empty;
        query = string.Empty;
        if (uri is null || !uri.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var argument in uri["search-ms:".Length..].Split('&'))
        {
            if (argument.StartsWith("query=", StringComparison.OrdinalIgnoreCase))
                query = Uri.UnescapeDataString(argument["query=".Length..]);
            else if (argument.StartsWith("crumb=location:", StringComparison.OrdinalIgnoreCase))
                scopePath = Uri.UnescapeDataString(argument["crumb=location:".Length..]);
        }
        return query.Length > 0 && IsSupportedScope(scopePath);
    }

    private static bool IsSupportedScope(string scope) => Path.IsPathFullyQualified(scope) ||
        (ShellLocationIdentity.TryNormalize(scope, out _, out var isFileSystemPath) && !isFileSystemPath);

    public static IntPtr ParsePidl(string uri)
    {
        if (!TryReadScopeAndQuery(uri, out _, out _))
            throw new ArgumentException("Expected a scoped search URI.", nameof(uri));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out var context));
        nint pidl = 0;
        try
        {
            // Without this documented bind option, search-ms resolves to a protocol
            // launch item on this Windows build and BrowseToIDList opens Explorer.
            context.RegisterObjectParam("Parse Prefer Folder Browsing", new object());
            Marshal.ThrowExceptionForHR(SHParseDisplayName(uri, context, out pidl, 0, out _));
            if (pidl == 0 || !IsFolder(pidl))
                throw new COMException("Shell did not return an embeddable search folder.");
            var result = pidl;
            pidl = 0;
            return result;
        }
        finally
        {
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            try { context.RevokeObjectParam("Parse Prefer Folder Browsing"); }
            finally { Marshal.ReleaseComObject(context); }
        }
    }

    private static bool IsFolder(nint pidl)
    {
        var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        Marshal.ThrowExceptionForHR(SHCreateItemFromIDList(pidl, ref iid, out var item));
        try
        {
            var read = Marshal.GetDelegateForFunctionPointer<GetAttributes>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), 6 * nint.Size));
            return read(item, 0x20000000, out var attributes) == 0 && (attributes & 0x20000000) != 0;
        }
        finally { Marshal.Release(item); }
    }

    public static bool TryGetParsingName(IntPtr pidl, out string name)
    {
        name = string.Empty;
        if (pidl == IntPtr.Zero || SHGetNameFromIDList(pidl, DesktopAbsoluteParsing, out var pointer) < 0)
            return false;
        try { name = Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { Marshal.FreeCoTaskMem(pointer); }
        return name.Length > 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IBindCtx bindContext, out IntPtr pidl,
        uint attributes, out uint receivedAttributes);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(uint reserved, out IBindCtx context);
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(nint pidl, ref Guid iid, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributes(nint self, uint mask, out uint attributes);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint displayNameType, out IntPtr name);
}

// Per-browser runtime metadata. Default search captions (and ILIsEqual) can alias
// different queries, while Shell normalizes PIDL bytes during navigation. BuildUri
// assigns query-and-scope captions; retain only exact names issued by this browser,
// reject any collision, and keep raw identities as the first lookup path.
// No native pointers or search locations are persisted in the portable session.
internal sealed class ShellSearchHistory
{
    private readonly Dictionary<string, string> locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> namedLocations = new(StringComparer.Ordinal);

    internal void Remember(nint pidl, string uri)
    {
        if (!ShellSearchLocation.TryReadScopeAndQuery(uri, out _, out _))
            throw new ArgumentException("Expected a scoped search URI.", nameof(uri));
        if (!ShellSearchLocation.TryGetParsingName(pidl, out var name) || string.IsNullOrEmpty(name))
            throw new COMException("Search folder has no restorable native name.");
        if (namedLocations.TryGetValue(name, out var previous) && previous != uri)
            throw new COMException("Windows returned the same search identity for different queries.");
        namedLocations[name] = uri;
        locations[Identity(pidl)] = uri;
    }

    internal void Clear()
    {
        locations.Clear();
        namedLocations.Clear();
    }

    internal bool TryResolve(nint pidl, out string uri)
    {
        uri = string.Empty;
        if (pidl == 0) return false;
        if (locations.TryGetValue(Identity(pidl), out uri!)) return true;
        return ShellSearchLocation.TryGetParsingName(pidl, out var name) &&
            namedLocations.TryGetValue(name, out uri!);
    }

    private static string Identity(nint pidl)
    {
        if (pidl == 0) throw new ArgumentException("Search PIDL is null.", nameof(pidl));
        var size = ILGetSize(pidl);
        if (size is < 2 or > 1048576) throw new COMException("Unexpected search PIDL size.");
        var bytes = new byte[size];
        Marshal.Copy(pidl, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
}
