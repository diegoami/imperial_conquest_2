using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>AiDiplomacyPhase</c>'s alliance gate, and in particular the half of it the command handler does not
/// enforce.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Review round 1, F2.</strong> This file exists because the alliance-while-at-war fix had no
/// direct regression test: it was guarded only by the CLI demo's golden transcript, and
/// <c>docs/build-process.md</c> §2.3 lets any task regenerate that transcript — as this very PR did. A
/// named bug guarded only by a fixture the next task may overwrite is the same coupling that destroyed
/// T10's supply demonstration, one layer up. So the rule is pinned here, where only the rule can break it.
/// </para>
/// <para>
/// <strong>The bug.</strong> <c>ProposeAllianceCommandHandler</c> refuses an alliance while either side
/// is at war <em>only when the target is AI-controlled</em>; a human target
/// <em>"always accepts"</em> — which is <c>[confirmed]</c> behaviour from <c>TPolitics_MakeAlliance</c>,
/// not a defect. So when the AI proposed an alliance to a human nation it had just declared war on,
/// nothing refused it: zero rejected commands, and a news log reading
/// <c>"SOUTHERN LEAGUE DECLARES WAR ON NORTHERN LEAGUE."</c> followed immediately by
/// <c>"Southern League forms an alliance with Northern League."</c> The AI undid its own declaration in
/// the same turn.
/// </para>
/// <para>
/// <strong>Why the soak could never have found it.</strong> Between two AI seats the handler's own
/// <c>SideAtWar</c> gate hides the mistake — the command is refused, the AI counts a rejection, and the
/// scorer looks innocent. It takes a <em>human</em> target to expose it, which is why every case below
/// names the target's <see cref="SeatControl"/> explicitly and why the war cases are tested against both.
/// </para>
/// </remarks>
public sealed class AiDiplomacyPhaseTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static DiplomacyRules Rules => AiScriptedStates.Ruleset.Diplomacy;

    /// <summary>
    /// The finding itself: at war, no alliance is proposed — <strong>including to a human target</strong>,
    /// the case the handler would have accepted.
    /// </summary>
    [Theory]
    [InlineData(SeatControl.Human)]
    [InlineData(SeatControl.Ai)]
    public void No_alliance_is_proposed_to_a_nation_this_one_is_at_war_with(SeatControl targetControl)
    {
        var kinds = Diplomacy(Rules.StateCodes.War, targetControl);

        Assert.DoesNotContain("propose-alliance", kinds);
    }

    /// <summary>
    /// The control. Without this the test above would pass against an AI that proposed no alliance ever,
    /// which would satisfy the letter of the fix and lose the behaviour.
    /// </summary>
    [Theory]
    [InlineData(SeatControl.Human)]
    [InlineData(SeatControl.Ai)]
    public void An_alliance_is_proposed_at_peace(SeatControl targetControl)
    {
        var kinds = Diplomacy(Rules.StateCodes.Peace, targetControl);

        Assert.Contains("propose-alliance", kinds);
    }

    /// <summary>Trade is the other relation an alliance may be proposed from.</summary>
    [Fact]
    public void An_alliance_is_proposed_from_a_trade_relation()
    {
        Assert.Contains("propose-alliance", Diplomacy(Rules.StateCodes.Trade, SeatControl.Human));
    }

    /// <summary>
    /// A negative relation is a cooldown the nation itself incurred. <c>FormAlliance</c> would overwrite
    /// it and a human target would accept, so this is the other half the handler does not enforce.
    /// </summary>
    [Fact]
    public void No_alliance_is_proposed_out_of_a_cooldown()
    {
        Assert.DoesNotContain(
            "propose-alliance", Diplomacy(Rules.CooldownAfterEndedWar, SeatControl.Human));
    }

    /// <summary>Already allied: the handler refuses this one outright, and the AI does not ask.</summary>
    [Fact]
    public void No_alliance_is_proposed_to_an_existing_ally()
    {
        Assert.DoesNotContain(
            "propose-alliance", Diplomacy(Rules.StateCodes.Alliance, SeatControl.Human));
    }

    /// <summary>
    /// Trade is gated separately and more narrowly — the handler accepts trade from exactly one relation
    /// value — so the war case is pinned for it too rather than assumed to follow.
    /// </summary>
    [Fact]
    public void No_trade_is_proposed_to_a_nation_this_one_is_at_war_with()
    {
        Assert.DoesNotContain("propose-trade", Diplomacy(Rules.StateCodes.War, SeatControl.Human));
    }

    /// <summary>
    /// End to end, through the real dispatcher: the whole turn that produced the nonsense news line now
    /// declares war and attacks without also allying. Asserted on the issued commands, so it holds even
    /// if the candidate names ever change.
    /// </summary>
    [Fact]
    public void A_turn_that_declares_war_does_not_also_propose_an_alliance()
    {
        var bold = new AiPersonality(Aggression: 0.9, ExpansionDrive: 0.5, LoyaltyToAlliances: 1.0);
        var state = AiScriptedStates.TwoArmiesInContact(bold);

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.Contains("diplomacy.declare-war", driven.IssuedKinds);
        Assert.DoesNotContain("diplomacy.propose-alliance", driven.IssuedKinds);
        Assert.Equal(0, driven.Outcome.CommandsRejected);
    }

    /// <summary>The diplomacy phase's candidate kinds against one relation value and one target control.</summary>
    private static List<string> Diplomacy(int relation, SeatControl targetControl)
    {
        var state = DiplomacyState(relation, targetControl);
        var view = new AiView(state, AiScriptedStates.Ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();

        AiDiplomacyPhase.Propose(
            view, AiPersonalityProfile.For(state.NationById(Acting)!), candidates);

        var kinds = new List<string>();
        foreach (var candidate in candidates)
        {
            kinds.Add(candidate.Kind);
        }

        return kinds;
    }

    /// <summary>
    /// Two nations with one city each and no armies, so nothing but the diplomacy phase has anything to
    /// say, and the relation between them set to exactly the value under test.
    /// </summary>
    private static GameState DiplomacyState(int relation, SeatControl targetControl)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting,
                // Full loyaltyToAlliances, so a missing candidate is never a scoring accident.
                new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 1.0),
                capitalCityId: "ours"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs")
                with { Control = targetControl },
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 1, 1, Acting, Acting, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 6, 4, Other, Other, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities);
        return AiScriptedStates.WithActiveSeat(
            state with { Relations = state.Relations.WithRelation(Acting, Other, relation) }, Acting);
    }
}
