using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.App.Tests;

/// <summary>Time provider whose timers fire only when the test advances the clock.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now;

    public ManualTimeProvider()
        : this(DateTimeOffset.UnixEpoch)
    {
    }

    public ManualTimeProvider(DateTimeOffset start)
    {
        now = start;
    }

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        Track(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that becomes due, earliest first.</summary>
    public void Advance(TimeSpan delta)
    {
        var target = now + delta;
        while (FindNextDue(target) is { } next)
        {
            now = next.DueAt!.Value;
            next.Fire();
        }

        now = target;
    }

    internal void Remove(ManualTimer timer) => timers.Remove(timer);

    internal void Track(ManualTimer timer) => timers.Add(timer);

    private ManualTimer? FindNextDue(DateTimeOffset target)
    {
        ManualTimer? next = null;
        foreach (var timer in timers)
        {
            if (timer.DueAt is not { } dueAt || dueAt > target)
            {
                continue;
            }

            if (next?.DueAt is not { } nextDue || dueAt < nextDue)
            {
                next = timer;
            }
        }

        return next;
    }

    internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        public void Dispose()
        {
            DueAt = null;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void Fire()
        {
            DueAt = null;
            callback(state);
        }
    }
}
