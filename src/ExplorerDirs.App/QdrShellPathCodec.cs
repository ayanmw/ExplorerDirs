using System.Globalization;
using System.IO;
using System.Text;

namespace ExplorerDirs.App;

internal static class QdrShellPathCodec
{
    internal static string ToProviderIdentity(string physicalPath, Guid profileProviderClassId)
    {
        if (profileProviderClassId == Guid.Empty) throw new ArgumentException("A profile provider CLSID is required.", nameof(profileProviderClassId));
        if (!Path.IsPathFullyQualified(physicalPath)) throw new ArgumentException("A fully qualified filesystem path is required.", nameof(physicalPath));
        var token = string.Concat(physicalPath.Select(character =>
            ((int)character).ToString("X4", CultureInfo.InvariantCulture)));
        return $"::{{{profileProviderClassId:D}}}\\edpath:{token}";
    }

    internal static bool TryGetProviderPath(string identity, Guid? profileProviderClassId, out string physicalPath)
    {
        physicalPath = string.Empty;
        if (profileProviderClassId is not { } classId) return false;
        var providerPrefix = $"::{{{classId:D}}}\\edpath:";
        if (!identity.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        physicalPath = DecodeProviderPath(identity[providerPrefix.Length..]);
        return true;
    }

    internal static string ToQdrTextPath(string identity, Guid? profileProviderClassId = null)
    {
        if (TryGetProviderPath(identity, profileProviderClassId, out var physicalPath)) return physicalPath;

        return identity.StartsWith("::", StringComparison.Ordinal) ? "shell:" + identity : identity;
    }

    private static string DecodeProviderPath(string token)
    {
        if (token.Length == 0 || token.Length % 4 != 0)
            throw new InvalidDataException("应用 Shell 目录标识中的物理路径数据无效。");

        var path = new StringBuilder(token.Length / 4);
        for (var index = 0; index < token.Length; index += 4)
        {
            if (!ushort.TryParse(token.AsSpan(index, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var character))
                throw new InvalidDataException("应用 Shell 目录标识中的物理路径数据无效。");
            path.Append((char)character);
        }

        var decoded = path.ToString();
        if (decoded.Contains('\0') || !Path.IsPathFullyQualified(decoded))
            throw new InvalidDataException("应用 Shell 目录标识没有包含完整的物理路径。");
        return decoded;
    }
}
