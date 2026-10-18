using IC2.Engine.Battle.Tactical;
using IC2.Engine.Presentation;
using IC2.Slice.Audio;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class TacticalPlaybackAudioTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void Displayed_entry_delivers_every_exchange_once_in_order_through_terminal_and_half_rounds(double delay, bool finish)
    {
        var (session, frames) = TacticalSoundCuesTests.Battle();
        var delivered = new List<string>();
        var adapter = new TacticalPlaybackAudio(session.Ruleset, () => true, delivered.Add);
        var queue = new TacticalPlayback { ShotDelay = delay, AttackDelay = delay };
        queue.ExchangeEntered += adapter.ExchangeEntered;
        queue.Begin(frames);
        if (finish) { queue.Advance(0.25); queue.Finish(); }
        else for (var i = 0; queue.IsPlaying && i < 100000; i++) queue.Advance(0.1);
        Assert.False(queue.IsPlaying);
        queue.Finish(); queue.Advance(100); queue.Begin(frames); queue.Finish();
        Assert.Equal(frames.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent)
            .Select(f => TacticalSoundCues.ForExchange(f, session.Ruleset)), delivered);
        Assert.True(frames.Count(f => f.Kind == TacticalFrameKind.HalfRound) > 1);
        Assert.Equal(TacticalFrameKind.Reconciliation, queue.CurrentFrame!.Kind);
        var before = delivered.Count;
        queue.Begin(new[] { frames[0] with { BattleId = "next" } }); queue.Finish();
        Assert.Equal(before, delivered.Count);
        Assert.Null(queue.CurrentExchange);
    }

    [Fact]
    public void Muting_at_displayed_dispatch_drops_only_muted_exchanges_and_nonexchange_is_silent()
    {
        var (session, frames) = TacticalSoundCuesTests.Battle();
        var enabled = false;
        var delivered = new List<string>();
        var adapter = new TacticalPlaybackAudio(session.Ruleset, () => enabled, delivered.Add);
        var exchanges = frames.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent).Take(3).ToArray();
        Assert.Equal(3, exchanges.Length);
        var queue = new TacticalPlayback { ShotDelay = 1, AttackDelay = 1 };
        queue.ExchangeEntered += adapter.ExchangeEntered;
        queue.Begin(exchanges);
        Assert.Empty(delivered);
        enabled = true;
        queue.Advance(1);
        Assert.Single(delivered);
        enabled = false;
        queue.Finish();
        Assert.Single(delivered);
        enabled = true;
        foreach (var frame in frames.Where(f => f.Event is not (TacticalShotEvent or TacticalMeleeEvent))) adapter.ExchangeEntered(frame);
        Assert.Single(delivered);
    }
}
