using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Cities.Capture;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// Bug #199 (T69), Done-when 3 and 4: a nation eliminated mid-turn through the real capture path — a
/// <see cref="BesiegeCityCommand"/> that takes its last city, the same production path
/// <c>BesiegeCityCommandTests.DoD02_ACaptureThroughTheCommandEliminatesOneNationAndLeavesNothingDangling</c>
/// exercises — is rejected as a counterparty by every propose/accept handler afterward, while a relation
/// it already held is left untouched, kept as history rather than broken.
/// </summary>
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
    /// <see cref="ThirdPartyId"/> — and a besieging army strong enough to take it outright.
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
        state = state with { Relations = state.Relations.WithRelation(DoomedId, ThirdPartyId, codes.Trade) };

        return AtWar(state, ConquerorId, DoomedId);
    }

    /// <summary>
    /// The shared setup: dispatches the siege that actually eliminates <see cref="DoomedId"/>, and hands
    /// back the resulting state plus the codes both DoD 3 and DoD 4 assert against.
    /// </summary>
    private static (GameState State, RelationStateCodes Codes) EliminateDoomedNation()
    {
        var state = Fixture();
        var result = Dispatcher().Dispatch(state, new BesiegeCityCommand(ConquerorId, BesiegerId, LastCityId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.True(result.State.NationById(DoomedId)!.Eliminated);

        return (result.State, ToyRuleset.Diplomacy.StateCodes);
    }

    /// <summary>
    /// DoD 4: the trade relation <see cref="DoomedId"/> held with <see cref="ThirdPartyId"/> survives its
    /// owner's elimination unchanged — kept as history, not broken (the user's 2026-09-23 decision).
    /// </summary>
    [Fact]
    public void DoD04_ElimationKeepsAnExistingRelation_UnchangedAsHistory()
    {
        var (state, codes) = EliminateDoomedNation();

        Assert.Equal(codes.Trade, state.Relations.Get(DoomedId, ThirdPartyId));
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
        Assert.Equal(Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>
    /// DoD 3: the same, for <see cref="ProposeAllianceCommand"/>.
    /// </summary>
    [Fact]
    public void DoD03_ProposeAlliance_AgainstTheJustEliminatedNation_IsRejectedAndChangesNothing()
    {
        var (state, _) = EliminateDoomedNation();

        var result = Dispatcher().Dispatch(state, new ProposeAllianceCommand(ConquerorId, DoomedId));

        Assert.True(result.IsRejected);
        Assert.Equal(Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>
    /// DoD 3: a pending offer that names the just-eliminated nation as proposer is rejected the same way —
    /// the gap T17's <c>PendingOfferSystem</c> already closes on the roll (<c>aliveOk</c>) is the accept
    /// path, reached here directly by handing the state a pending offer the roll would never have produced.
    /// </summary>
    [Fact]
    public void DoD03_AcceptPendingOffer_NamingTheJustEliminatedNationAsProposer_IsRejectedAndChangesNothing()
    {
        var (eliminated, codes) = EliminateDoomedNation();
        var state = eliminated with { PendingOffer = new PendingDiplomaticOffer(DoomedId, codes.Trade) };

        var result = Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(ConquerorId));

        Assert.True(result.IsRejected);
        Assert.Equal(Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }
}
