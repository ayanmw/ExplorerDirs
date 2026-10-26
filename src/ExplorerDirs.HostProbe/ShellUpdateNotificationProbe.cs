using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

// Verifies the production notification reaches a registered Shell observer without creating an ExplorerBrowser view.
internal static class ShellUpdateNotificationProbe
{
    internal static int RunRegistrationLifecycleStress(int cycles = 40)
        => RunRegistrationLifecycleStress(cycles, freshWindowPerCycle: false, settleMilliseconds: 0);

    internal static int RunFreshWindowRegistrationLifecycleStress(int cycles = 40, int settleMilliseconds = 100)
        => RunRegistrationLifecycleStress(cycles, freshWindowPerCycle: true, settleMilliseconds: settleMilliseconds);

    internal static int RunSameProcessDeliveryStress(int cycles = 12, int setupDelayMilliseconds = 150,
        int settleMilliseconds = 300, int alternateSettleMilliseconds = -1, bool freshWindowPerCycle = false)
    {
        var foregroundBefore = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SameProcessDelivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var successes = 0;
        var registrationFailures = 0;
        var deliveryFailures = 0;
        var deregistrationFailures = 0;
        var failures = new List<string>();
        var bySettle = new Dictionary<int, int[]>(); // successes, registrations, deliveries, deregistrations
        HwndSource? sharedSource = null;
        try
        {
            if (!freshWindowPerCycle) sharedSource = CreateRegistrationSource();
            if (!freshWindowPerCycle && GetForegroundWindow() != foregroundBefore)
                throw new InvalidOperationException("Foreground changed after creating the observer HWND.");

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                var source = freshWindowPerCycle ? CreateRegistrationSource() : sharedSource!;
                if (freshWindowPerCycle && GetForegroundWindow() != foregroundBefore)
                {
                    source.Dispose();
                    throw new InvalidOperationException("Foreground changed after creating a cycle observer HWND.");
                }
                var cycleSettle = alternateSettleMilliseconds >= 0 && cycle % 2 == 0
                    ? alternateSettleMilliseconds : settleMilliseconds;
                if (!bySettle.TryGetValue(cycleSettle, out var groupCounts))
                    bySettle[cycleSettle] = groupCounts = new int[4];
                var name = $"changed-{cycle:D2}.txt";
                var path = Path.Combine(root, name);
                ChangeNotificationObserver? observer;
                try { observer = new ChangeNotificationObserver(source, root, Stopwatch.StartNew()); }
                catch
                {
                    registrationFailures++;
                    groupCounts[1]++;
                    failures.Add($"cycle={cycle};settle-ms={cycleSettle};stage=registration");
                    PumpDispatcherFor(cycleSettle);
                    if (freshWindowPerCycle) source.Dispose();
                    continue;
                }

                try
                {
                    File.WriteAllText(path, "before");
                    NotifyCreatedItem(path);
                    try { WaitForTargetCount(observer, $"2/{name}", 1, TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException)
                    {
                        deliveryFailures++;
                        groupCounts[2]++;
                        failures.Add($"cycle={cycle};settle-ms={cycleSettle};stage=create;{observer.Summary}");
                        continue;
                    }
                    PumpFor(TimeSpan.FromMilliseconds(setupDelayMilliseconds));
                    File.WriteAllText(path, "after with changed size");
                    NotifyChangedItem(path);
                    try
                    {
                        WaitForUpdateItem(observer, TimeSpan.FromSeconds(5), name);
                        successes++;
                        groupCounts[0]++;
                    }
                    catch (TimeoutException)
                    {
                        deliveryFailures++;
                        groupCounts[2]++;
                        failures.Add($"cycle={cycle};settle-ms={cycleSettle};stage=delivery;{observer.Summary}");
                    }
                }
                finally
                {
                    observer.Dispose();
                    if (!observer.DeregistrationSucceeded)
                    {
                        deregistrationFailures++;
                        groupCounts[3]++;
                        failures.Add($"cycle={cycle};settle-ms={cycleSettle};stage=deregistration");
                    }
                    PumpDispatcherFor(cycleSettle);
                    if (File.Exists(path)) File.Delete(path);
                    PumpDispatcherFor(50);
                    if (freshWindowPerCycle) source.Dispose();
                }
            }

            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            var settleDescription = alternateSettleMilliseconds >= 0
                ? $"{settleMilliseconds}/{alternateSettleMilliseconds} alternating" : settleMilliseconds.ToString();
            var groups = string.Join('|', bySettle.OrderBy(item => item.Key).Select(item =>
                $"settle={item.Key}:success={item.Value[0]},registration-failures={item.Value[1]},delivery-failures={item.Value[2]},deregistration-failures={item.Value[3]}"));
            var windowMode = freshWindowPerCycle ? "new-window-per-cycle" : "shared-window";
            Console.WriteLine($"shell-change-same-process-delivery-stress; mode={windowMode}; cycles={cycles}; successes={successes}; registration-failures={registrationFailures}; delivery-failures={deliveryFailures}; deregistration-failures={deregistrationFailures}; {groups}; setup-delay-ms={setupDelayMilliseconds}; settle-pump-ms={settleDescription}; foreground-unchanged={foregroundUnchanged}; explorer-browser-created=false");
            foreach (var failure in failures) Console.WriteLine(failure);
            return foregroundUnchanged && successes == cycles && registrationFailures == 0 &&
                   deliveryFailures == 0 && deregistrationFailures == 0 ? 0 : 1;
        }
        finally
        {
            sharedSource?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static int RunSameRegistrationDeliveryControl(int notifications = 12, int setupDelayMilliseconds = 150,
        int spacingMilliseconds = 0)
    {
        var foregroundBefore = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SameRegistrationDelivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        HwndSource? source = null;
        ChangeNotificationObserver? observer = null;
        var successes = 0;
        var createFailures = 0;
        var updateFailures = 0;
        var failedSummaries = new List<string>();
        try
        {
            source = CreateRegistrationSource();
            if (GetForegroundWindow() != foregroundBefore)
                throw new InvalidOperationException("Foreground changed after creating the observer HWND.");
            observer = new ChangeNotificationObserver(source, root, Stopwatch.StartNew());
            for (var index = 1; index <= notifications; index++)
            {
                var name = $"control-{index:D2}.txt";
                var path = Path.Combine(root, name);
                File.WriteAllText(path, "created");
                NotifyCreatedItem(path);
                try
                {
                    WaitForTargetCount(observer, $"2/{name}", 1, TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException ex)
                {
                    createFailures++;
                    failedSummaries.Add($"notification={index};stage=create;{ex.Message}");
                    break;
                }

                var expectedCount = observer.TargetCount($"2000/{name}") + 1;
                PumpFor(TimeSpan.FromMilliseconds(setupDelayMilliseconds));
                File.WriteAllText(path, new string('x', index * 32));
                NotifyChangedItem(path);
                try
                {
                    WaitForTargetCount(observer, $"2000/{name}", expectedCount, TimeSpan.FromSeconds(5));
                    successes++;
                }
                catch (TimeoutException ex)
                {
                    updateFailures++;
                    failedSummaries.Add($"notification={index};stage=update;{ex.Message}");
                    break;
                }
                PumpDispatcherFor(spacingMilliseconds);
            }

            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            Console.WriteLine($"shell-change-same-registration-delivery-control; notifications={notifications}; create-update-successes={successes}; create-failures={createFailures}; update-failures={updateFailures}; setup-delay-ms={setupDelayMilliseconds}; inter-notification-pump-ms={spacingMilliseconds}; foreground-unchanged={foregroundUnchanged}; explorer-browser-created=false");
            foreach (var failure in failedSummaries) Console.WriteLine(failure);
            return foregroundUnchanged && successes == notifications ? 0 : 1;
        }
        catch (TimeoutException)
        {
            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            Console.WriteLine($"shell-change-same-registration-delivery-control; notifications={notifications}; create-update-successes={successes}; create-failures={createFailures}; update-failures={updateFailures}; setup-delay-ms={setupDelayMilliseconds}; inter-notification-pump-ms={spacingMilliseconds}; foreground-unchanged={foregroundUnchanged}; explorer-browser-created=false");
            return 1;
        }
        finally
        {
            observer?.Dispose();
            if (source is not null)
            {
                PumpFor(TimeSpan.FromMilliseconds(100));
                source.Dispose();
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static int RunRegistrationLifecycleStress(int cycles, bool freshWindowPerCycle = false, int settleMilliseconds = 0)
    {
        var foregroundBefore = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ChangeRegistrationStress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var registrations = 0;
        var deregistrations = 0;
        var failures = new List<string>();
        var pumpDuringSettle = Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_PUMP_DURING_SETTLE") == "1";
        HwndSource? sharedSource = null;
        try
        {
            if (!freshWindowPerCycle) sharedSource = CreateRegistrationSource();
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                var source = freshWindowPerCycle ? CreateRegistrationSource() : sharedSource!;
                ChangeNotificationObserver observer;
                try { observer = new ChangeNotificationObserver(source, root, Stopwatch.StartNew(), resolveTargetPaths: false); }
                catch (Exception ex)
                {
                    failures.Add($"cycle={cycle + 1} stage=register {ex.Message}");
                    if (freshWindowPerCycle) source.Dispose();
                    continue;
                }

                registrations++;
                observer.Dispose();
                if (observer.DeregistrationSucceeded) deregistrations++;
                else failures.Add($"cycle={cycle + 1} stage=deregister returned false");
                if (freshWindowPerCycle)
                {
                    if (pumpDuringSettle) PumpDispatcherFor(settleMilliseconds);
                    else Thread.Sleep(settleMilliseconds);
                    source.Dispose();
                }
            }

            var foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
            if (!foregroundUnchanged) failures.Add("foreground changed during the observer-only probe");
            var windowMode = freshWindowPerCycle ? $"fresh-window;settle-ms={settleMilliseconds}" : "shared-window";
            Console.WriteLine($"shell-change-registration-stress mode={windowMode}; event-mask=0x{GetRegistrationEventMask():X8}; settle-pump={pumpDuringSettle}; cycles={cycles}; registrations={registrations}; deregistrations={deregistrations}; failures={failures.Count}; foreground-unchanged={foregroundUnchanged}; explorer-browser-created=false");
            foreach (var failure in failures) Console.Error.WriteLine(failure);
            return failures.Count == 0 && registrations == cycles && deregistrations == cycles ? 0 : 1;
        }
        finally
        {
            sharedSource?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static int GetRegistrationEventMask()
        => int.TryParse(Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_EVENT_MASK"),
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture,
            out var configured) ? configured : 0x7FFFFFFF;

    internal static void PumpDispatcherFor(int milliseconds)
    {
        if (milliseconds <= 0) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static HwndSource CreateRegistrationSource() => new(new HwndSourceParameters("Explorer Dirs registration lifecycle observer")
    {
        Width = 1,
        Height = 1,
        WindowStyle = 0
    });

    internal static int Run()
    {
        var foregroundBefore = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ShellUpdateSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new HwndSource(new HwndSourceParameters("Explorer Dirs Shell notification observer")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            });
            ChangeNotificationObserver? observer = null;
            try
            {
                var clock = Stopwatch.StartNew();
                observer = new ChangeNotificationObserver(source, root, clock);
                var path = Path.Combine(root, "changed.txt");
                File.WriteAllText(path, "before");
                NotifyCreatedItem(path);
                WaitForTargetCount(observer, "2/changed.txt", 1, TimeSpan.FromSeconds(5));
                var setupDelayMilliseconds = int.TryParse(
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_SETUP_DELAY_MS"),
                    out var requestedSetupDelay) && requestedSetupDelay is >= 0 and <= 5000
                    ? requestedSetupDelay : 150;
                PumpFor(TimeSpan.FromMilliseconds(setupDelayMilliseconds));
                File.WriteAllText(path, "after with changed size");

                NotifyChangedItem(path);

                var summary = WaitForUpdateItem(observer, TimeSpan.FromSeconds(5), "changed.txt");
                if (GetForegroundWindow() != foregroundBefore)
                    throw new InvalidOperationException("Foreground window changed during the noninteractive notification probe.");
                Console.WriteLine($"shell-update-notification-smoke PASS; setup-delay-ms={setupDelayMilliseconds}; {summary}; foreground-unchanged=true; explorer-browser-created=false");
                return 0;
            }
            finally
            {
                observer?.Dispose();
                // Shell notification deregistration can finish asynchronously after its API call returns.
                // Keep the recipient HWND alive briefly so the next probe process cannot race its teardown.
                PumpFor(TimeSpan.FromMilliseconds(100));
                source.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void NotifyChangedItem(string path)
    {
        var notificationType = Assembly.Load("ExplorerDirs.App")
            .GetType("ExplorerDirs.App.ShellChangedItemNotification", throwOnError: true)!;
        if (!(bool)notificationType.GetMethod("ShouldNotifyPath", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [path])!)
            throw new InvalidOperationException("The fixture path was rejected by the production path policy.");
        notificationType.GetMethod("NotifyChangedItem", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [path]);
    }

    private static void NotifyCreatedItem(string path) => SHChangeNotify(0x00000002, 0x00002005, path, IntPtr.Zero);

    private static string WaitForUpdateItem(ChangeNotificationObserver observer, TimeSpan timeout, string expectedItem)
    {
        var summary = observer.Summary;
        bool ReceivedExpectedItem(string value) => value.Contains($"2000/{expectedItem}", StringComparison.OrdinalIgnoreCase);
        if (ReceivedExpectedItem(summary)) return summary;

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            summary = observer.Summary;
            if (ReceivedExpectedItem(summary) || clock.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!ReceivedExpectedItem(summary))
            throw new TimeoutException($"Shell did not deliver SHCNE_UPDATEITEM for the isolated fixture within {timeout}: {summary}");
        return summary;
    }

    private static void WaitForTargetCount(ChangeNotificationObserver observer, string target, int expectedCount,
        TimeSpan timeout)
    {
        if (observer.TargetCount(target) >= expectedCount) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (observer.TargetCount(target) >= expectedCount || clock.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (observer.TargetCount(target) < expectedCount)
        {
            var eventName = target.StartsWith("2/", StringComparison.Ordinal) ? "SHCNE_CREATE" : "SHCNE_UPDATEITEM";
            throw new TimeoutException($"Shell did not deliver {eventName} for {target} within {timeout}: {observer.Summary}");
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(int eventId, uint flags, string item1, nint item2);
}
