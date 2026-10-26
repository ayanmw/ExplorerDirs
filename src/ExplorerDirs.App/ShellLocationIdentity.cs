using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Session locations are filesystem paths or Shell parsing names. Native Shell
// resolution and SFGAO_FOLDER checks decide whether a namespace name is usable.
internal static class ShellLocationIdentity
{
    private const uint FolderAttribute = 0x20000000;
    private const uint FileSystemAttribute = 0x40000000;
    private const uint DesktopAbsoluteParsing = 0x80028000;
    private const uint FileSystemPath = 0x80058000;
    private const uint NormalDisplayName = 0;
    private static readonly (string Input, string Canonical)[] Aliases =
    [
        ("shell:MyComputerFolder", "shell:MyComputerFolder"),
        ("shell:RecycleBinFolder", "shell:RecycleBinFolder"),
        ("shell:Libraries", "shell:Libraries"),
        ("shell:Desktop", "shell:Desktop")
    ];
    private static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    internal static bool TryNormalize(string value, out string identity, out bool isFileSystemPath)
    {
        identity = string.Empty;
        isFileSystemPath = false;
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n', '\0']) >= 0) return false;

        var trimmed = value.Trim().Trim('"');
        foreach (var (input, canonical) in Aliases)
        {
            if (!trimmed.Equals(input, StringComparison.OrdinalIgnoreCase)) continue;
            identity = canonical;
            return true;
        }

        // SIGDN_DESKTOPABSOLUTEPARSING names for virtual Shell folders commonly
        // start with "::". Keep the spelling and let SHParseDisplayName plus
        // SFGAO_FOLDER validation reject missing providers and non-folders.
        if (IsShellAbsoluteParsingName(trimmed))
        {
            identity = trimmed;
            return true;
        }

        if (!Path.IsPathFullyQualified(trimmed)) return false;
        try { identity = Path.GetFullPath(trimmed); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return false; }
        isFileSystemPath = true;
        return true;
    }

    internal static string GetDisplayName(string identity)
    {
        foreach (var (alias, canonical) in Aliases)
        {
            if (!identity.Equals(canonical, StringComparison.OrdinalIgnoreCase)) continue;
            return canonical switch
            {
                "shell:MyComputerFolder" => "此电脑",
                "shell:RecycleBinFolder" => "回收站",
                "shell:Libraries" => "库",
                "shell:Desktop" => "桌面",
                _ => identity
            };
        }
        if (IsShellAbsoluteParsingName(identity) && TryGetFolderPidl(identity, out var pidl))
        {
            try
            {
                if (SHGetNameFromIDList(pidl, NormalDisplayName, out var displayName) >= 0 && displayName != 0)
                {
                    try
                    {
                        var value = Marshal.PtrToStringUni(displayName);
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                    finally { Marshal.FreeCoTaskMem(displayName); }
                }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(identity));
    }

    // Returns a caller-owned absolute PIDL after Shell confirms it represents a folder.
    internal static bool TryGetFolderPidl(string identity, out nint pidl)
    {
        pidl = 0;
        if (!TryNormalize(identity, out var normalized, out _)) return false;
        var result = SHParseDisplayName(normalized, IntPtr.Zero, out var parsed, 0, out _);
        if (result < 0 || parsed == 0)
        {
            if (parsed != 0) Marshal.FreeCoTaskMem(parsed);
            return false;
        }

        if (!TryGetAttributes(parsed, out var attributes) || (attributes & FolderAttribute) == 0 ||
            (!IsShellAbsoluteParsingName(normalized) &&
             !IsZipParsingPath(normalized) && (attributes & FileSystemAttribute) == 0))
        {
            Marshal.FreeCoTaskMem(parsed);
            return false;
        }
        pidl = parsed;
        return true;
    }

    // Returns a confirmed filesystem directory or a supported namespace identity.
    internal static bool TryGetPersistedIdentity(nint pidl, out string identity, out bool isFileSystemPath)
    {
        identity = string.Empty;
        isFileSystemPath = false;
        if (pidl == 0 || !TryGetAttributes(pidl, out var attributes) || (attributes & FolderAttribute) == 0)
            return false;

        if ((attributes & FileSystemAttribute) != 0 &&
            SHGetNameFromIDList(pidl, FileSystemPath, out var filesystemName) >= 0 && filesystemName != 0)
        {
            try
            {
                var path = Marshal.PtrToStringUni(filesystemName);
                // A directory may legitimately have a .zip suffix. Treat the
                // name as a namespace candidate only when it is not an actual
                // filesystem directory; ZIP archives themselves are files.
                if (!string.IsNullOrWhiteSpace(path) &&
                    (!IsZipParsingPath(path) || Directory.Exists(path)) &&
                    Path.IsPathFullyQualified(path))
                {
                    identity = path;
                    isFileSystemPath = true;
                    return true;
                }
            }
            finally { Marshal.FreeCoTaskMem(filesystemName); }
        }

        if (TryMatchAlias(pidl, out identity)) return true;
        if (SHGetNameFromIDList(pidl, DesktopAbsoluteParsing, out var parsingName) < 0 || parsingName == 0)
            return false;
        try
        {
            var value = Marshal.PtrToStringUni(parsingName);
            if (string.IsNullOrWhiteSpace(value) || !TryNormalize(value, out var normalized, out _))
                return false;
            if (!IsShellAbsoluteParsingName(normalized) && !IsZipParsingPath(normalized))
                return false;
            identity = normalized;
            return true;
        }
        finally { Marshal.FreeCoTaskMem(parsingName); }
    }

    internal static bool IsZipParsingPath(string value)
    {
        if (!Path.IsPathFullyQualified(value)) return false;
        var start = 0;
        while ((start = value.IndexOf(".zip", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + 4;
            if (end == value.Length || value[end] is '\\' or '/') return true;
            start = end;
        }
        return false;
    }

    private static bool IsShellAbsoluteParsingName(string value) =>
        value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("::", StringComparison.Ordinal);

    private static bool TryMatchAlias(nint pidl, out string identity)
    {
        identity = string.Empty;
        foreach (var (alias, canonical) in Aliases)
        {
            if (SHParseDisplayName(canonical, IntPtr.Zero, out var knownPidl, 0, out _) < 0 || knownPidl == 0)
            {
                if (knownPidl != 0) Marshal.FreeCoTaskMem(knownPidl);
                continue;
            }
            try
            {
                if (ILIsEqual(pidl, knownPidl))
                {
                    identity = canonical;
                    return true;
                }
            }
            finally { Marshal.FreeCoTaskMem(knownPidl); }
        }
        return false;
    }

    private static bool TryGetAttributes(nint pidl, out uint attributes)
    {
        attributes = 0;
        if (SHCreateItemFromIDList(pidl, in ShellItemId, out var item) < 0 || item == 0) return false;
        try
        {
            var getAttributes = Marshal.GetDelegateForFunctionPointer<GetAttributes>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), 6 * nint.Size));
            return getAttributes(item, FolderAttribute | FileSystemAttribute, out attributes) >= 0;
        }
        finally { Marshal.Release(item); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAttributes(nint self, uint mask, out uint attributes);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl,
        uint attributes, out uint receivedAttributes);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateItemFromIDList(nint pidl, in Guid interfaceId, out nint item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILIsEqual(nint first, nint second);
}
