namespace Pipeline.Core;

/// <summary>
/// All sources are Turkish, so list dates use Turkey time (a fixed UTC+3 since 2016).
/// Cosmos compares and sorts dates as ISO text, which is only correct when every stored
/// date has the same offset and precision; <see cref="Normalize"/> guarantees both.
/// </summary>
public static class TurkeyTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>The same moment in Turkey time, to whole seconds.</summary>
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var turkey = value.ToOffset(Offset);
        return new DateTimeOffset(turkey.Ticks - turkey.Ticks % TimeSpan.TicksPerSecond, Offset);
    }

    /// <summary>Midnight at the start of the given day in Turkey.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), Offset);
}
