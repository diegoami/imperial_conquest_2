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

    /// <summary>
    /// Rework round 1, N4: with more than one eligible partner, the original always picks the first one
    /// in the nation table's own index order (report §1a: loop 1, then loop 3, both over the fixed slot
    /// order) -- never a random one. Three eligible partners are given deliberately non-alphabetical ids
    /// ("z-first", "m-second", "a-third") in exactly that construction order, so a wrong implementation
    /// keyed to alphabetical order ("a-third" first) would fail this test just as clearly as one that
    /// still ties every score and lets the seed decide. Checked across several different <see cref="IRng"/>
    /// seeds: before this fix, every partner scored identically at the phase's own flat
    /// <c>OwnTradeScore</c>, so <c>AiTurn.Select</c>'s own exact-tie break (a draw from the seed's stream)
    /// made the choice seed-dependent; now the highest-scoring "ai-form-trade" candidate always names
    /// "z-first", regardless of seed, because the scores are no longer tied.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void Trade_picks_the_first_eligible_partner_in_nation_order_regardless_of_seed(ulong seed)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation("z-first", AiScriptedStates.DefaultPersonality, capitalCityId: "z-city"),
            AiScriptedStates.AiNation("m-second", AiScriptedStates.DefaultPersonality, capitalCityId: "m-city"),
            AiScriptedStates.AiNation("a-third", AiScriptedStates.DefaultPersonality, capitalCityId: "a-city"),
        };
        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 1, 1, Acting, Acting, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "z-city", "ZCity", 6, 4, "z-first", "z-first", loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "m-city", "MCity", 8, 4, "m-second", "m-second", loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "a-city", "ACity", 10, 4, "a-third", "a-third", loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities);
        var candidates = Propose(state, AiScriptedStates.World, SplitMix64Rng.ForStream(seed, "ai.turn"));

        var tradeCandidates = candidates.Where(c => c.Kind == "ai-form-trade").ToList();
        Assert.Equal(3, tradeCandidates.Count);

        // The real, seed-sensitive failure mode is a tie: AiTurn.Select (outside this task's Owns list to
        // change) breaks an exact tie for the overall best score with a draw from the seed's own stream,
        // so a stable LINQ OrderBy over the candidate list alone would keep returning the same one
        // regardless of whether the underlying scores are actually tied -- masking the bug this test
        // exists to catch. Asserting the max score is unique is what actually distinguishes the fix from
        // the mutation: flat scoring at OwnTradeScore leaves all three tied for the best, live to the
        // random draw; the fix's per-rank scores leave exactly one holding it.
        var maxScore = tradeCandidates.Max(c => c.Score);
        var atMax = tradeCandidates.Where(c => c.Score == maxScore).ToList();
        Assert.True(
            atMax.Count == 1,
            $"expected exactly one trade candidate to hold the max score {maxScore}, got {atMax.Count} tied");

        var command = Assert.IsType<AiFormTradeCommand>(Assert.Single(atMax[0].Commands));
        Assert.Equal("z-first", command.PartnerNationId);
    }

    /// <summary>The diplomacy phase's candidate kinds against one relation value and one target control.</summary>
    private static List<string> Diplomacy(int relation, SeatControl targetControl)
    {
        var state = DiplomacyState(relation, targetControl);
        var view = new AiView(state, AiScriptedStates.Ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();

        AiDiplomacyPhase.Propose(
            view, AiPersonalityProfile.For(state.NationById(Acting)!), SplitMix64Rng.ForStream(1, "ai.turn"), candidates);

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
        var state = AllianceState(partnerControl: SeatControl.Ai);

        for (var seed = 0UL; seed < 500; seed++)
        {
            // T82 Owns amendment (PR #378): the roll now comes from the seat-turn's own IRng, the same
            // stream AiMilitaryPhase's war roll uses -- not from state.RandomSeed -- so this searches
            // over the rng's own seed instead.
            var candidates = Propose(state, world, SplitMix64Rng.ForStream(seed, "ai.turn"));
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

    /// <summary>
    /// Rework round 1, B4: no test caught the <c>Random(20)</c> gate being deleted. Every deterministic
    /// gate in <see cref="AllianceState"/> is met on every seed (an eligible partner exists, not busy),
    /// so with the roll wired correctly some seeds hit and some miss; replacing
    /// <c>if (!roll.NextChance(1, denominator))</c> with a never-taken branch (the mutation the review
    /// applied by hand) makes every seed hit, and this test's own "at least one miss" assertion catches
    /// that -- the same "hits and misses both occur" shape
    /// <c>AiWarDeclarationRollTests.TheRollDoesNotAlwaysHit_AcrossManySeedsWithAnEligibleTarget</c>
    /// already uses for the war roll.
    /// </summary>
    [Fact]
    public void TheAllianceRollDoesNotAlwaysHit_AcrossManySeedsWithAnEligiblePartner()
    {
        var world = AllianceWorld();
        var state = AllianceState(partnerControl: SeatControl.Ai);

        var hits = 0;
        var misses = 0;
        for (var seed = 0UL; seed < 200; seed++)
        {
            var candidates = Propose(state, world, SplitMix64Rng.ForStream(seed, "ai.turn"));
            if (candidates.Any(c => c.Kind == "ai-form-alliance"))
            {
                hits++;
            }
            else
            {
                misses++;
            }
        }

        Assert.True(hits > 0, "expected at least one of 200 seeds to hit the alliance Random(20) roll");
        Assert.True(
            misses > 0,
            "expected at least one of 200 seeds to miss the alliance Random(20) roll -- the roll must gate the alliance, not wave it through");
    }

    /// <summary>
    /// Rework round 2, N-d: the sibling test above pins that the roll gates the alliance at all, but a
    /// hard-coded denominator (10, the war roll's own) would still show both hits and misses across 200
    /// seeds -- it would just hit twice as often. This pins the denominator's own <em>value</em> by
    /// reading it from the ruleset, not <c>AiOwnDiplomacyRules.AllianceRollDenominator</c>'s shipped 20:
    /// overriding it to 2 (a coin flip) must make the roll hit within a small, fixed seed window far more
    /// reliably than either the shipped 20 or a hard-coded-to-the-war-roll's 10 ever would, proving the
    /// code path actually reads <see cref="AiOwnDiplomacyRules.AllianceRollDenominator"/> off
    /// <paramref name="view"/>'s own ruleset rather than a literal.
    /// </summary>
    [Fact]
    public void TheAllianceRollDenominatorComesFromTheRuleset_NotAHardCodedLiteral()
    {
        var world = AllianceWorld();
        var coinFlipRuleset = AiScriptedStates.Ruleset with
        {
            Diplomacy = AiScriptedStates.Ruleset.Diplomacy with
            {
                AiOwnDiplomacy = AiScriptedStates.Ruleset.Diplomacy.AiOwnDiplomacy with { AllianceRollDenominator = 2 },
            },
        };
        var state = AllianceState(partnerControl: SeatControl.Ai);

        var hits = 0;
        const int SeedCount = 20;
        for (var seed = 0UL; seed < SeedCount; seed++)
        {
            var view = new AiView(state, coinFlipRuleset, world, Acting);
            var candidates = new List<AiCandidate>();
            AiDiplomacyPhase.Propose(
                view, AiPersonalityProfile.For(state.NationById(Acting)!), SplitMix64Rng.ForStream(seed, "ai.turn"), candidates);
            if (candidates.Any(c => c.Kind == "ai-form-alliance"))
            {
                hits++;
            }
        }

        // Binomial(20, 1/2) has a mean of 10; Binomial(20, 1/20) (the shipped denominator, if the
        // override were silently ignored) has a mean of 1, and Binomial(20, 1/10) (a hard-coded literal
        // copied from the war roll) has a mean of 2 -- 8 or more hits out of 20 is expected well over
        // 99% of the time at 1/2 odds, and well under 1% of the time at either 1/20 or 1/10 odds.
        Assert.True(
            hits >= 8,
            $"expected at least 8 of {SeedCount} seeds to hit a coin-flip (denominator 2) alliance roll, got {hits} -- the denominator may not be read from the ruleset");
    }

    /// <summary>
    /// T82 Owns amendment (PR #378), Done-when 3's new line: "Random(20) draws from the seat-turn's
    /// IRng, the same stream as Random(10), so a re-evaluated pass sees the same draw." Simulates the
    /// greedy loop re-running <c>Propose</c> more than once in the same turn against the very same
    /// <see cref="IRng"/> instance (never a fresh one): whether the roll hit or missed the first time,
    /// the second (and third) call must agree exactly. Fails if <c>ProposeOwnAlliance</c> is changed
    /// back to seed a second generator from <see cref="Model.GameState.RandomSeed"/> instead of calling
    /// <see cref="IRng.ForStream"/> on the passed-in <paramref name="rng"/> — a fresh
    /// <see cref="SplitMix64Rng"/> per call would not reliably agree with itself the way this test
    /// requires, since nothing ties it to a stable per-turn value across repeated calls.
    /// </summary>
    [Fact]
    public void ARepeatedProposalPass_SeesTheSameAllianceRollEachTime()
    {
        var world = AllianceWorld();
        var state = AllianceState(partnerControl: SeatControl.Ai);

        // Finds a seed where the roll hits first (the same search
        // An_alliance_candidate_appears_and_names_the_right_partner_when_the_roll_hits already proves is
        // reachable), so the assertions below test something real rather than "false equals false".
        IRng? hittingRng = null;
        for (var seed = 0UL; seed < 2000; seed++)
        {
            var candidateRng = SplitMix64Rng.ForStream(seed, "ai.turn");
            if (Propose(state, world, candidateRng).Any(c => c.Kind == "ai-form-alliance"))
            {
                hittingRng = candidateRng;
                break;
            }
        }

        Assert.NotNull(hittingRng);

        // Simulates an intervening command landing between proposal passes this same turn --
        // CommandDispatcher's own documented behaviour (it advances GameState.RandomSeed after every
        // accepted command) -- with the *same* rng instance, but a *different* RandomSeed. The roll must
        // still agree: it is keyed off rng (stable for the whole seat-turn), never off the state's own
        // moving RandomSeed. Proved by mutation: seeding a fresh SplitMix64Rng from view.State.RandomSeed
        // instead of calling rng.ForStream flips this from a hit to a miss once RandomSeed is varied.
        var second = Propose(state with { RandomSeed = state.RandomSeed + 1 }, world, hittingRng)
            .Any(c => c.Kind == "ai-form-alliance");
        var third = Propose(state with { RandomSeed = state.RandomSeed + 12345 }, world, hittingRng)
            .Any(c => c.Kind == "ai-form-alliance");

        Assert.True(second, "a re-evaluated pass with a drifted RandomSeed must still see the hit");
        Assert.True(third, "a re-evaluated pass with a drifted RandomSeed must still see the hit");
    }

    private static List<AiCandidate> Propose(GameState state, World world, IRng? rng = null)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, world, Acting);
        var candidates = new List<AiCandidate>();

        AiDiplomacyPhase.Propose(
            view, AiPersonalityProfile.For(state.NationById(Acting)!), rng ?? SplitMix64Rng.ForStream(1, "ai.turn"), candidates);

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

    /// <summary>
    /// Rework round 1, N11: neither <c>AiCommandLegalityTests</c>' battery nor the toy soak ever issues
    /// <c>ai-form-alliance</c> through a real, end-to-end AI turn -- every existing alliance test stops
    /// at <see cref="AiDiplomacyPhase.Propose"/>'s own candidate list, never reaching
    /// <see cref="AiTurn.Run"/>'s own <c>Select</c>-then-dispatch. This drives one real turn (through
    /// <see cref="AiScriptedStates.DriveOneTurn(GameState, World, ulong)"/>, the same seam
    /// <see cref="AiTurnSystem"/> uses) against <see cref="AllianceState"/>, searching seeds for the
    /// <c>Random(20)</c> hit exactly as <see cref="An_alliance_candidate_appears_and_names_the_right_partner_when_the_roll_hits"/>
    /// does, and confirms the command was not just proposed but actually accepted and issued, and that it
    /// really wrote the alliance relation.
    /// </summary>
    [Fact]
    public void AFullAiTurn_ReallyIssuesAiFormAlliance()
    {
        var world = AllianceWorld();
        var state = AllianceState(partnerControl: SeatControl.Ai);
        var ruleset = AiScriptedStates.Ruleset;

        for (var seed = 0UL; seed < 2000; seed++)
        {
            var driven = AiScriptedStates.DriveOneTurn(state, world, seed);
            if (!driven.IssuedKinds.Contains("diplomacy.ai-form-alliance"))
            {
                continue;
            }

            Assert.Equal(0, driven.Outcome.CommandsRejected);
            Assert.Equal(
                ruleset.Diplomacy.StateCodes.Alliance, driven.Outcome.State.Relations.Get(Acting, "m"));
            return;
        }

        Assert.Fail("No seed under 2000 drove a real AI turn that issued diplomacy.ai-form-alliance.");
    }

    /// <summary>
    /// Rework round 1, N11: the same gap as above, for <c>ai-swap-trade-partner</c>. <c>me</c> already
    /// trades with a poor partner and is at peace with a richer, still-AI-controlled candidate -- every
    /// deterministic gate <see cref="AiOwnDiplomacyRule.FindTradeSwap"/> checks is satisfied and, unlike
    /// the alliance and war rolls, the swap is not chance-gated at all (report §1a), so no seed search is
    /// needed: it fires on the very first action of the very first seed.
    /// </summary>
    [Fact]
    public void AFullAiTurn_ReallyIssuesAiSwapTradePartner()
    {
        var ruleset = AiScriptedStates.Ruleset;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        // Acting is already AT its own trade cap (poorer plus (cap - 1) filler partners), so
        // EligibleTradePartners offers nothing fresh -- richer is reachable only through a swap, which
        // (unlike a fresh trade) does not check the issuer's own cap at all, only the poorer partner's
        // tax base against the richer candidate's.
        var nations = new List<NationState>
        {
            DiplomacyFixtures.Nation(Acting, "Acting"),
            DiplomacyFixtures.Nation("poorer", "Poorer", taxBase: 100),
            DiplomacyFixtures.Nation("richer", "Richer", taxBase: 500),
        };
        var cities = new List<CityState>
        {
            CaptureFixtures.City(
                "acting-city", "ActingCity", 1, 1, Acting, Acting, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "poorer-city", "PoorerCity", 3, 1, "poorer", "poorer", loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "richer-city", "RicherCity", 5, 1, "richer", "richer", loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
        };
        for (var i = 1; i < cap; i++)
        {
            var fillerId = $"filler{i}";
            nations.Add(DiplomacyFixtures.Nation(fillerId, fillerId, taxBase: 50));
            cities.Add(CaptureFixtures.City(
                $"{fillerId}-city", fillerId, 7 + i, 1, fillerId, fillerId, loyalty: 80, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10));
        }

        var nationIds = ValueList.From(nations.Select(n => n.Id));
        var relations = DiplomaticRelations.Uniform(nationIds, ruleset.Diplomacy.StateCodes.Peace)
            .WithRelation(Acting, "poorer", ruleset.Diplomacy.StateCodes.Trade);
        for (var i = 1; i < cap; i++)
        {
            relations = relations.WithRelation(Acting, $"filler{i}", ruleset.Diplomacy.StateCodes.Trade);
        }

        var state = BattleCommandTestbed.StateWith(nations, cities) with { Relations = relations };

        // Confirms the fixture reaches the scenario the doc comment describes, not merely that some
        // command happens to be issued: Acting is genuinely at cap, so a fresh trade with richer is not
        // among the candidates at all, only the swap is.
        Assert.Empty(AiOwnDiplomacyRule.EligibleTradePartners(state, ruleset, Acting));
        Assert.Equal(("poorer", "richer"), AiOwnDiplomacyRule.FindTradeSwap(state, ruleset, Acting));

        var driven = AiScriptedStates.DriveOneTurn(state, AiScriptedStates.World, seed: 1);

        Assert.Contains("diplomacy.ai-swap-trade-partner", driven.IssuedKinds);
        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(
            ruleset.Diplomacy.CooldownAfterBrokenTrade, driven.Outcome.State.Relations.Get(Acting, "poorer"));
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, driven.Outcome.State.Relations.Get(Acting, "richer"));
    }
}
