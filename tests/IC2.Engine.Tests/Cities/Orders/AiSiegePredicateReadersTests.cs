using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Ai;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using RecruitmentTestFixtures = IC2.Engine.Tests.Recruitment.MobilizationFixture;

namespace IC2.Engine.Tests.Cities.Orders;

/// <summary>
/// T67 (#304) Done-when 3, the AI's two readers in <c>AiEconomyPhase</c>: the fortify gate that mirrors
/// <c>OrderCityCommandHandler</c>'s refusal, and the mobilize restraint. Both ask the hostile-army-adjacent
/// predicate (<c>FUN_004497cc</c>), and both are reached from a state built by play: the other nation's army
/// is walked beside the acting nation's city with a real <see cref="MoveArmyCommand"/>, never by a flag.
/// Each test pairs the besieged state with an identical one whose army has not moved.
/// </summary>
public sealed class AiSiegePredicateReadersTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;
    private const string HomeId = "home";

    [Fact]
    public void FortifyGate_AHostileArmyMovedBesideTheCity_StopsTheAiProposingAnOrderTheHandlerWouldRefuse()
    {
        var far = BuildState(slotState: null);
        Assert.Contains(Propose(far), c => c.Kind == "fortify");

        var besieged = MoveOtherArmyBesideHome(far);

        Assert.DoesNotContain(Propose(besieged), c => c.Kind == "fortify");
    }

    [Fact]
    public void MobilizeRestraint_AHostileArmyMovedBesideTheCity_SkipsMobilizingAtIt()
    {
        var recruitment = AiScriptedStates.Ruleset.Recruitment;
        var far = BuildState(recruitment.MobilizationMinStateCodeAiSeat);
        Assert.Contains(Propose(far), c => c.Kind == "mobilize");

        var besieged = MoveOtherArmyBesideHome(far);

        Assert.DoesNotContain(Propose(besieged), c => c.Kind == "mobilize");
    }

    private static List<AiCandidate> Propose(GameState state)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, RecruitmentTestFixtures.OpenWorld(30, 30), Acting);
        var candidates = new List<AiCandidate>();
        AiEconomyPhase.Propose(view, AiPersonalityProfile.For(state.NationById(Acting)!, view.Ruleset), candidates);
        return candidates;
    }

    /// <summary>At war from the start; the other nation's army stands in the far corner, two cells from nothing.</summary>
    private static GameState BuildState(int? slotState)
    {
        var acting = AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, treasury: 5000, capitalCityId: HomeId);
        if (slotState is { } code)
        {
            acting = acting with
            {
                RecruitmentSlots = ValueList.Of(new RecruitmentSlot(HomeId, "light_infantry", 1000, code)),
            };
        }

        var nations = new[]
        {
            acting,
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, treasury: 5000),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                HomeId, "Home", 2, 2, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };
        var armies = new[]
        {
            CaptureFixtures.Army("other-army", Other, 6, 4, 60, CaptureFixtures.Unit("light_infantry", 10000)) with { Moves = 20 },
        };

        var state = BattleCommandTestbed.StateWith(nations, cities, armies);
        return AiScriptedStates.WithActiveSeat(BattleCommandTestbed.AtWar(state, Acting, Other), Acting);
    }

    private static GameState MoveOtherArmyBesideHome(GameState state)
    {
        var originalSeat = state.ActiveSeatIndex;
        var otherSeat = AiScriptedStates.WithActiveSeat(state, Other).ActiveSeatIndex;
        var home = state.CityById(HomeId)!;
        var result = BattleCommandTestbed.Dispatcher().Dispatch(
            state with { ActiveSeatIndex = otherSeat },
            new MoveArmyCommand(Other, "other-army", home.X + 1, home.Y + 1));
        Assert.True(result.IsAccepted, result.Rejection?.ToString());
        var arrived = result.State.ArmyById("other-army")!;
        Assert.Equal((home.X + 1, home.Y + 1), (arrived.X, arrived.Y));
        return result.State with { ActiveSeatIndex = originalSeat };
    }
}
