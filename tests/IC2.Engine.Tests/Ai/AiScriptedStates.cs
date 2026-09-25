using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Cities.Capture;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// The hand-built states the AI's own behaviour tests run against, plus the seam a test needs to drive
/// one AI turn: a dispatcher over the real engine assembly and a recording sink.
/// </summary>
/// <remarks>
/// Fixtures are reused rather than re-declared: <see cref="CaptureTestbed"/>'s army, city and nation
/// builders and <see cref="BattleCommandTestbed"/>'s "<c>StateWith</c> plus a relation matrix that
/// actually covers these nations" are exactly what these tests need, and a second copy of either would
/// drift. Only the two things neither testbed has are added here — both seats AI with a named
/// personality, and a state whose active seat is the one about to decide.
/// </remarks>
public static class AiScriptedStates
{
    /// <summary>The attacking nation in every scripted state below.</summary>
    public const string Attacker = "north";

    /// <summary>The defending nation.</summary>
    public const string Defender = "south";

    /// <summary>The shipped toy ruleset.</summary>
    public static Ruleset Ruleset => BattleCommandTestbed.ToyRuleset;

    /// <summary>The shipped toy world.</summary>
    public static World World => BattleCommandTestbed.ToyWorld;

    /// <summary>A nation record with a seat this AI will actually play, and a named personality.</summary>
    public static NationState AiNation(
        string id,
        AiPersonality personality,
        int treasury = 0,
        string? capitalCityId = null) =>
        CaptureFixtures.Nation(id, treasury: treasury, capitalCityId: capitalCityId)
            with { Personality = personality };

    /// <summary>
    /// The Done-when 3 state: two armies standing next to each other, at peace, with the attacker
    /// roughly half again as strong as the defender — inside the band where an
    /// <c>aggression: 0.9</c> nation attacks and an <c>aggression: 0.1</c> nation does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every number here is chosen against the engine's own formula, not guessed.</strong>
    /// <c>ArmyPower.Compute</c> is
    /// <c>(Σ (combatPowerWeight × troops / powerTroopDivisor) / powerDivisor) × morale</c>, and the toy
    /// ruleset gives light infantry a <c>combatPowerWeight</c> of 20 with divisors of 100 and 80. At
    /// morale 60 that makes the attacker's 15,000 troops worth 2,220 and the defender's 10,000 worth
    /// 1,500 — a ratio of 1,480 permille.
    /// </para>
    /// <para>
    /// <strong>Why that number and not another.</strong> The gate is
    /// <c>2200 − 1300 × aggression</c> (<see cref="AiView.RequiredAttackRatioPermille"/>), so it asks
    /// 1,030 permille of an <c>aggression: 0.9</c> nation and 2,070 of an <c>aggression: 0.1</c> one.
    /// 1,480 sits between them with room on both sides: the test would still separate the two
    /// personalities if either army's morale moved by ten points. A fixture parked one permille from a
    /// threshold proves the threshold exists; this one proves the personality decides.
    /// </para>
    /// <para>
    /// The two cities exist only so that neither nation is eliminated and neither is left with a march
    /// or a siege that could outscore the attack. They sit four tiles from the armies and from each
    /// other, both fully fortified, so the only thing worth doing in this state is the attack.
    /// </para>
    /// </remarks>
    public static GameState TwoArmiesInContact(
        AiPersonality attackerPersonality,
        int attackerTroops = 15000,
        int defenderTroops = 10000,
        int morale = 60)
    {
        var nations = new[]
        {
            AiNation(Attacker, attackerPersonality, capitalCityId: "attacker-city"),
            AiNation(Defender, DefaultPersonality, capitalCityId: "defender-city"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "attacker-city", "Attacker City", 0, 0, Attacker, Attacker,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "defender-city", "Defender City", 7, 5, Defender, Defender,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "attacker-army", Attacker, 3, 2, morale,
                    CaptureFixtures.Unit("light_infantry", attackerTroops))
                with { Moves = 5 },
            CaptureFixtures.Army(
                    "defender-army", Defender, 4, 2, morale,
                    CaptureFixtures.Unit("light_infantry", defenderTroops))
                with { Moves = 5 },
        };

        return WithActiveSeat(BattleCommandTestbed.StateWith(nations, cities, armies), Attacker);
    }

    /// <summary>A neutral personality for the seat that is not under test.</summary>
    public static AiPersonality DefaultPersonality { get; } =
        new(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);

    /// <summary>Returns <paramref name="state"/> with <paramref name="nationId"/>'s seat active.</summary>
    /// <exception cref="ArgumentException"><paramref name="nationId"/> is not in the turn order.</exception>
    public static GameState WithActiveSeat(GameState state, string nationId)
    {
        for (var i = 0; i < state.TurnOrder.Count; i++)
        {
            if (string.Equals(state.TurnOrder[i], nationId, StringComparison.Ordinal))
            {
                return state with { ActiveSeatIndex = i };
            }
        }

        throw new ArgumentException($"'{nationId}' is not in the turn order.", nameof(nationId));
    }

    /// <summary>What one driven AI turn produced: its outcome and everything it published.</summary>
    /// <param name="Outcome">The turn's own result.</param>
    /// <param name="Events">Every event the turn's commands published, in order.</param>
    public sealed record DrivenTurn(AiTurnOutcome Outcome, IReadOnlyList<DomainEvent> Events)
    {
        /// <summary>The <see cref="ICommand.Kind"/> of every command the turn issued, in order.</summary>
        public IReadOnlyList<string> IssuedKinds { get; } = KindsFrom(Outcome);

        private static IReadOnlyList<string> KindsFrom(AiTurnOutcome outcome)
        {
            var kinds = new List<string>();
            foreach (var line in outcome.Log)
            {
                const string marker = "  issued ";
                if (line.StartsWith(marker, StringComparison.Ordinal))
                {
                    kinds.Add(line[marker.Length..]);
                }
            }

            return kinds;
        }
    }

    /// <summary>
    /// Runs one AI turn against <paramref name="state"/> through the real
    /// <see cref="CommandDispatcher"/> — the same seam <see cref="AiTurnSystem"/> hands
    /// <see cref="AiTurn.Run"/> inside the pipeline, so a scripted test exercises the production path
    /// rather than a stand-in. Against the shared two-nation toy <see cref="World"/>.
    /// </summary>
    public static DrivenTurn DriveOneTurn(GameState state, ulong seed = 1) => DriveOneTurn(state, World, seed);

    /// <summary>
    /// Rework round 1, N11: the same drive as <see cref="DriveOneTurn(GameState, ulong)"/>, against a
    /// caller-supplied <paramref name="world"/> instead of the shared two-nation toy one -- needed for
    /// any full-turn drive whose candidate (an alliance, say) depends on
    /// <see cref="Diplomacy.NeighbourGeography"/>, which the toy world's own two nations cannot exercise.
    /// </summary>
    public static DrivenTurn DriveOneTurn(GameState state, World world, ulong seed = 1)
    {
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), Ruleset, world, sink);
        var rng = SplitMix64Rng.ForStream(seed, "ai.turn");
        var outcome = AiTurn.Run(state, Ruleset, world, dispatcher, rng, sink);
        return new DrivenTurn(outcome, sink.Events.ToArray());
    }
}
