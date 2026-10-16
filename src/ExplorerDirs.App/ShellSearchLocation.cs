using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// A search-ms URI remains a Shell location. It must never be passed through
// Path.GetFullPath or saved as a file-system tab path.
internal static class ShellSearchLocation
{
    private const uint DesktopAbsoluteParsing = 0x80028000;

    public static string BuildUri(string scopePath, string query)
    {
        if (string.IsNullOrWhiteSpace(scopePath) || !Path.IsPathFullyQualified(scopePath))
            throw new ArgumentException("Search scope must be a full directory path.", nameof(scopePath));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));

        return "search-ms:query=" + Uri.EscapeDataString(query.Trim()) +
               "&crumb=location:" + Uri.EscapeDataString(scopePath);
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
        return query.Length > 0 && Path.IsPathFullyQualified(scopePath);
    }

    public static IntPtr ParsePidl(string uri)
    {
        var hr = SHParseDisplayName(uri, IntPtr.Zero, out var pidl, 0, out _);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        if (pidl == IntPtr.Zero) throw new COMException("Shell returned no search location.");
        return pidl;
    }

    public static bool IsSearchPidl(IntPtr pidl) =>
        TryGetParsingName(pidl, out var name) &&
        name.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase);

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
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl,
        uint attributes, out uint receivedAttributes);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint displayNameType, out IntPtr name);
}
