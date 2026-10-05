using IC2.Engine.Battle;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T116.md</c> Done-when 1: pins <see cref="IC2.Slice.Screens.BattleReportRouter"/>'s rule
/// on hand-built <see cref="BattleResult"/>s, with no engine, session or Godot run needed — the same seam
/// <c>HotseatHandoffDetectorTests</c> and <c>BattleResultViewModelTests</c> already use. Fails before the
/// router exists (it is a new class), passes after.
/// </summary>
public sealed class BattleReportRouterTests
{
    /// <summary>
    /// One human seat: every battle against it is shown now, in the order fought, and nothing is held —
    /// the one-human behaviour T116 must not change.
    /// </summary>
    [Fact]
    public void One_human_seat_shows_every_battle_against_it_now_in_the_order_fought()
    {
        var router = new IC2.Slice.Screens.BattleReportRouter();
        var first = Battle("carthage", "rome", attackerId: "carthage-army-1", defenderId: "rome-army-1");
        var second = Battle("carthage", "rome", attackerId: "carthage-army-2", defenderId: "rome-army-2");

        var shown = router.Route(new[] { first, second }, new[] { "rome" }, "rome");

        Assert.Equal(new[] { first, second }, shown);
        Assert.Empty(router.HeldFor("rome"));
    }

    /// <summary>
    /// Two human seats with an AI seat between. The battle against the <em>incoming</em> seat is shown
    /// now; the battle against the <em>outgoing</em> seat is held and released when that seat is next
    /// active, in the order fought, ahead of the battles of the call that made it active.
    /// </summary>
    [Fact]
    public void A_battle_against_the_outgoing_seat_is_held_and_released_when_that_seat_is_next_active()
    {
        var router = new IC2.Slice.Screens.BattleReportRouter();
        var againstSeleucid = Battle("carthage", "seleucid", attackerId: "carthage-army-2", defenderId: "seleucid-army-1");
        var againstRome = Battle("carthage", "rome", attackerId: "carthage-army-3", defenderId: "rome-army-1");
        var humans = new[] { "rome", "seleucid" };

        // Rome ends; carthage (the AI between them) fought both, then seleucid is active: only its own
        // battle is shown now, and Rome's is held for Rome.
        var afterRomeEnds = router.Route(new[] { againstSeleucid, againstRome }, humans, "seleucid");
        Assert.Equal(new[] { againstSeleucid }, afterRomeEnds);
        Assert.Equal(new[] { againstRome }, router.HeldFor("rome"));
        Assert.Empty(router.HeldFor("seleucid"));

        // Seleucid ends; the call that makes Rome active has a battle of its own. The held report comes
        // first, then that call's own battles.
        var ownBattleOfTheCall = Battle("macedonia", "rome", attackerId: "macedonia-army-1", defenderId: "rome-army-2");
        var afterSeleucidEnds = router.Route(new[] { ownBattleOfTheCall }, humans, "rome");
        Assert.Equal(new[] { againstRome, ownBattleOfTheCall }, afterSeleucidEnds);
        Assert.Empty(router.HeldFor("rome"));
    }

    /// <summary>An AI-versus-AI battle has no human side, so T25's behaviour is kept: shown now.</summary>
    [Fact]
    public void An_AI_versus_AI_battle_is_shown_now()
    {
        var router = new IC2.Slice.Screens.BattleReportRouter();
        var aiBattle = Battle("carthage", "gaul", attackerId: "carthage-army-1", defenderId: "gaul-army-1");

        var shown = router.Route(new[] { aiBattle }, new[] { "rome" }, "rome");

        Assert.Equal(new[] { aiBattle }, shown);
    }

    /// <summary>A held battle whose seat is no longer human (it fell, or was handed to the AI) is dropped.</summary>
    [Fact]
    public void A_held_battle_whose_seat_is_no_longer_human_is_dropped()
    {
        var router = new IC2.Slice.Screens.BattleReportRouter();
        var heldForRome = Battle("carthage", "rome", attackerId: "carthage-army-3", defenderId: "rome-army-1");

        router.Route(new[] { heldForRome }, new[] { "rome", "seleucid" }, "seleucid");
        Assert.Equal(new[] { heldForRome }, router.HeldFor("rome"));

        // Rome is handed to the AI T87-style: it is gone from the human seats before its own turn comes.
        var shown = router.Route(Array.Empty<BattleResult>(), new[] { "seleucid" }, "seleucid");

        Assert.Empty(shown);
        Assert.Empty(router.HeldFor("rome"));
    }

    /// <summary>
    /// A battle between two human seats goes to every human seat on either side: the active one sees it
    /// now, the other has it held for its own turn.
    /// </summary>
    [Fact]
    public void A_battle_between_two_human_seats_is_shown_to_the_active_one_and_held_for_the_other()
    {
        var router = new IC2.Slice.Screens.BattleReportRouter();
        var humanVersusHuman = Battle("rome", "seleucid", attackerId: "rome-army-1", defenderId: "seleucid-army-1");

        var shown = router.Route(new[] { humanVersusHuman }, new[] { "rome", "seleucid" }, "rome");

        Assert.Equal(new[] { humanVersusHuman }, shown);
        Assert.Equal(new[] { humanVersusHuman }, router.HeldFor("seleucid"));
    }

    private static BattleResult Battle(
        string attackerNation, string defenderNation, string attackerId, string defenderId) => new(
        Kind: BattleKind.Field,
        AttackerId: attackerId,
        DefenderId: defenderId,
        AttackerNationId: attackerNation,
        DefenderNationId: defenderNation,
        AttackerPower: 120,
        DefenderPower: 80,
        Winner: BattleSide.Attacker,
        AppliedDefeatOutcome: DefeatOutcome.Destroyed,
        LoserFate: LoserFate.Destroyed,
        WinnerCasualties: 12,
        LoserCasualties: 80,
        UnitCasualties: ValueList<UnitCasualty>.Empty,
        Promotions: ValueList<UnitPromotion>.Empty,
        AbsorbedMoney: 250,
        AbsorbedSupplyTons: 10,
        WinnerUnityDelta: 5,
        LoserUnityDelta: -10,
        WinnerShipsLost: 0,
        WinnerConditionLost: 0,
        WinnerUnitsLost: 0,
        PeaceTreatyFired: false,
        PeaceTreatyOffered: false,
        Scatter: null);
}
