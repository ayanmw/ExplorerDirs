using System.Globalization;
using System.IO;
using System.Text;

namespace ExplorerDirs.App;

internal static class QdrShellPathCodec
{
    internal static string ToQdrTextPath(string identity, Guid? profileProviderClassId = null)
    {
        if (profileProviderClassId is { } classId)
        {
            var providerPrefix = $"::{{{classId:D}}}\\edpath:";
            if (identity.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
                return DecodeProviderPath(identity[providerPrefix.Length..]);
        }

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
