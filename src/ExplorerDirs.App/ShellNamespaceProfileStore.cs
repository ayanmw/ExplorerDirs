using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed class ShellNamespaceProfileStore(string path)
{
    private const int CurrentFormatVersion = 1;

    public string? LoadWarning { get; private set; }

    public Guid? LoadOrCreate()
    {
        LoadWarning = null;
        if (File.Exists(path)) return ReadExisting();

        var classId = Guid.NewGuid();
        var content = Render(classId);
        try
        {
            PortableIniWriter.WriteIfUnchanged(path, expected: null, content,
                "Shell namespace profile identity changed while it was being created.");
            return classId;
        }
        catch (IOException)
        {
            // A concurrent first launch may have created the profile ID; use only its validated value.
            if (!File.Exists(path)) throw;
            return ReadExisting();
        }
    }

    private Guid? ReadExisting()
    {
        string content;
        try { content = File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LoadWarning = "Shell namespace 身份配置无法读取；未生成新身份。";
            return null;
        }

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        Guid? classId = null;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line.Equals("[ShellNamespace]", StringComparison.OrdinalIgnoreCase);
                if (!inSection || seenSection) complete = false;
                seenSection |= inSection;
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inSection || equals < 1) { complete = false; continue; }
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
            {
                if (version >= 0 || !BigInteger.TryParse(value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out version))
                    complete = false;
            }
            else if (key.Equals("ClassId", StringComparison.OrdinalIgnoreCase))
            {
                if (classId.HasValue || !Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
                    complete = false;
                else classId = parsed;
            }
            else complete = false;
        }

        if (version > CurrentFormatVersion)
        {
            LoadWarning = "Shell namespace 身份配置格式高于当前版本；已停止使用，不会覆盖原文件。";
            return null;
        }
        if (!complete || version != CurrentFormatVersion || !seenSection || !classId.HasValue)
        {
            LoadWarning = "Shell namespace 身份配置不完整；原文件已保留，未自动更换 CLSID。";
            return null;
        }
        return classId;
    }

    private static string Render(Guid classId) =>
        "; Explorer Dirs per-profile Shell namespace identity. Keep stable when moving this profile." + Environment.NewLine +
        "[ShellNamespace]" + Environment.NewLine +
        $"FormatVersion={CurrentFormatVersion}" + Environment.NewLine +
        $"ClassId={classId:D}" + Environment.NewLine;
}
