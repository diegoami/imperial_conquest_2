using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using DiplomacyFixtures = IC2.Engine.Tests.Diplomacy.DiplomacyTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>AiDiplomacyPhase</c>'s own wiring of <c>FUN_0044FB7C</c>'s alliance and trade halves
/// (<c>decompiled-ai-offers-to-human-seats.md</c> §1a <strong>[confirmed]</strong>) into candidates —
/// the deterministic gates themselves are <c>AiOwnDiplomacyRuleTests</c>' job
/// (<c>tests/IC2.Engine.Tests/Diplomacy/AiOwnDiplomacyRuleTests.cs</c>), this file's is that
/// <see cref="AiDiplomacyPhase.Propose"/> reaches them correctly and never reaches a human.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T82 (#359, bug #357): rewritten from the ground up.</strong> This file used to guard a
/// different, narrower bug: the pre-T82 engine proposed <see cref="ProposeAllianceCommand"/> toward
/// <em>any</em> nation at peace with a shared enemy, human seats included, and the handler's "always
/// accepted from a human seat" branch (correct for genuine hotseat play) then wrote the alliance
/// immediately — so an AI that had just declared war on a human could ally with it in the very same
/// turn, undoing its own declaration. That specific path no longer exists: this phase's alliance and
/// trade methods now go through <see cref="AiFormAllianceCommand"/>/<see cref="AiFormTradeCommand"/>,
/// which require a computer-controlled partner by construction
/// (<see cref="AiFormAllianceRejections.PartnerNotAi"/>), and
/// <see cref="AiOwnDiplomacyRule.FindAlliancePartner"/> never even considers a human nation as the
/// partner <c>m</c>. The old bug is therefore structurally unreachable, not merely un-scored — pinned
/// below regardless.
/// </para>
/// </remarks>
public sealed class AiDiplomacyPhaseTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static DiplomacyRules Rules => AiScriptedStates.Ruleset.Diplomacy;

    // ---- Trade: still gated on relation and target control, through the new AI-to-AI path ----

    [Fact]
    public void No_trade_is_proposed_to_a_nation_this_one_is_at_war_with()
    {
        Assert.DoesNotContain("ai-form-trade", Diplomacy(Rules.StateCodes.War, SeatControl.Ai));
    }

    [Fact]
    public void No_trade_is_ever_proposed_to_a_human_even_at_peace()
    {
        Assert.DoesNotContain("ai-form-trade", Diplomacy(Rules.StateCodes.Peace, SeatControl.Human));
    }

    [Fact]
    public void Trade_is_proposed_to_an_ai_nation_at_peace()
    {
        Assert.Contains("ai-form-trade", Diplomacy(Rules.StateCodes.Peace, SeatControl.Ai));
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
    /// say, and the relation between them set to exactly the value under test. Uses the shared two-nation
    /// toy world: fine for trade, which never consults <see cref="NeighbourGeography"/>.
    /// </summary>
    private static GameState DiplomacyState(int relation, SeatControl targetControl)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting,
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

    // ---- Alliance: needs a real, multi-nation, adjacency-bearing world (NeighbourGeography's own
    // requirement -- see AiOwnDiplomacyRuleTests' remarks), so these build one directly rather than
    // reusing the shared two-nation toy world above. ----

    private const int Width = 30;
    private const int Height = 14;

    [Fact]
    public void No_alliance_candidate_ever_names_a_human_partner_even_when_every_other_gate_is_met()
    {
        var world = AllianceWorld();
        var state = AllianceState(partnerControl: SeatControl.Human);

        var candidates = Propose(state, world);

        Assert.DoesNotContain(candidates, c => c.Kind == "ai-form-alliance");
    }

    [Fact]
    public void No_alliance_candidate_while_busy_even_though_trade_still_offers()
    {
        var world = AllianceWorld();
        var state = AllianceState(partnerControl: SeatControl.Ai);
        // Busy: at war with a bystander unrelated to the alliance search.
        state = state with { Relations = state.Relations.WithRelation(Acting, "bystander", Rules.StateCodes.War) };

        var candidates = Propose(state, world);

        Assert.DoesNotContain(candidates, c => c.Kind == "ai-form-alliance");
        // The busy gate is alliance/war-only (report §1a) -- trade is unaffected.
        Assert.Contains(candidates, c => c.Kind == "ai-form-trade");
    }

    /// <summary>
    /// Every deterministic gate is met (an eligible partner exists, not busy); across a small range of
    /// seeds, at least one produces the <c>Random(20) == 0</c> hit and the resulting candidate names the
    /// right partner. Fails if the alliance wiring is deleted (the partner search, the roll, or the
    /// command construction), the same "search for a reachable seed" pattern
    /// <c>AiClassicalMediterraneanStallMeasurementTests</c> already uses.
    /// </summary>
    [Fact]
    public void An_alliance_candidate_appears_and_names_the_right_partner_when_the_roll_hits()
    {
        var world = AllianceWorld();
        var denominator = AiScriptedStates.Ruleset.Diplomacy.AiOwnDiplomacy.AllianceRollDenominator;

        for (var seed = 0UL; seed < 500; seed++)
        {
            var state = AllianceState(partnerControl: SeatControl.Ai) with { RandomSeed = seed };
            var candidates = Propose(state, world);
            var found = candidates.FirstOrDefault(c => c.Kind == "ai-form-alliance");
            if (found is null)
            {
                continue;
            }

            var command = Assert.IsType<AiFormAllianceCommand>(Assert.Single(found.Commands));
            Assert.Equal(Acting, command.IssuingNationId);
            Assert.Equal("m", command.PartnerNationId);
            return;
        }

        Assert.Fail($"No seed under 500 produced an alliance candidate (Random({denominator}) roll).");
    }

    private static List<AiCandidate> Propose(GameState state, World world)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, world, Acting);
        var candidates = new List<AiCandidate>();

        AiDiplomacyPhase.Propose(
            view, AiPersonalityProfile.For(state.NationById(Acting)!), candidates);

        return candidates;
    }

    /// <summary>Three nations in adjacent bands (acting, j, m) plus an unrelated fourth (bystander).</summary>
    private static World AllianceWorld()
    {
        var ids = new[] { Acting, "j", "m", "bystander" };
        var bandWidth = Width / ids.Length;
        var nationDefs = ids
            .Select(id => new NationDefinition(id, id, "#000000", id, id, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();
        var cities = ids
            .Select((id, band) => new CityDefinition(
                id, id, (band * bandWidth) + (bandWidth / 2), Height / 2, id, id,
                80, 0, 50, 10, 10, 0, ValueList<UnitSlot>.Empty))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "ai-diplomacy-phase-alliance-world",
            "AiDiplomacyPhase alliance test world",
            Width,
            Height,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, Width * Height))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nationDefs),
            ValueList<CityDefinition>.Of(cities),
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList<string>.Of(ids));
    }

    /// <summary>
    /// <c>acting</c> is not busy; <c>j</c> is its neighbour, at peace, unprotected; <c>m</c> is already
    /// at war with <c>j</c>, has one war (under the cap), and <c>cities[j]</c> (1) is under
    /// <c>cities[acting]</c> (5) + <c>cities[m]</c> (1) -- every deterministic gate
    /// <see cref="AiOwnDiplomacyRule.FindAlliancePartner"/> checks is satisfied, so the only thing left
    /// to vary is the roll (and, for one test, <paramref name="partnerControl"/>).
    /// </summary>
    private static GameState AllianceState(SeatControl partnerControl)
    {
        var nations = new[]
        {
            DiplomacyFixtures.Nation(Acting, Acting),
            DiplomacyFixtures.Nation("j", "j"),
            DiplomacyFixtures.Nation("m", "m", control: partnerControl),
            DiplomacyFixtures.Nation("bystander", "bystander"),
        };

        var cities = Enumerable.Range(0, 5)
            .Select(i => new CityState(
                $"acting-city-{i}", $"Acting City {i}", X: 0, Y: 0, Owner: Acting, Allegiance: Acting,
                Loyalty: 80, SupplyTons: 0, FortificationCode: 50, PopulationThousands: 10,
                MaxPopulationThousands: 10, Tribute: 0, UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty))
            .Append(new CityState(
                "j-city", "J City", X: 0, Y: 0, Owner: "j", Allegiance: "j", Loyalty: 80, SupplyTons: 0,
                FortificationCode: 50, PopulationThousands: 10, MaxPopulationThousands: 10, Tribute: 0,
                UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty))
            .Append(new CityState(
                "m-city", "M City", X: 0, Y: 0, Owner: "m", Allegiance: "m", Loyalty: 80, SupplyTons: 0,
                FortificationCode: 50, PopulationThousands: 10, MaxPopulationThousands: 10, Tribute: 0,
                UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty))
            .ToArray();

        var ids = ValueList.From(nations.Select(n => n.Id));
        var ruleset = AiScriptedStates.Ruleset;
        var state = new GameState(
            SchemaVersion: 1,
            WorldId: "ai-diplomacy-phase-alliance-world",
            RulesetId: ruleset.Id,
            ScenarioId: "ai-diplomacy-phase-alliance-scenario",
            Calendar: new CalendarState(Week: ruleset.Calendar.StartWeek, SeasonIndex: 0, YearBc: 270, TurnIndex: 0),
            TurnOrder: ids,
            ActiveSeatIndex: 0,
            RandomSeed: 0UL,
            Nations: ValueList.From(nations),
            Cities: ValueList.From(cities),
            Armies: ValueList<ArmyState>.Empty,
            Fleets: ValueList<FleetState>.Empty,
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(ids, ruleset.Diplomacy.StateCodes.Peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: null);

        return state with { Relations = state.Relations.WithRelation("m", "j", ruleset.Diplomacy.StateCodes.War) };
    }
}
