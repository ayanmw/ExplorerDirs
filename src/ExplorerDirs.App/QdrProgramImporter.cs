using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record QdrProgramImportSkippedRow(int LineNumber, string Text, string Reason);

internal sealed record QdrProgramImportPreview(
    IReadOnlyList<ProgramStarterEntry> Entries,
    IReadOnlyList<QdrProgramImportSkippedRow> SkippedRows);

internal static class QdrProgramImporter
{
    public static string ReadConfiguration(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
            return new UTF32Encoding(false, true, true).GetString(bytes, 4, bytes.Length - 4);
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
            return new UTF32Encoding(true, true, true).GetString(bytes, 4, bytes.Length - 4);
        if (HasPrefix(bytes, 0xFF, 0xFE))
            return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
        if (HasPrefix(bytes, 0xFE, 0xFF))
            return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);

        var offset = HasPrefix(bytes, 0xEF, 0xBB, 0xBF) ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            if (offset != 0) throw;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var activeCodePage = GetActiveCodePage();
            return Encoding.GetEncoding((int)activeCodePage, EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetString(bytes, offset, bytes.Length - offset);
        }
    }

    public static QdrProgramImportPreview Parse(string text)
    {
        List<ProgramStarterEntry> entries = [];
        List<QdrProgramImportSkippedRow> skipped = [];
        var inProgram = false;
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inProgram = line.Equals("[Program]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inProgram) continue;

            var nameEnd = line.IndexOf('=');
            if (nameEnd < 0)
            {
                skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line, "缺少程序名称和路径字段。"));
                continue;
            }
            var name = line[..nameEnd].Trim();
            if (name.Equals("portable_install", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Length == 0)
            {
                skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line, "程序名称为空。"));
                continue;
            }
            var pathEnd = FindUnquotedEquals(line, nameEnd + 1);
            var executableField = pathEnd < 0 ? line[(nameEnd + 1)..] : line[(nameEnd + 1)..pathEnd];
            if (!TryReadQuotedField(executableField, out var executablePath))
            {
                skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line, "程序路径的引号不完整或格式无效。"));
                continue;
            }
            if (executablePath.Length == 0)
            {
                skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line, "程序路径为空。"));
                continue;
            }
            var tail = pathEnd < 0 ? string.Empty : line[(pathEnd + 1)..];
            var tailSeparators = FindUnquotedEqualses(tail);
            var arguments = tail.Trim();
            var workingDirectory = string.Empty;
            if (tailSeparators.Count > 1)
            {
                skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line,
                    "参数与工作目录之间包含多个未引用的等号，无法唯一识别字段。"));
                continue;
            }
            // A single remaining delimiter is the documented Arguments/Working-directory boundary.
            // Equals signs inside a quoted argument are data, not field delimiters.
            if (tailSeparators.Count == 1)
            {
                var separator = tailSeparators[0];
                arguments = tail[..separator].Trim();
                var workingDirectoryField = tail[(separator + 1)..];
                if (!TryReadQuotedField(workingDirectoryField, out workingDirectory))
                {
                    skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line, "工作目录的引号不完整或格式无效。"));
                    continue;
                }
                if (!IsUnambiguousWorkingDirectory(workingDirectoryField, workingDirectory))
                {
                    skipped.Add(new QdrProgramImportSkippedRow(lineNumber, line,
                        "参数与工作目录之间的等号无法唯一识别；请为工作目录加引号或使用完整/相对路径。"));
                    continue;
                }
            }
            entries.Add(new ProgramStarterEntry(Guid.NewGuid(), name, executablePath, arguments, workingDirectory));
        }

        return new QdrProgramImportPreview(entries, skipped);
    }

    private static int FindUnquotedEquals(string value, int startIndex)
    {
        var quoted = false;
        for (var index = startIndex; index < value.Length; index++)
        {
            if (value[index] == '"') quoted = !quoted;
            else if (value[index] == '=' && !quoted) return index;
        }
        return -1;
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix) => bytes.AsSpan().StartsWith(prefix);

    private static List<int> FindUnquotedEqualses(string value)
    {
        List<int> separators = [];
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"') quoted = !quoted;
            else if (value[index] == '=' && !quoted) separators.Add(index);
        }
        return separators;
    }

    private static bool TryReadQuotedField(string raw, out string value)
    {
        value = raw.Trim();
        var startsQuoted = value.StartsWith('"');
        var endsQuoted = value.EndsWith('"');
        if (startsQuoted != endsQuoted) return false;
        if (!startsQuoted) return !value.Contains('"');
        if (value.Length < 2 || value[1..^1].Contains('"')) return false;
        value = value[1..^1];
        return true;
    }

    private static bool IsUnambiguousWorkingDirectory(string raw, string value)
    {
        if (raw.Trim().StartsWith('"')) return true;
        return value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar) ||
               value.Contains(':') || (value.StartsWith('%') && value.EndsWith('%'));
    }

    [DllImport("kernel32.dll", EntryPoint = "GetACP")]
    private static extern uint GetActiveCodePage();
}
