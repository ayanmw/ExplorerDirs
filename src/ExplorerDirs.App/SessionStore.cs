using System.Text.Json;
using System.IO;

namespace ExplorerDirs.App;

internal sealed record SessionState(int Layout, List<List<string>> Panes);

internal sealed class SessionStore
{
#if DEBUG
    private const string ProfileDirectory = "ExplorerDirs.Dev";
#else
    private const string ProfileDirectory = "ExplorerDirs";
#endif
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProfileDirectory, "session.json");

    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_path));
            return state?.Panes is { Count: > 0 } ? state : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(SessionState state)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, _path, true);
    }
}
