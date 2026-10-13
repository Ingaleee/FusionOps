using System;

namespace FusionOps.Domain.ValueObjects;

/// <summary>
/// Immutable time interval expressed in UTC.
/// </summary>
public sealed record TimeRange
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            throw new ArgumentException("End must be after start", nameof(end));

        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(DateTimeOffset moment) => moment >= Start && moment < End;

    public TimeRange[] SplitByDay()
    {
        var list = new System.Collections.Generic.List<TimeRange>();
        var currentStart = Start;
        while (currentStart < End)
        {
            var nextMidnight = new DateTimeOffset(currentStart.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            var segmentEnd = nextMidnight < End ? nextMidnight : End;
            list.Add(new TimeRange(currentStart, segmentEnd));
            currentStart = segmentEnd;
        }
        return list.ToArray();
    }

    public override string ToString() => $"{Start:u} - {End:u}";
}
