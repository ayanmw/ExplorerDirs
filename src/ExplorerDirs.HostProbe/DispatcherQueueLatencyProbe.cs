using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

internal sealed class DispatcherQueueLatencyProbe : IDisposable
{
    private static readonly DispatcherPriority[] Priorities =
    [DispatcherPriority.Normal, DispatcherPriority.Input, DispatcherPriority.ContextIdle];

    private readonly Dispatcher _dispatcher;
    private readonly Timer _timer;
    private readonly long[] _queued = new long[Priorities.Length];
    private readonly long[] _completed = new long[Priorities.Length];
    private readonly long[] _maxQueueDelayTicks = new long[Priorities.Length];
    private readonly long[] _postFailures = new long[Priorities.Length];
    private readonly ConcurrentQueue<string> _slowQueueDelays = new();
    private readonly object _maxQueueDelayLock = new();
    private readonly QueueLatencySample[] _maxQueueDelaySamples = new QueueLatencySample[Priorities.Length];
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private long _lastTimerCallbackAt;
    private long _maxTimerCallbackGapTicks;
    private int _timerCallbacks;
    private int _overlappingTimerCallbacks;
    private int _callbackActive;
    private int _slowQueueDelayCount;
    private bool _disposed;

    internal DispatcherQueueLatencyProbe(Dispatcher dispatcher, TimeSpan interval)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(OnTimer, null, interval, interval);
    }

    internal int TimerCallbacks => Volatile.Read(ref _timerCallbacks);
    internal int OverlappingTimerCallbacks => Volatile.Read(ref _overlappingTimerCallbacks);
    internal double MaximumQueueDelayMilliseconds => TicksToMilliseconds(
        Enumerable.Range(0, Priorities.Length).Max(index => Interlocked.Read(ref _maxQueueDelayTicks[index])));

    internal bool HasSamplesForEveryPriority => Enumerable.Range(0, Priorities.Length)
        .All(index => Interlocked.Read(ref _queued[index]) > 0 && Interlocked.Read(ref _completed[index]) > 0);

    internal bool IsDrained => Enumerable.Range(0, Priorities.Length)
        .All(index => Interlocked.Read(ref _completed[index]) + Interlocked.Read(ref _postFailures[index]) >=
            Interlocked.Read(ref _queued[index]));

    internal string Summary =>
        $"timer-callbacks={TimerCallbacks},overlaps={OverlappingTimerCallbacks},max-timer-callback-gap-ms={TicksToMilliseconds(Interlocked.Read(ref _maxTimerCallbackGapTicks)):F1}," +
        string.Join('|', Enumerable.Range(0, Priorities.Length).Select(index =>
            $"{Priorities[index]}:queued={Interlocked.Read(ref _queued[index])},completed={Interlocked.Read(ref _completed[index])},post-failures={Interlocked.Read(ref _postFailures[index])},max-queue-delay-ms={TicksToMilliseconds(Interlocked.Read(ref _maxQueueDelayTicks[index])):F1}")) +
        $",max-lags=[{DescribeMaxQueueDelays()}],slow-lags=[{string.Join(';', _slowQueueDelays)}]";

    internal static bool RunSmoke()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var probe = new DispatcherQueueLatencyProbe(dispatcher, TimeSpan.FromMilliseconds(10));
        var frame = new DispatcherFrame();
        var finishTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        finishTimer.Tick += (_, _) =>
        {
            finishTimer.Stop();
            probe.Stop();
            if (probe.IsDrained) frame.Continue = false;
            else WaitUntilDrained(probe, frame);
        };

        finishTimer.Start();
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Thread.Sleep(120)));
        Dispatcher.PushFrame(frame);
        return probe.HasSamplesForEveryPriority && probe.IsDrained && probe.TimerCallbacks > 0 &&
            probe.OverlappingTimerCallbacks == 0 && probe.MaximumQueueDelayMilliseconds >= 50;
    }

    internal void Stop()
    {
        if (_disposed) return;
        _disposed = true;
        using var callbacksFinished = new ManualResetEvent(false);
        if (_timer.Dispose(callbacksFinished))
            callbacksFinished.WaitOne(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => Stop();

    private static void WaitUntilDrained(DispatcherQueueLatencyProbe probe, DispatcherFrame frame)
    {
        var deadline = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (probe.IsDrained || deadline.Elapsed >= TimeSpan.FromSeconds(3))
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
    }

    private void OnTimer(object? state)
    {
        if (Interlocked.Exchange(ref _callbackActive, 1) != 0)
        {
            Interlocked.Increment(ref _overlappingTimerCallbacks);
            return;
        }

        try
        {
            var postedAt = Stopwatch.GetTimestamp();
            var previous = Interlocked.Exchange(ref _lastTimerCallbackAt, postedAt);
            if (previous != 0) UpdateMax(ref _maxTimerCallbackGapTicks, postedAt - previous);
            Interlocked.Increment(ref _timerCallbacks);
            for (var index = 0; index < Priorities.Length; index++)
            {
                var sampleIndex = index;
                Interlocked.Increment(ref _queued[index]);
                try
                {
                    _dispatcher.BeginInvoke(Priorities[sampleIndex], new Action(() =>
                    {
                        var startedAt = Stopwatch.GetTimestamp();
                        RecordQueueDelay(sampleIndex, postedAt, startedAt);
                        Interlocked.Increment(ref _completed[sampleIndex]);
                    }));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref _postFailures[sampleIndex]);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _callbackActive, 0);
        }
    }

    private static void UpdateMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private void RecordQueueDelay(int index, long postedAt, long startedAt)
    {
        var delayTicks = startedAt - postedAt;
        lock (_maxQueueDelayLock)
        {
            if (delayTicks > _maxQueueDelayTicks[index])
            {
                Interlocked.Exchange(ref _maxQueueDelayTicks[index], delayTicks);
                _maxQueueDelaySamples[index] = new QueueLatencySample(postedAt, startedAt);
            }
        }
        if (delayTicks < Stopwatch.Frequency / 10 || Interlocked.Increment(ref _slowQueueDelayCount) > 32) return;
        _slowQueueDelays.Enqueue($"{Priorities[index]}:posted={TicksToMilliseconds(postedAt - _startedAt):F1}ms,started={TicksToMilliseconds(startedAt - _startedAt):F1}ms,delay={TicksToMilliseconds(delayTicks):F1}ms");
    }

    private string DescribeMaxQueueDelays()
    {
        lock (_maxQueueDelayLock)
        {
            return string.Join('|', Enumerable.Range(0, Priorities.Length).Select(index =>
            {
                var sample = _maxQueueDelaySamples[index];
                if (sample.StartedAt == 0) return $"{Priorities[index]}:none";
                var delayTicks = sample.StartedAt - sample.PostedAt;
                return $"{Priorities[index]}:posted={TicksToMilliseconds(sample.PostedAt - _startedAt):F1}ms,started={TicksToMilliseconds(sample.StartedAt - _startedAt):F1}ms,delay={TicksToMilliseconds(delayTicks):F1}ms";
            }));
        }
    }

    private readonly record struct QueueLatencySample(long PostedAt, long StartedAt);

    private static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
