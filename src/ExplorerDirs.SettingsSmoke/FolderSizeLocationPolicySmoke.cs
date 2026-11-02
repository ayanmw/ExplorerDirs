using System.IO;
using ExplorerDirs.App;

internal static class FolderSizeLocationPolicySmoke
{
    internal static void Run()
    {
        var profile = Guid.NewGuid();
        var initial = ViewState.Default();
        var defaults = FolderSizeLocationPolicy.WithSizeColumn(initial, true);
        if (defaults.DisplayColumns?.Count != 4 || defaults.DisplayColumns[^1].Key != FolderSizeLocationPolicy.SizeColumn ||
            initial.DisplayColumns is not null || defaults.Mode != initial.Mode)
            throw new Exception("Default size columns mutated the saved view or changed its mode.");
        var custom = initial with { DisplayColumns = [new(new PropertyKeyState(Guid.NewGuid(), 7), 333)],
            SortColumns = [new(FolderSizeLocationPolicy.SizeColumn, -1)] };
        var enabled = FolderSizeLocationPolicy.WithSizeColumn(custom, true);
        var repeated = FolderSizeLocationPolicy.WithSizeColumn(enabled, true);
        var disabled = FolderSizeLocationPolicy.WithSizeColumn(enabled, false);
        if (enabled.DisplayColumns?.Count != 2 || enabled.DisplayColumns[0] != custom.DisplayColumns![0] ||
            repeated.DisplayColumns?.Count != 2 || disabled.DisplayColumns?.Count != 2 ||
            disabled.SortColumns.Single().Direction != -1 || custom.DisplayColumns.Count != 1)
            throw new Exception("Size visibility lost custom columns, duplicated size or removed file-size sorting on disable.");
        const uint ordinary = 0x60000000;
        var path = @"C:\测试\目录.zip";
        var provider = FolderSizeLocationPolicy.NativeIdentity(path, ordinary, profile, enabled: true);
        if (provider == path || FolderSizeLocationPolicy.LogicalPath(provider, profile) != path)
            throw new Exception("A real directory with ZIP suffix was excluded or lost its display identity.");
        if (FolderSizeLocationPolicy.NativeIdentity(path, ordinary | 0x00400000, profile, true) != path ||
            FolderSizeLocationPolicy.NativeIdentity("shell:MyComputerFolder", 0x20000000, profile, true) != "shell:MyComputerFolder" ||
            FolderSizeLocationPolicy.NativeIdentity(path, ordinary, profile, false) != path ||
            FolderSizeLocationPolicy.NativeIdentity(provider, ordinary, profile, false) != provider)
            throw new Exception("ZIP/virtual/off routing or existing provider history changed.");
        var nested = provider + @"\子目录\file.txt";
        if (FolderSizeLocationPolicy.LogicalPath(nested, profile) != path + @"\子目录\file.txt")
            throw new Exception("Nested provider identity did not preserve the physical path.");
        if (!FolderSizeLocationPolicy.TryGetProviderChildIdentity(provider, path, path + @"\a.txt", profile, out var child) ||
            child != provider + @"\a.txt" ||
            FolderSizeLocationPolicy.TryGetProviderChildIdentity(provider, path, @"C:\other\a.txt", profile, out _) ||
            FolderSizeLocationPolicy.TryGetProviderChildIdentity(provider, path, path + @"\nested\a.txt", profile, out _))
            throw new Exception("Provider selection admitted a non-direct child or lost its native parent.");
        try { FolderSizeLocationPolicy.LogicalPath(provider + @"\..\outside", profile); throw new Exception("Ambiguous provider traversal accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("Folder size location policy smoke: PASS (physical/native identities, nested paths, ZIP streams, virtual/off routing, history identity and direct-child selection; no Shell/IO)");
    }
}
