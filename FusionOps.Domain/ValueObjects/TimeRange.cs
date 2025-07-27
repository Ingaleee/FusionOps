using System;

namespace FusionOps.Domain.ValueObjects;

/// <summary>
/// Immutable time interval expressed in UTC.
/// </summary>
public readonly record struct TimeRange
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end < start)
            throw new ArgumentException("End must be after start", nameof(end));

        Start = start;
        End = end;
    }

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(DateTimeOffset moment) => moment >= Start && moment <= End;

    public TimeRange[] SplitByDay()
    {
        var list = new System.Collections.Generic.List<TimeRange>();
        var currentStart = Start;
        while (currentStart.Date < End.Date)
        {
            var dayEnd = currentStart.Date.AddDays(1).AddTicks(-1);
            list.Add(new TimeRange(currentStart, dayEnd));
            currentStart = dayEnd.AddTicks(1);
        }
        list.Add(new TimeRange(currentStart, End));
        return list.ToArray();
    }

    public override string ToString() => $"{Start:u} - {End:u}";
}