using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Threading;

internal sealed class ThreadMessagePumpTraceProbe : IDisposable
{
    private const int WhGetMessage = 3;
    private const int HcAction = 0;
    private const int PmRemove = 1;
    private const uint SlowWindowProcedureThresholdTicks = 10;
    private const int MaxDispatcherOperationIntervals = 100_000;
    private static ThreadMessagePumpTraceProbe? _active;
    private readonly HookProcedure _hookProcedure;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly List<SlowEvent> _slowEvents = new(128);
    private readonly List<uint> _messages = new(512);
    private readonly List<uint> _windowProcedureMessages = new(512);
    private readonly List<IntPtr> _subclassedWindows = new();
    private readonly Dictionary<IntPtr, string> _windowClasses = new();
    private readonly Dictionary<(string ClassName, uint Message), WindowProcedureTiming> _slowWindowProcedureTimings = new();
    private readonly ConcurrentDictionary<DispatcherOperation, long> _dispatcherOperationPostedAt = new();
    private readonly Dictionary<DispatcherOperation, long> _dispatcherOperationStarts = new();
    private readonly Dictionary<DispatcherPriority, DispatcherOperationAggregate> _dispatcherOperationTotals = new();
    private readonly Dictionary<(DispatcherPriority Priority, string Kind), DispatcherOperationAggregate> _dispatcherOperationKinds = new();
    private readonly List<DispatcherOperationInterval> _dispatcherOperationIntervals = new();
    private readonly DispatcherHooks _dispatcherHooks;
    private readonly IntPtr _hook;
    private long _lastMessageAt;
    private long _maxGapTicks;
    private int _messageCount;
    private int _windowProcedureMessageCount;
    private Exception? _callbackError;

    internal ThreadMessagePumpTraceProbe()
    {
        if (_active is not null) throw new InvalidOperationException("Only one thread message trace may be active.");
        _hookProcedure = OnGetMessage;
        _subclassProcedure = OnWindowProcedure;
        _active = this;
        _hook = SetWindowsHookEx(WhGetMessage, _hookProcedure, IntPtr.Zero, GetCurrentThreadId());
        if (_hook == IntPtr.Zero)
        {
            _active = null;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the thread-specific WH_GETMESSAGE probe.");
        }
        _dispatcherHooks = Dispatcher.CurrentDispatcher.Hooks;
        _dispatcherHooks.OperationPosted += OnDispatcherOperationPosted;
        _dispatcherHooks.OperationStarted += OnDispatcherOperationStarted;
        _dispatcherHooks.OperationCompleted += OnDispatcherOperationCompleted;
        _dispatcherHooks.OperationAborted += OnDispatcherOperationAborted;
        // Keep the hook narrow: instrumenting every Shell child HWND changes the behavior being measured.
        AddWindow(GetDispatcherMessageWindowHandle());
    }

    internal string Summary =>
        $"messages={Volatile.Read(ref _messageCount)},max-gap-ms={TicksToMilliseconds(Interlocked.Read(ref _maxGapTicks)):F1}," +
        $"wndproc-messages={Volatile.Read(ref _windowProcedureMessageCount)},callback-error={_callbackError?.GetType().Name ?? "none"}," +
        $"dispatcher-operation-totals={string.Join('|', _dispatcherOperationTotals.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:completed={pair.Value.Completed},aborted={pair.Value.Aborted},total-ms={TicksToMilliseconds(pair.Value.TotalTicks):F1},max-ms={TicksToMilliseconds(pair.Value.MaxTicks):F1}"))}," +
        $"dispatcher-operation-kinds={string.Join('|', _dispatcherOperationKinds.OrderByDescending(pair => pair.Value.TotalTicks)
            .Select(pair => $"{pair.Key.Priority}/{pair.Key.Kind}:completed={pair.Value.Completed},total-ms={TicksToMilliseconds(pair.Value.TotalTicks):F1},max-ms={TicksToMilliseconds(pair.Value.MaxTicks):F1}"))}," +
        $"dispatcher-operation-intervals={_dispatcherOperationIntervals.Count},dropped={Math.Max(0, _dispatcherOperationTotals.Values.Sum(value => value.Completed) - _dispatcherOperationIntervals.Count)}," +
        $"slow-events=[{string.Join('|', _slowEvents.OrderByDescending(sample => sample.DurationTicks).Select(sample => sample.Description))}]," +
        $"slow-wndproc={WindowProcedureSummary()}";

    internal static bool RunHookSmoke()
    {
        const uint threadMessage = 0x8000 + 0x51A;
        const uint windowMessage = 0x8000 + 0x51B;
        var parameters = new HwndSourceParameters("ExplorerDirs.MessagePumpTraceSmoke")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        };
        using var source = new HwndSource(parameters);
        source.AddHook((IntPtr hwnd, int id, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if ((uint)id == windowMessage)
            {
                Thread.Sleep(120);
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        });

        using var trace = new ThreadMessagePumpTraceProbe();
        trace.AddWindow(source.Handle);
        _ = PeekMessageW(out _, IntPtr.Zero, 0, 0, PmRemove); // Create this thread's message queue before posting to it.
        if (!PostThreadMessageW(GetCurrentThreadId(), threadMessage, (nuint)0x1234, (nint)0x5678))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not post the thread-hook smoke message.");
        if (!PeekMessageW(out var captured, IntPtr.Zero, threadMessage, threadMessage, PmRemove) || captured.Message != threadMessage)
            throw new InvalidOperationException("The posted thread-hook smoke message was not removed from the queue.");
        _ = SendMessageW(source.Handle, windowMessage, IntPtr.Zero, IntPtr.Zero);
        var dispatcherMessage = RegisterWindowMessageW("DispatcherProcessQueue");
        if (dispatcherMessage == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the WPF Dispatcher queue message.");
        _ = SendMessageW(GetDispatcherMessageWindowHandle(), dispatcherMessage, IntPtr.Zero, IntPtr.Zero);
        var dispatcherOperationWindowStartedAt = Stopwatch.GetTimestamp();
        var dispatcher = Dispatcher.CurrentDispatcher;
        DispatcherOperation? contextIdleOperation = null;
        var contextIdlePostTask = Task.Run(() =>
        {
            Thread.Sleep(20);
            contextIdleOperation = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        });
        var dispatcherOperation = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new DispatcherOperationCallback(_ => { Thread.Sleep(120); return null; }), "hook-smoke");
        dispatcherOperation.Wait();
        contextIdlePostTask.GetAwaiter().GetResult();
        contextIdleOperation!.Wait();
        var dispatcherTimerWithoutBacklog = RunDispatcherTimerLatencySmoke(dispatcher, blockDispatcher: false);
        var dispatcherTimerWithBacklog = RunDispatcherTimerLatencySmoke(dispatcher, blockDispatcher: true);
        var dispatcherOperationWindowEndedAt = Stopwatch.GetTimestamp();
        var timestampedWindowProcedure = trace.Summary.Contains("wndproc=HwndWrapper[", StringComparison.Ordinal) &&
            trace.Summary.Contains("/0x851B", StringComparison.Ordinal) &&
            trace.Summary.Contains("start=", StringComparison.Ordinal) && trace.Summary.Contains("end=", StringComparison.Ordinal);
        var timestampedDispatcherOperation = trace.Summary.Contains("dispatcher-operation,priority=Background", StringComparison.Ordinal) &&
            trace.Summary.Contains("start=", StringComparison.Ordinal) && trace.Summary.Contains("end=", StringComparison.Ordinal) &&
            trace.Summary.Contains("state=System.String:hook-smoke", StringComparison.Ordinal);
        var dispatcherOperationsSummarized = trace.Summary.Contains("dispatcher-operation-totals=", StringComparison.Ordinal) &&
            trace.Summary.Contains("Background:completed=", StringComparison.Ordinal) &&
            trace.Summary.Contains("completed=", StringComparison.Ordinal);
        var dispatcherOperationsGroupedByKind = trace.Summary.Contains("Background/ThreadMessagePumpTraceProbe", StringComparison.Ordinal);
        var operationOverlap = trace.DescribeDispatcherOperationOverlap(dispatcherOperationWindowStartedAt,
            dispatcherOperationWindowEndedAt);
        var dispatcherOperationOverlapCorrelated = operationOverlap.Contains("Background:operations=", StringComparison.Ordinal) &&
            operationOverlap.Contains("overlap-ms=", StringComparison.Ordinal);
        var dispatcherQueueDelayIdentified = trace.Summary.Contains("dispatcher-queue-delay,priority=ContextIdle", StringComparison.Ordinal) &&
            trace.Summary.Contains("delay-ms=", StringComparison.Ordinal);
        var dispatcherTimerScheduleExcluded = !trace.Summary.Contains("timer-tag=timer-schedule-smoke", StringComparison.Ordinal);
        var dispatcherTimerBacklogMeasured = dispatcherTimerWithBacklog is { DelayMs: >= 100 } timerDelay &&
            Math.Abs((timerDelay.PostToStartMs - timerDelay.IntervalMs) - timerDelay.DelayMs) <= 50;
        if (!timestampedWindowProcedure || !timestampedDispatcherOperation || !dispatcherOperationsSummarized || !dispatcherOperationsGroupedByKind ||
            !dispatcherOperationOverlapCorrelated || !dispatcherQueueDelayIdentified ||
            !dispatcherTimerScheduleExcluded || !dispatcherTimerBacklogMeasured)
        {
            Console.WriteLine($"message-pump-hook-smoke-flags=thread-message:{trace.ContainsMessage(threadMessage)},window-message:{trace.ContainsWindowProcedureMessage(windowMessage)},dispatcher-message:{trace.ContainsWindowProcedureMessage(dispatcherMessage)},timestamped-window:{timestampedWindowProcedure},timestamped-operation:{timestampedDispatcherOperation},operation-totals:{dispatcherOperationsSummarized},operation-kinds:{dispatcherOperationsGroupedByKind},overlap:{dispatcherOperationOverlapCorrelated},queue-delay:{dispatcherQueueDelayIdentified},timer-schedule-excluded:{dispatcherTimerScheduleExcluded},timer-backlog:{dispatcherTimerWithBacklog},timer-unblocked:{dispatcherTimerWithoutBacklog},message-count:{trace.MessageCount},wndproc-count:{trace.WindowProcedureMessageCount},message-ids=[{string.Join(',', trace._messages.Select(message => $"0x{message:X}"))}]");
            Console.WriteLine(trace.Summary);
        }
        return trace.ContainsMessage(threadMessage) && trace.ContainsWindowProcedureMessage(windowMessage) &&
            trace.ContainsWindowProcedureMessage(dispatcherMessage) &&
            trace.MessageCount > 0 && trace.WindowProcedureMessageCount > 0 &&
            timestampedWindowProcedure && timestampedDispatcherOperation && dispatcherOperationsSummarized && dispatcherOperationsGroupedByKind &&
            dispatcherOperationOverlapCorrelated && dispatcherQueueDelayIdentified &&
            dispatcherTimerScheduleExcluded && dispatcherTimerBacklogMeasured;
    }

    private static (double DelayMs, double PostToStartMs, double IntervalMs)? RunDispatcherTimerLatencySmoke(
        Dispatcher dispatcher, bool blockDispatcher)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(blockDispatcher ? 150 : 250),
            Tag = blockDispatcher ? "timer-backlog-smoke" : "timer-schedule-smoke"
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        if (blockDispatcher)
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Thread.Sleep(400)));
        Dispatcher.PushFrame(frame);
        if (!blockDispatcher) return null;

        var trace = _active ?? throw new InvalidOperationException("The dispatcher trace probe stopped before the timer smoke completed.");
        var timerEvent = trace.Summary.Split('|').FirstOrDefault(segment =>
            segment.Contains("dispatcher-queue-delay", StringComparison.Ordinal) &&
            segment.Contains("timer-tag=timer-backlog-smoke", StringComparison.Ordinal));
        if (timerEvent is null) return null;
        var delay = Regex.Match(timerEvent, @"delay-ms=([0-9]+(?:\.[0-9]+)?)").Groups[1].Value;
        var postToStart = Regex.Match(timerEvent, @"post-to-start-ms=([0-9]+(?:\.[0-9]+)?)").Groups[1].Value;
        var interval = Regex.Match(timerEvent, @"timer-interval-ms=([0-9]+(?:\.[0-9]+)?)").Groups[1].Value;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return double.TryParse(delay, System.Globalization.NumberStyles.Float, culture, out var delayMs) &&
               double.TryParse(postToStart, System.Globalization.NumberStyles.Float, culture, out var postToStartMs) &&
               double.TryParse(interval, System.Globalization.NumberStyles.Float, culture, out var intervalMs)
            ? (delayMs, postToStartMs, intervalMs)
            : null;
    }

    private int MessageCount => Volatile.Read(ref _messageCount);
    private int WindowProcedureMessageCount => Volatile.Read(ref _windowProcedureMessageCount);
    private bool ContainsMessage(uint message) => _messages.Contains(message);
    private bool ContainsWindowProcedureMessage(uint message) => _windowProcedureMessages.Contains(message);

    private IntPtr OnGetMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == HcAction && wParam.ToInt32() == PmRemove)
                RecordMessage(Marshal.PtrToStructure<NativeMessage>(lParam));
        }
        catch (Exception ex)
        {
            _callbackError ??= ex;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private IntPtr OnWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        nuint subclassId, IntPtr referenceData)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        try
        {
            Interlocked.Increment(ref _windowProcedureMessageCount);
            var endedAt = Stopwatch.GetTimestamp();
            RecordWindowProcedure(hwnd, message, wParam, lParam, startedAt, endedAt);
        }
        catch (Exception ex)
        {
            _callbackError ??= ex;
        }
        return result;
    }

    private void RecordMessage(NativeMessage message)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Exchange(ref _lastMessageAt, now);
        if (previous != 0)
        {
            var gapTicks = now - previous;
            UpdateMax(ref _maxGapTicks, gapTicks);
            if (gapTicks >= Stopwatch.Frequency / 10)
            {
                var hwndClass = GetWindowClass(message.Hwnd);
                AddSlowEvent(gapTicks, $"message-gap,from={TicksToMilliseconds(previous - _startedAt):F1}ms,from-ticks={previous},to={TicksToMilliseconds(now - _startedAt):F1}ms,to-ticks={now},gap={TicksToMilliseconds(gapTicks):F1}ms," +
                    $"next=0x{message.Message:X},hwnd={message.Hwnd:X},class={hwndClass},wp=0x{message.WParam:X},lp=0x{message.LParam:X}");
            }
        }
        if (_messages.Count < 512) _messages.Add(message.Message);
        Interlocked.Increment(ref _messageCount);
    }

    private void RecordWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, long startedAt, long endedAt)
    {
        if (_windowProcedureMessages.Count < 512) _windowProcedureMessages.Add(message);
        var elapsedTicks = endedAt - startedAt;
        if (elapsedTicks < Stopwatch.Frequency * SlowWindowProcedureThresholdTicks / 1000) return;
        var className = _windowClasses.TryGetValue(hwnd, out var existingClass) ? existingClass : GetWindowClass(hwnd);
        var key = (className, message);
        _slowWindowProcedureTimings.TryGetValue(key, out var timing);
        var elapsedMilliseconds = TicksToMilliseconds(elapsedTicks);
        _slowWindowProcedureTimings[key] = new WindowProcedureTiming(timing.Count + 1,
            timing.TotalMilliseconds + elapsedMilliseconds, Math.Max(timing.MaxMilliseconds, elapsedMilliseconds));
        if (elapsedMilliseconds >= 100)
            AddSlowEvent(elapsedTicks, $"wndproc={className}/0x{message:X},hwnd={hwnd:X},start={TicksToMilliseconds(startedAt - _startedAt):F1}ms,start-ticks={startedAt}," +
                $"end={TicksToMilliseconds(endedAt - _startedAt):F1}ms,end-ticks={endedAt},duration={elapsedMilliseconds:F1}ms,wp=0x{wParam:X},lp=0x{lParam:X}");
    }

    private void OnDispatcherOperationPosted(object? sender, DispatcherHookEventArgs eventArgs) =>
        _dispatcherOperationPostedAt[eventArgs.Operation] = Stopwatch.GetTimestamp();

    private void OnDispatcherOperationStarted(object? sender, DispatcherHookEventArgs eventArgs)
    {
        var startedAt = Stopwatch.GetTimestamp();
        if (_dispatcherOperationPostedAt.TryRemove(eventArgs.Operation, out var postedAt))
        {
            var postToStartTicks = startedAt - postedAt;
            var timer = GetDispatcherTimer(eventArgs.Operation);
            var queueDelayTicks = timer is null
                ? postToStartTicks
                : GetDispatcherTimerOverdueTicks(timer);
            if (queueDelayTicks >= Stopwatch.Frequency / 10)
            {
                var callback = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(eventArgs.Operation) as Delegate;
                var callbackName = callback is null ? "unknown" :
                    $"{callback.Method.DeclaringType?.FullName ?? "<global>"}.{callback.Method.Name}";
                AddSlowEvent(queueDelayTicks, $"dispatcher-queue-delay,priority={eventArgs.Operation.Priority},callback={callbackName}," +
                    $"{DescribeDispatcherOperationState(eventArgs.Operation)},posted={TicksToMilliseconds(postedAt - _startedAt):F1}ms,posted-ticks={postedAt}," +
                    $"started={TicksToMilliseconds(startedAt - _startedAt):F1}ms,started-ticks={startedAt},delay-ms={TicksToMilliseconds(queueDelayTicks):F1}" +
                    (timer is null ? string.Empty : $",post-to-start-ms={TicksToMilliseconds(postToStartTicks):F1}"));
            }
        }
        _dispatcherOperationStarts[eventArgs.Operation] = startedAt;
    }

    private static object? GetDispatcherOperationArgument(DispatcherOperation operation)
    {
        var arguments = typeof(DispatcherOperation).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(operation);
        if (arguments is Array values && values.Length == 1) arguments = values.GetValue(0);
        return arguments;
    }

    private static DispatcherTimer? GetDispatcherTimer(DispatcherOperation operation) =>
        GetDispatcherOperationArgument(operation) as DispatcherTimer;

    private static long GetDispatcherTimerOverdueTicks(DispatcherTimer timer)
    {
        var dueTimeField = typeof(DispatcherTimer).GetField("_dueTimeInTicks", BindingFlags.Instance | BindingFlags.NonPublic);
        if (dueTimeField?.GetValue(timer) is not int dueTime) return 0;
        // DispatcherTimer posts its inactive operation when started and promotes it at due time.
        // Measure only time after that deadline, not the configured interval itself.
        var overdueMilliseconds = Math.Max(0, unchecked(Environment.TickCount - dueTime));
        return (long)(overdueMilliseconds * (Stopwatch.Frequency / 1000d));
    }

    private void OnDispatcherOperationCompleted(object? sender, DispatcherHookEventArgs eventArgs) =>
        RecordDispatcherOperation(eventArgs.Operation);

    private void OnDispatcherOperationAborted(object? sender, DispatcherHookEventArgs eventArgs)
    {
        _dispatcherOperationPostedAt.TryRemove(eventArgs.Operation, out _);
        _dispatcherOperationStarts.Remove(eventArgs.Operation);
        GetDispatcherOperationAggregate(eventArgs.Operation.Priority).Aborted++;
    }

    private void RecordDispatcherOperation(DispatcherOperation operation)
    {
        if (!_dispatcherOperationStarts.Remove(operation, out var startedAt)) return;
        var endedAt = Stopwatch.GetTimestamp();
        var elapsedTicks = endedAt - startedAt;
        if (_dispatcherOperationIntervals.Count < MaxDispatcherOperationIntervals)
            _dispatcherOperationIntervals.Add(new DispatcherOperationInterval(operation.Priority, startedAt, endedAt));
        GetDispatcherOperationAggregate(operation.Priority).RecordCompleted(elapsedTicks);
        var operationKind = DescribeDispatcherOperationKind(operation);
        var kindKey = (operation.Priority, operationKind);
        if (!_dispatcherOperationKinds.TryGetValue(kindKey, out var kindAggregate))
            _dispatcherOperationKinds.Add(kindKey, kindAggregate = new DispatcherOperationAggregate());
        kindAggregate.RecordCompleted(elapsedTicks);
        if (elapsedTicks < Stopwatch.Frequency / 10) return;
        var callback = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(operation) as Delegate;
        var callbackName = callback is null ? "unknown" :
            $"{callback.Method.DeclaringType?.FullName ?? "<global>"}.{callback.Method.Name}";
        var targetName = callback?.Target?.GetType().Name ?? "static";
        var stateDescription = DescribeDispatcherOperationState(operation);
        AddSlowEvent(elapsedTicks, $"dispatcher-operation,priority={operation.Priority},callback={callbackName},target={targetName},{stateDescription}," +
            $"start={TicksToMilliseconds(startedAt - _startedAt):F1}ms,start-ticks={startedAt},end={TicksToMilliseconds(endedAt - _startedAt):F1}ms,end-ticks={endedAt}," +
            $"duration={TicksToMilliseconds(elapsedTicks):F1}ms");
    }

    internal string DescribeDispatcherOperationOverlap(long intervalStart, long intervalEnd)
    {
        var totals = new Dictionary<DispatcherPriority, (int Operations, long OverlapTicks, long MaxOverlapTicks)>();
        foreach (var operation in _dispatcherOperationIntervals)
        {
            if (operation.StartedAt >= intervalEnd || operation.EndedAt <= intervalStart) continue;
            var overlapTicks = Math.Min(operation.EndedAt, intervalEnd) - Math.Max(operation.StartedAt, intervalStart);
            var total = totals.GetValueOrDefault(operation.Priority);
            totals[operation.Priority] = (total.Operations + 1, total.OverlapTicks + overlapTicks,
                Math.Max(total.MaxOverlapTicks, overlapTicks));
        }
        return string.Join('|', totals.OrderBy(pair => pair.Key).Select(pair =>
            $"{pair.Key}:operations={pair.Value.Operations},overlap-ms={TicksToMilliseconds(pair.Value.OverlapTicks):F1},max-ms={TicksToMilliseconds(pair.Value.MaxOverlapTicks):F1}"));
    }

    private DispatcherOperationAggregate GetDispatcherOperationAggregate(DispatcherPriority priority)
    {
        if (!_dispatcherOperationTotals.TryGetValue(priority, out var aggregate))
            _dispatcherOperationTotals.Add(priority, aggregate = new DispatcherOperationAggregate());
        return aggregate;
    }

    private static string DescribeDispatcherOperationState(DispatcherOperation operation)
    {
        var arguments = GetDispatcherOperationArgument(operation);
        if (arguments is DispatcherTimer timer)
        {
            var tickField = typeof(DispatcherTimer).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = (tickField?.GetValue(timer) as Delegate)?.GetInvocationList() ?? [];
            var tickTargets = string.Join(';', handlers.Select(handler =>
                $"{handler.Method.DeclaringType?.FullName ?? "<global>"}.{handler.Method.Name}"));
            var tag = timer.Tag is null ? "null" : timer.Tag.ToString();
            var priority = typeof(DispatcherTimer).GetField("_priority", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(timer);
            return $"state=DispatcherTimer,timer-interval-ms={timer.Interval.TotalMilliseconds:F0},timer-priority={priority ?? "unknown"},timer-tag={tag},timer-ticks={tickTargets}";
        }
        return arguments is null
            ? "state=null"
            : $"state={arguments.GetType().FullName}:{arguments}";
    }

    private static string DescribeDispatcherOperationKind(DispatcherOperation operation)
    {
        var argument = GetDispatcherOperationArgument(operation);
        if (argument is DispatcherTimer timer)
        {
            var tickField = typeof(DispatcherTimer).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = (tickField?.GetValue(timer) as Delegate)?.GetInvocationList() ?? [];
            return handlers.Length == 0 ? "DispatcherTimer.Tick:unknown" : string.Join(';', handlers.Select(handler =>
                $"{handler.Method.DeclaringType?.FullName ?? "<global>"}.{handler.Method.Name}"));
        }

        var callback = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(operation) as Delegate;
        return callback is null ? "unknown" :
            $"{callback.Method.DeclaringType?.FullName ?? "<global>"}.{callback.Method.Name}";
    }

    private void AddSlowEvent(long durationTicks, string description)
    {
        if (_slowEvents.Count < 128)
        {
            _slowEvents.Add(new SlowEvent(durationTicks, description));
            return;
        }

        var shortestIndex = 0;
        for (var index = 1; index < _slowEvents.Count; index++)
            if (_slowEvents[index].DurationTicks < _slowEvents[shortestIndex].DurationTicks)
                shortestIndex = index;
        if (durationTicks > _slowEvents[shortestIndex].DurationTicks)
            _slowEvents[shortestIndex] = new SlowEvent(durationTicks, description);
    }

    private void AddWindow(IntPtr hwnd)
    {
        if (_windowClasses.ContainsKey(hwnd)) return;
        _windowClasses[hwnd] = GetWindowClass(hwnd);
        if (SetWindowSubclass(hwnd, _subclassProcedure, (nuint)0xED1D1, IntPtr.Zero))
            _subclassedWindows.Add(hwnd);
    }

    private static IntPtr GetDispatcherMessageWindowHandle()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var wrapperField = typeof(Dispatcher).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find the WPF Dispatcher message window.");
        var wrapper = wrapperField.GetValue(dispatcher)
            ?? throw new InvalidOperationException("The WPF Dispatcher message window is unavailable.");
        return wrapper.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public)?.GetValue(wrapper) as IntPtr?
            ?? throw new InvalidOperationException("Could not read the WPF Dispatcher message-window handle.");
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "thread-message";
        var name = new StringBuilder(128);
        var length = GetClassName(hwnd, name, name.Capacity);
        return length > 0 ? name.ToString() : "unknown";
    }

    private string WindowProcedureSummary() => string.Join('|', _slowWindowProcedureTimings
        .OrderByDescending(entry => entry.Value.TotalMilliseconds)
        .Take(32)
        .Select(entry => $"{entry.Key.ClassName}/0x{entry.Key.Message:X}:slow-calls(>=10ms)={entry.Value.Count}," +
            $"total-ms={entry.Value.TotalMilliseconds:F1},max-ms={entry.Value.MaxMilliseconds:F1}"));

    private static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static void UpdateMax(ref long location, long candidate)
    {
        var current = Interlocked.Read(ref location);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref location, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }

    public void Dispose()
    {
        _dispatcherHooks.OperationPosted -= OnDispatcherOperationPosted;
        _dispatcherHooks.OperationStarted -= OnDispatcherOperationStarted;
        _dispatcherHooks.OperationCompleted -= OnDispatcherOperationCompleted;
        _dispatcherHooks.OperationAborted -= OnDispatcherOperationAborted;
        _dispatcherOperationPostedAt.Clear();
        for (var i = _subclassedWindows.Count - 1; i >= 0; i--)
            _ = RemoveWindowSubclass(_subclassedWindows[i], _subclassProcedure, (nuint)0xED1D1);
        if (_hook != IntPtr.Zero) _ = UnhookWindowsHookEx(_hook);
        if (ReferenceEquals(_active, this)) _active = null;
        GC.KeepAlive(_hookProcedure);
        GC.KeepAlive(_subclassProcedure);
    }

    private readonly record struct WindowProcedureTiming(int Count, double TotalMilliseconds, double MaxMilliseconds);
    private readonly record struct SlowEvent(long DurationTicks, string Description);
    private readonly record struct DispatcherOperationInterval(DispatcherPriority Priority, long StartedAt, long EndedAt);

    private sealed class DispatcherOperationAggregate
    {
        internal long Completed { get; private set; }
        internal long Aborted { get; set; }
        internal long TotalTicks { get; private set; }
        internal long MaxTicks { get; private set; }

        internal void RecordCompleted(long elapsedTicks)
        {
            Completed++;
            TotalTicks += elapsedTicks;
            MaxTicks = Math.Max(MaxTicks, elapsedTicks);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal IntPtr Hwnd;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        nuint subclassId, IntPtr referenceData);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookProcedure callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMessage message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string message);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProcedure callback, nuint subclassId, IntPtr referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProcedure callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
