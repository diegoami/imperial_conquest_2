using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Abstractions;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Issue <see href="https://github.com/diegoami/imperial_conquest_2/issues/268">#268</see>, measured and
/// not fixed (user decision, 2026-09-23: "measure first"): the shipped all-AI <c>classical-mediterranean</c>
/// scenario reports a stall run of 2 on every seed T60 tried, which <c>docs/task-catalogue.md</c> T22
/// Done-when 1 would fail. T22's own soak never plays this scenario.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a measurement, not an assertion.</strong> Nothing here asserts on stall length or the
/// worst stall run, so a stall this class finds can never fail this test -- only the PR body and the
/// comment this reports on #268 say what was found. What <em>is</em> asserted (zero rejected commands,
/// zero projection mismatches) is unrelated to stalling and would fail only on a genuine correctness
/// defect, exactly as <see cref="AiSiegeDiagnosticsTests"/>'s sibling assertions already do.
/// </para>
/// <para>
/// <strong>Why the cause can be measured at all.</strong> <c>AiGameRunner.Run</c>'s loop counts a turn
/// toward a stall only when it issues <em>zero</em> commands and leaves the state substantively unchanged
/// (<see cref="AiSubstantiveState.AreEquivalent"/>). Reading <c>AiTurn.Run</c>: the only way a turn issues
/// zero commands is for its very first proposal pass to return no candidate at all
/// (<c>Select</c> returns <see langword="null"/>) -- any candidate that clears
/// <c>AiWeights.MinimumActionScore</c> gets dispatched, and dispatch increments the issued count whether
/// accepted or rejected. So every stalled turn's transcript necessarily carries the
/// <c>"no candidate scored at least {0}; turn ends"</c> line, and the state right before that turn is
/// enough to ask each phase directly what it would have proposed.
/// </para>
/// <para>
/// <strong>How a stall's state is reached.</strong> <see cref="ReplayTo"/> re-plays the same seed through
/// the same <see cref="TurnCoordinator"/>/<see cref="CommandDispatcher"/> pair <c>AiGameRunner.Run</c>
/// itself uses, for exactly as many turns as preceded the stall -- deterministic because the seed, the
/// world, the ruleset and the scenario are all fixed. <see cref="Diagnose"/> then calls
/// <see cref="AiMilitaryPhase.Propose"/>, <see cref="AiEconomyPhase.Propose"/> and
/// <see cref="AiDiplomacyPhase.Propose"/> directly against that state -- read-only, nothing is
/// dispatched -- and reports what each phase generated (if anything), the seat's treasury and
/// <see cref="AiEconomyPhase.TurnBudget"/>, and whether it owns any army or fleet with moves left at all.
/// </para>
/// </remarks>
public sealed class AiClassicalMediterraneanStallMeasurementTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Sized to stay inside the suite's time budget across ten seeds. 120 seat-turns is 7.5 rounds of
    /// this 16-nation scenario: the first stall is at seat-turn 27 (round 2), well inside the cap, but
    /// the cap covers only <em>one</em> seasonal treasury refill (the sixteen nations' turns land at
    /// t102-118 on the seeds sampled) -- review round 1, B2/N3. A longer run would show more of the same
    /// recurring pattern, not a different one: every stall found here repeats once per round for the
    /// same four nations, tied to that one season boundary.
    /// </summary>
    private const int TurnCap = 120;

    private static readonly Regex StallLine = new(
        @"^turn (?<turn>\d+) -- (?<seat>\S+): no command issued and no substantive change \(stall run (?<run>\d+)\)$",
        RegexOptions.Compiled);

    public AiClassicalMediterraneanStallMeasurementTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Reproduces with: <c>AiGameRunner.Run(shipped.World, shipped.Ruleset, shipped.Scenario, seed, 120)</c>
    /// for <c>seed</c> in 1..10, where <c>shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean")</c>.
    /// </summary>
    [Fact]
    public void Seeds_1_to_10_are_measured_for_stalls_on_the_shipped_all_ai_scenario()
    {
        var shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"classical-mediterranean, seeds 1-10, turn cap {TurnCap}");
        report.AppendLine(
            "reproduce with: AiGameRunner.Run(shipped.World, shipped.Ruleset, shipped.Scenario, seed, "
            + TurnCap.ToString(CultureInfo.InvariantCulture) + ")");

        var totalRejected = 0;
        var totalMismatches = 0;

        for (ulong seed = 1; seed <= 10; seed++)
        {
            var result = AiGameRunner.Run(shipped.World, shipped.Ruleset, shipped.Scenario, seed, TurnCap);
            totalRejected += result.CommandsRejected;
            totalMismatches += result.ProjectionMismatches;

            report.AppendLine(CultureInfo.InvariantCulture, $"-- seed {seed} --");
            report.AppendLine(CultureInfo.InvariantCulture, $"  {result.Summary()}");

            var stalls = FindStalls(result.Transcript);
            if (stalls.Count == 0)
            {
                report.AppendLine("  no stalled turns found");
                continue;
            }

            // One incremental pass per seed, not one full replay per stall: DiagnoseAll walks the same
            // seed once, turn by turn, and diagnoses each stalled turn from the state already in hand
            // right before it, rather than restarting from turn 1 for every stall found.
            foreach (var diagnosis in DiagnoseAll(shipped.World, shipped.Ruleset, shipped.Scenario, seed, stalls))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {diagnosis}");
            }
        }

        var text = report.ToString();
        _output.WriteLine(text);

        // Unrelated to stalling: a rejected command or a probe mismatch is a correctness defect this
        // measurement would be the wrong place to hide, exactly as AiSiegeDiagnosticsTests already
        // asserts for this same scenario's committed siege test.
        Assert.Equal(0, totalRejected);
        Assert.Equal(0, totalMismatches);
    }

    /// <summary>Parses every <c>AiGameRunner</c> stall line out of one seed's transcript.</summary>
    private static List<Stall> FindStalls(IReadOnlyList<string> transcript)
    {
        var stalls = new List<Stall>();
        foreach (var line in transcript)
        {
            var match = StallLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            stalls.Add(new Stall(
                int.Parse(match.Groups["turn"].Value, CultureInfo.InvariantCulture),
                match.Groups["seat"].Value,
                int.Parse(match.Groups["run"].Value, CultureInfo.InvariantCulture)));
        }

        return stalls;
    }

    /// <summary>
    /// Re-plays <paramref name="seed"/> once, turn by turn, through the same
    /// <see cref="TurnCoordinator"/>/<see cref="CommandDispatcher"/> pair <c>AiGameRunner.Run</c> itself
    /// uses -- deterministic, since the seed, the world, the ruleset and the scenario are all fixed, and
    /// nothing here dispatches a command of its own. Every turn number named in
    /// <paramref name="stalls"/> is diagnosed from the state already in hand right before that turn runs,
    /// rather than a fresh replay per stall: ten seeds times a dozen-odd stalls each made the naive
    /// per-stall replay too slow for the suite's time budget.
    /// </summary>
    private static List<string> DiagnoseAll(
        World world, Ruleset ruleset, Scenario scenario, ulong seed, IReadOnlyList<Stall> stalls)
    {
        var byTurn = stalls.ToDictionary(s => s.Turn);
        var lastTurn = stalls[^1].Turn;

        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, ruleset, world, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, ruleset, world, NullEventSink.Instance, dispatcher);
        var state = GameStateFactory.CreateInitial(world, ruleset, scenario) with { RandomSeed = seed };

        var diagnoses = new List<string>(stalls.Count);
        for (var turn = 1; turn <= lastTurn; turn++)
        {
            if (byTurn.TryGetValue(turn, out var stall))
            {
                diagnoses.Add(Diagnose(world, ruleset, seed, state, stall));
            }

            state = coordinator.RunTurn(state).State;
        }

        return diagnoses;
    }

    /// <summary>
    /// Asks each AI phase directly what it would have proposed for the stalled seat, against the exact
    /// state <see cref="DiagnoseAll"/> reached right before the stalled turn ran.
    /// </summary>
    private static string Diagnose(World world, Ruleset ruleset, ulong seed, GameState state, Stall stall)
    {
        if (!string.Equals(state.ActiveNationId, stall.Seat, StringComparison.Ordinal))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "turn {0}, {1}: replay landed on a different seat ({2}) -- diagnosis skipped",
                stall.Turn, stall.Seat, state.ActiveNationId);
        }

        var nation = state.NationById(stall.Seat)!;
        var personality = AiPersonalityProfile.For(nation);
        var view = new AiView(state, ruleset, world, stall.Seat);

        var military = new List<AiCandidate>();
        AiMilitaryPhase.Propose(
            view, personality, SplitMix64Rng.ForStream(seed, "t65.stall-diagnosis"), Array.Empty<string>(), military);
        var economy = new List<AiCandidate>();
        AiEconomyPhase.Propose(view, personality, economy);
        var diplomacy = new List<AiCandidate>();
        AiDiplomacyPhase.Propose(view, personality, diplomacy);

        var budget = AiEconomyPhase.TurnBudget(nation.Treasury, personality.ExpansionDrivePermille);
        var ownArmies = view.OwnArmies().Count;
        var ownFleets = view.OwnFleets().Count;
        var armiesWithMoves = view.OwnArmies().Count(a => !a.IsEmbarked && a.Moves > 0);
        var fleetsWithMoves = view.OwnFleets().Count(f => !f.IsUnderConstruction && f.Moves > 0);
        var ownCities = view.OwnCities().Count;
        var recruitmentSlots = nation.RecruitmentSlots.Count;
        var maxRecruitmentSlots = ruleset.Recruitment.MaxSlots;
        var cheapestUnitCost = CheapestUnitCost(ruleset);
        var cheapestFortifyPointCost = CheapestFortifyPointCost(view, ruleset);

        // Named, not guessed (review round 1, B2): every candidate generator here either found nothing to
        // propose (an empty list) or found candidates that all fell short of
        // AiWeights.MinimumActionScore (a non-empty list Select would still reject). The two are
        // reported differently rather than collapsed, since only the second is "legal but too weak" --
        // the class this scenario's own siege gate already proves correct (AiSiegeDiagnosticsTests).
        //
        // The "no army, no fleet" branch below is category "no affordable action", confirmed by tracing
        // seeds 1 and 2 turn by turn (not guessed): armenia, illyria, dacia and numidia are the only
        // stalling nations, and data/worlds/classical-mediterranean.json's startingArmies/startingFleets
        // list none for any of them -- they never had a unit to begin with, and their RecruitmentSlots
        // stay empty for the whole 120-turn run, so they never place an order either. At each seasonal
        // treasury refill they receive a large lump sum (seed 1 armenia: 1546 talents at t110) and spend
        // ~90% of it in that one turn, entirely on AiEconomyPhase.ProposeFortification orders (11 of 11
        // commands that turn) -- because at this personality's expansionDrive, a fortify candidate always
        // outscores a recruit candidate (FortifyBaseScore x (2 - expansionDrive share) clears
        // RecruitBaseScore x expansionDrive share every pass while any fortification point is still
        // affordable), so the action loop dispatches fortify after fortify until the remaining budget
        // clears neither the cheapest unit nor the cheapest fortification point. The treasury then sits
        // at that leftover level for the rest of the season, which is exactly what this diagnosis measures
        // below: a small positive budget against costs both above it.
        var cause = military.Count == 0 && economy.Count == 0 && diplomacy.Count == 0
            ? ownArmies == 0 && ownFleets == 0
                ? "no affordable action: the seat has no army and no fleet at all (none in this scenario's "
                  + "own starting data, and its recruitment table has stayed empty for the whole run), so "
                  + "military has nothing to move/attack/siege with, and its post-fortify-spree budget of "
                  + $"{budget} clears neither the cheapest recruitable unit ({cheapestUnitCost} talents) "
                  + $"nor its own cheapest fortification point ({FormatCost(cheapestFortifyPointCost)} "
                  + $"talents) -- recruitment table {recruitmentSlots}/{maxRecruitmentSlots} slots, "
                  + $"{ownCities} own cities"
                : armiesWithMoves == 0 && fleetsWithMoves == 0
                    ? "no candidate from any phase; every owned army/fleet has zero moves left this turn, "
                      + $"and economy proposed nothing of its own despite budget {budget} -- recruitment "
                      + $"table {recruitmentSlots}/{maxRecruitmentSlots} slots, {ownCities} own cities"
                    : "no candidate from any phase despite moves being available -- something else"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} military / {1} economy / {2} diplomacy candidate(s) were generated but none cleared "
                + "AiWeights.MinimumActionScore",
                military.Count, economy.Count, diplomacy.Count);

        return string.Format(
            CultureInfo.InvariantCulture,
            "turn {0}, {1} (stall run {2}): treasury {3}, budget {4}, armies {5} ({6} with moves), "
            + "fleets {7} ({8} with moves) -- {9}",
            stall.Turn, stall.Seat, stall.Run, nation.Treasury, budget, ownArmies, armiesWithMoves, ownFleets,
            fleetsWithMoves, cause);
    }

    /// <summary>
    /// The lowest initial cost (<see cref="StandingRecruitmentCost.InitialCost"/>) of a standard
    /// battalion of any unit type this ruleset defines -- the same figure
    /// <c>AiEconomyPhase.BestAffordableUnitType</c> compares a turn's budget against, computed
    /// independently here for the diagnosis rather than read off that private method.
    /// </summary>
    private static int CheapestUnitCost(Ruleset ruleset)
    {
        var cheapest = int.MaxValue;
        foreach (var unitType in ruleset.UnitTypes)
        {
            var cost = StandingRecruitmentCost.InitialCost(unitType.StandardBattalionSize, unitType.Id, ruleset);
            cheapest = Math.Min(cheapest, cost);
        }

        return cheapest;
    }

    /// <summary>
    /// The lowest one-point fortification cost (<c>CostPerPointPerPopulationThousand x population</c>,
    /// <c>AiEconomyPhase.ProposeFortification</c>'s own expression) among the seat's own cities, or
    /// <see langword="null"/> if it owns none or the ruleset declares no fortification order.
    /// </summary>
    private static int? CheapestFortifyPointCost(AiView view, Ruleset ruleset)
    {
        if (BattleCommandRuleset.FortificationOrderIdIn(ruleset) is not { } orderId)
        {
            return null;
        }

        CityOrderRule? rule = null;
        foreach (var candidate in ruleset.CityOrders.Orders)
        {
            if (string.Equals(candidate.Id, orderId, StringComparison.Ordinal))
            {
                rule = candidate;
                break;
            }
        }

        if (rule is null)
        {
            return null;
        }

        int? cheapest = null;
        foreach (var city in view.OwnCities())
        {
            var costPerPoint = rule.CostPerPointPerPopulationThousand * city.PopulationThousands;
            if (costPerPoint > 0 && (cheapest is null || costPerPoint < cheapest))
            {
                cheapest = costPerPoint;
            }
        }

        return cheapest;
    }

    private static string FormatCost(int? cost) =>
        cost?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private readonly record struct Stall(int Turn, string Seat, int Run);
}
