using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Cities.Capture;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// Bug #199 (T69), Done-when 2 and 3: a nation eliminated mid-turn through the real capture path — a
/// <see cref="BesiegeCityCommand"/> that takes its last city, the same production path
/// <c>BesiegeCityCommandTests.DoD02_ACaptureThroughTheCommandEliminatesOneNationAndLeavesNothingDangling</c>
/// exercises — is rejected as a counterparty by every one of the five handlers afterward, changing
/// nothing.
/// </summary>
/// <remarks>
/// Rework round 1: this replaces the "kept as history" test PR #364's review (B1) found pinned the
/// opposite of the original's own rule. The exhaustive Done-when 4 mechanics — both call sites, the
/// <c>JustEliminated</c> gate, the two-entity probe, and the peace/cooldown-clears-to-0 case — live in
/// <c>tests/IC2.Engine.Tests/Cities/Capture/EliminationRelationResetTests.cs</c>; this file keeps only the
/// one integration check that the real command-driven path resets a relation too, plus DoD 3's five-
/// handler cross-task scenario. N3: the pending offer is created <em>before</em> the siege that eliminates
/// its proposer, matching bug #199's own ordering (roll first, then eliminate) rather than being injected
/// afterward.
/// </remarks>
public sealed class EliminationDiplomacyTests
{
    private const string ConquerorId = "north";
    private const string DoomedId = "south";
    private const string ThirdPartyId = "east";
    private const string LastCityId = "lastcity";
    private const string BesiegerId = "besieger";

    /// <summary>
    /// Builds a three-nation state where <see cref="ConquerorId"/> is at war with, and adjacent to, the
    /// last city of <see cref="DoomedId"/> — which already holds a trade relation with the uninvolved
    /// <see cref="ThirdPartyId"/> and a pending trade offer to <see cref="ConquerorId"/>, both already on
    /// the books before the siege — and a besieging army strong enough to take the city outright.
    /// </summary>
    private static GameState Fixture()
    {
        var conqueror = CaptureTestbed.Nation(ConquerorId, unity: 500, capitalCityId: "north-capital");
        var doomed = CaptureTestbed.Nation(DoomedId, unity: 668, capitalCityId: LastCityId);
        var thirdParty = CaptureTestbed.Nation(ThirdPartyId, unity: 500, capitalCityId: "east-capital");

        var lastCity = CaptureTestbed.City(
            LastCityId, "Last City", 3, 4, DoomedId, DoomedId,
            loyalty: 40, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var northCapital = CaptureTestbed.City(
            "north-capital", "North Capital", 0, 0, ConquerorId, ConquerorId,
            loyalty: 80, fortificationCode: 0, populationThousands: 15, maxPopulationThousands: 30, tribute: 5);
        var eastCapital = CaptureTestbed.City(
            "east-capital", "East Capital", 7, 0, ThirdPartyId, ThirdPartyId,
            loyalty: 80, fortificationCode: 0, populationThousands: 15, maxPopulationThousands: 30, tribute: 5);

        var besieger = BattleTestbed.Army(
            BesiegerId, ConquerorId, 3, 3, morale: 68, money: 0, supplyTons: 0,
            BattleTestbed.Unit("heavy_infantry", 400_000, 6, "1st Guards Battalion"));

        var state = StateWith(
            new[] { conqueror, doomed, thirdParty },
            new[] { lastCity, northCapital, eastCapital },
            new[] { besieger });

        var codes = ToyRuleset.Diplomacy.StateCodes;
        state = state with
        {
            Relations = state.Relations.WithRelation(DoomedId, ThirdPartyId, codes.Trade),
            // N3: the offer predates the elimination -- bug #199's own ordering is roll first, then
            // eliminate, not the other way around.
            PendingOffer = new PendingDiplomaticOffer(DoomedId, codes.Trade),
        };

        return AtWar(state, ConquerorId, DoomedId);
    }

    /// <summary>
    /// The shared setup: dispatches the siege that actually eliminates <see cref="DoomedId"/>, and hands
    /// back the resulting state plus the codes DoD 3 and DoD 4 assert against.
    /// </summary>
    private static (GameState State, RelationStateCodes Codes) EliminateDoomedNation()
    {
        var state = Fixture();
        Assert.NotNull(state.PendingOffer);

        var result = Dispatcher().Dispatch(state, new BesiegeCityCommand(ConquerorId, BesiegerId, LastCityId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.True(result.State.NationById(DoomedId)!.Eliminated);
        // The offer isn't cleared by the siege itself -- only PendingOfferSystem does that, at the next
        // human turn start -- so it is still there for DoD 3's accept-offer scenario below.
        Assert.NotNull(result.State.PendingOffer);

        return (result.State, ToyRuleset.Diplomacy.StateCodes);
    }

    /// <summary>
    /// DoD 4's integration slice: the trade relation <see cref="DoomedId"/> held with
    /// <see cref="ThirdPartyId"/> is reset to the broken-trade cooldown by the real elimination path, the
    /// original's own rule (rework round 1, B1) — not kept as history, the opposite behaviour the review
    /// found pinned here before.
    /// </summary>
    [Fact]
    public void DoD04_EliminationResetsAnExistingRelation_ToItsCooldown()
    {
        var (state, _) = EliminateDoomedNation();

        Assert.Equal(ToyRuleset.Diplomacy.CooldownAfterBrokenTrade, state.Relations.Get(DoomedId, ThirdPartyId));
    }

    /// <summary>
    /// DoD 3: once <see cref="DoomedId"/> is eliminated, a fresh <see cref="ProposeTradeCommand"/> against
    /// it is rejected with the shared counterparty-eliminated code and changes nothing.
    /// </summary>
    [Fact]
    public void DoD03_ProposeTrade_AgainstTheJustEliminatedNation_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new ProposeTradeCommand(ConquerorId, DoomedId));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>DoD 3: the same, for <see cref="ProposeAllianceCommand"/>.</summary>
    [Fact]
    public void DoD03_ProposeAlliance_AgainstTheJustEliminatedNation_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new ProposeAllianceCommand(ConquerorId, DoomedId));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>
    /// DoD 3: the pending offer that <see cref="Fixture"/> created before the siege, naming the
    /// now-eliminated <see cref="DoomedId"/> as proposer, is rejected the same way when accepted afterward
    /// — the gap T17's <c>PendingOfferSystem</c> already closes on the roll (<c>aliveOk</c>) is the accept
    /// path, not the roll.
    /// </summary>
    [Fact]
    public void DoD03_AcceptPendingOffer_OnAnOfferThatPredatesTheElimination_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(ConquerorId));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>DoD 3, N2: <see cref="DeclareWarCommand"/> against the just-eliminated nation.</summary>
    [Fact]
    public void DoD03_DeclareWar_AgainstTheJustEliminatedNation_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new DeclareWarCommand(ConquerorId, DoomedId));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>DoD 3, N2: <see cref="MakePeaceCommand"/> against the just-eliminated nation.</summary>
    [Fact]
    public void DoD03_MakePeace_AgainstTheJustEliminatedNation_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new MakePeaceCommand(ConquerorId, DoomedId));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }
}
