using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;

// Calls production host methods and reads its published state, not just the
// underlying native browser. All fixtures and hosts belong to this process.
internal static class SearchHostProbe
{
    internal static object CreateViewState(Type stateType, int pane)
    {
        var format = Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        object Key(uint id) => new { FormatId = format, PropertyId = id };
        var state = new
        {
            Mode = pane < 2 ? 4u : 1u,
            IconSize = pane < 2 ? 16 : pane == 2 ? 96 : 256,
            SortColumns = new[] { new { Key = Key(pane % 2 == 0 ? 10u : 12u), Direction = pane % 2 == 0 ? -1 : 1 } },
            GroupKey = pane % 2 == 0 ? null : Key(4),
            GroupAscending = false,
            DisplayColumns = new[]
            {
                new { Key = Key(10), Width = 271 + pane },
                new { Key = Key(12), Width = 137 + pane },
                new { Key = Key(4), Width = 193 + pane }
            }
        };
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(state), stateType)!;
    }

    internal static void Run(List<HwndHost> hosts, List<string> folders, Func<HwndHost, string> names)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = hosts[0].GetType();
        object? Call(HwndHost host, string name, params object[] args) => type.GetMethod(name, instance)!.Invoke(host, args);
        string? Query(HwndHost host) => (string?)type.GetProperty("SearchQuery", instance)!.GetValue(host);
        string? PathOf(HwndHost host) => (string?)type.GetProperty("CurrentPath", instance)!.GetValue(host);
        string ReadNames(HwndHost host)
        {
            try { return names(host); }
            // Group/sort application can replace item arrays after navigation
            // completes. Retry the whole observation, never accept partial data.
            catch (Exception ex) when (ex is COMException or ArgumentException)
            { return $"unavailable:0x{ex.HResult:X8}"; }
        }
        bool Pending(HwndHost host)
        {
            var ready = type.GetField("_viewReadiness", instance)!.GetValue(host)!;
            return (bool)ready.GetType().GetProperty("Pending", instance)!.GetValue(ready)!;
        }
        string Snapshot(HwndHost host)
        {
            var result = JsonSerializer.Serialize(Call(host, "CaptureViewState"));
            if ((bool)type.GetProperty("ViewCaptureFailed", instance)!.GetValue(host)!)
                throw new Exception("Native view capture failed; a cached fallback cannot prove restoration.");
            return result;
        }
        var errors = new List<string>();
        var published = hosts.ToDictionary(host => host, _ => (string?)null);
        EventHandler<string> onError = (_, error) => errors.Add(error);
        EventHandler<string> onDirectory = (sender, _) => published[(HwndHost)sender!] = "directory";
        EventHandler<string> onSearch = (sender, query) => published[(HwndHost)sender!] = query;
        foreach (var host in hosts)
        {
            if (host.IsVisible) throw new Exception("Search host must be hidden to prevent focus restoration.");
            type.GetEvent("NavigationFailed")!.AddEventHandler(host, onError);
            type.GetEvent("Navigated")!.AddEventHandler(host, onDirectory);
            type.GetEvent("SearchNavigated")!.AddEventHandler(host, onSearch);
        }
        var snapshots = new string[hosts.Count];
        var requested = hosts.Select(host => JsonSerializer.Serialize(Call(host, "CachedViewState"))).ToArray();
        var clock = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
        var stage = 0;
        var reading = false;
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            if (reading) return;
            reading = true;
            try
            {
                if (errors.Count != 0) throw new Exception("Production host errors: " + string.Join(";", errors));
                var expectedQuery = stage switch { 1 or 3 => "initial", 2 or 4 or 7 => "second", _ => null };
                var expectedNames = expectedQuery is null ? "initial.txt|second.txt" : expectedQuery + ".txt";
                var settled = hosts.Select((host, i) => !Pending(host) &&
                    published[host] == (expectedQuery ?? "directory") && Query(host) == expectedQuery &&
                    PathOf(host) == folders[i] && ReadNames(host) == expectedNames).All(x => x);
                if (!settled)
                {
                    if (clock.ElapsedMilliseconds > 15000)
                        throw new Exception($"Search host stage {stage} timed out: " + string.Join(';', hosts.Select(h => $"pending={Pending(h)},query={Query(h)},names={ReadNames(h)}")));
                    return;
                }
                Console.WriteLine($"production-search stage={stage}; four hosts query/state/results match; elapsed-ms={clock.ElapsedMilliseconds}");
                if (stage == 0)
                    for (var i = 0; i < hosts.Count; i++)
                    {
                        snapshots[i] = Snapshot(hosts[i]);
                        using var expected = JsonDocument.Parse(requested[i]);
                        using var actual = JsonDocument.Parse(snapshots[i]);
                        var properties = new List<string> { "Mode", "IconSize", "SortColumns", "GroupKey", "DisplayColumns" };
                        if (expected.RootElement.GetProperty("GroupKey").ValueKind != JsonValueKind.Null) properties.Add("GroupAscending");
                        foreach (var property in properties)
                            if (!JsonElement.DeepEquals(expected.RootElement.GetProperty(property), actual.RootElement.GetProperty(property)))
                                throw new Exception($"Pane {i} initial native {property} differs from requested configuration: {snapshots[i]}");
                    }
                else
                    for (var i = 0; i < hosts.Count; i++)
                        if (Snapshot(hosts[i]) != snapshots[i])
                            throw new Exception($"Pane {i} stage {stage}: {(expectedQuery is null ? "restored native" : "cached")} directory settings changed.");
                if (stage == 7) { frame.Continue = false; return; }
                foreach (var host in hosts) published[host] = null;
                if (stage is 5 or 6)
                {
                    var returnToDirectory = stage == 5;
                    stage++;
                    clock.Restart();
                    for (var i = 0; i < hosts.Count; i++)
                    {
                        Call(hosts[i], "Search", "initial");
                        if (returnToDirectory) Call(hosts[i], "Navigate", folders[i]);
                        else Call(hosts[i], "Search", "second");
                    }
                    return;
                }
                var action = stage switch { 0 or 1 => "Search", 2 => "GoBack", 3 => "GoForward", _ => "GoParent" };
                stage++;
                clock.Restart();
                foreach (var host in hosts)
                {
                    var result = action == "Search" ? Call(host, action, stage == 1 ? "initial" : "second") : Call(host, action);
                    if (result is false) throw new Exception(action + " was rejected.");
                }
            }
            catch (Exception ex) { failure = ex; frame.Continue = false; }
            finally { reading = false; }
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally
        {
            timer.Stop();
            foreach (var host in hosts)
            {
                type.GetEvent("NavigationFailed")!.RemoveEventHandler(host, onError);
                type.GetEvent("Navigated")!.RemoveEventHandler(host, onDirectory);
                type.GetEvent("SearchNavigated")!.RemoveEventHandler(host, onSearch);
            }
        }
        if (failure is not null) throw new InvalidOperationException("Production search integration failed.", failure);
        Console.WriteLine("Production search: two queries, native history, parent return, immediate supersession, published events, independent cached and restored native directory settings passed; hidden hosts only.");
    }
}
