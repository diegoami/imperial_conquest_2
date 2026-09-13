using System.Text.Json;

namespace IC2.Engine.Tests.Calendar;

/// <summary>One row of the DoD 1 hand-computed expected calendar sequence.</summary>
public sealed record ExpectedCalendarEntry(int Turn, int Week, int SeasonIndex, int YearBc);

/// <summary>Loads <see cref="CalendarTestPaths.ExpectedCalendarSequenceFile"/>.</summary>
public static class ExpectedCalendarSequence
{
    private static readonly Lazy<IReadOnlyList<ExpectedCalendarEntry>> LazyEntries = new(Load);

    /// <summary>The 48-round expected sequence, in round order.</summary>
    public static IReadOnlyList<ExpectedCalendarEntry> Entries => LazyEntries.Value;

    private static IReadOnlyList<ExpectedCalendarEntry> Load()
    {
        using var stream = File.OpenRead(CalendarTestPaths.ExpectedCalendarSequenceFile);
        using var document = JsonDocument.Parse(stream);

        var entries = new List<ExpectedCalendarEntry>();
        foreach (var element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new ExpectedCalendarEntry(
                element.GetProperty("turn").GetInt32(),
                element.GetProperty("week").GetInt32(),
                element.GetProperty("seasonIndex").GetInt32(),
                element.GetProperty("yearBc").GetInt32()));
        }

        return entries;
    }
}
