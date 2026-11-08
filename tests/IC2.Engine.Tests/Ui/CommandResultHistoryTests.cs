using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class CommandResultHistoryTests
{
    [Fact]
    public void Retains_exactly_the_last_100_complete_blocks_including_repeated_results()
    {
        var history = new CommandResultHistory();
        history.SetViewer("rome");
        for (var i = 0; i < 105; i++) history.Append($"result {i}\nsecond line");
        Assert.Equal(100, history.Entries.Count);
        Assert.Equal("result 5\nsecond line", history.Entries[0]);
        Assert.Equal("result 104\nsecond line", history.Entries[^1]);
        history.Append("same"); history.Append("same");
        Assert.Equal(new[] { "same", "same" }, history.Entries.TakeLast(2));
        history.Append(string.Empty);
        Assert.Equal(100, history.Entries.Count);
        Assert.Equal(string.Empty, history.LatestResult);
    }

    [Fact]
    public void Same_viewer_keeps_history_but_player_and_observer_boundaries_clear_it()
    {
        var history = new CommandResultHistory();
        history.SetViewer("rome"); history.Append("private Rome result");
        history.SetViewer("rome"); Assert.Single(history.Entries);
        history.SetViewer("carthage"); Assert.Empty(history.Entries);
        Assert.Empty(history.LatestResult);
        history.Append("private Carthage result");
        history.SetViewer("observer"); Assert.Empty(history.Entries);
        history.Append("observer information");
        history.SetViewer("rome"); Assert.Empty(history.Entries);
    }
}
