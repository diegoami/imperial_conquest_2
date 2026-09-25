using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// Review round 1, N2: a classical-world <em>behavioural</em> test that one of the six pairs the DAT
/// mask removes (Ptolemaic ↔ Greece — one of T82's own derivation-only pairs, per
/// <c>NeighbourGeographyTests.DerivationOnlyPairs</c>) actually changes an AI decision, not merely
/// <see cref="NeighbourGeography.AreNeighbours"/>'s own answer. <see cref="AiOwnDiplomacyRule.BestWarTarget"/>
/// is a pure function of <c>(state, ruleset, world, meId)</c> with no <c>IRng</c> draw of its own, so
/// this is deterministic and needs no seeded round -- unlike the reviewer's own 40-round manual repro
/// (PR #393 review, "Ptolemaic declares war on Greece" at Autumn 269 BC week 3 on <c>main</c>, never on
/// this PR).
/// </summary>
public sealed class ClassicalNeighbourMaskAiBehaviourTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static NationState Nation(string id, string name, int wealth, int unity) =>
        new(
            Id: id,
            Name: name,
            ColorHex: "#000000",
            LeaderName: $"{name} Leader",
            CapitalCityId: null,
            Control: SeatControl.Ai,
            Personality: new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5),
            Treasury: 0,
            Unity: unity,
            Wealth: wealth,
            TaxBase: 0,
            TaxRatePercent: 25,
            MobilizedPercent: 0,
            Population: 0,
            PopulationAtStart: 0,
            TreasuryAtStart: 0,
            CityCountAtStart: 0,
            RecruitmentSlots: ValueList<RecruitmentSlot>.Empty,
            Eliminated: false);

    /// <summary>
    /// A minimal two-nation <see cref="GameState"/> -- Ptolemaic with overwhelming power, Greece with
    /// none, both at peace, neither at war/mobilised/protected -- isolated from the real classical
    /// scenario's own 16-nation starting state so the only thing that can gate
    /// <see cref="AiOwnDiplomacyRule.BestWarTarget"/>'s choice is the neighbour check itself. Power(n) =
    /// (wealth / 20000) × (unity / 100) (classical-faithful.json's own <c>aiOwnDiplomacy</c> block):
    /// Ptolemaic's is 50 × 5 = 250; Greece's truncates to 0 × 0 = 0. Ratio = 8×250 / max(1,0) = 2000,
    /// far past the ruleset's own base of 10, so Greece wins the ratio comparison outright whenever it
    /// is even considered a legal candidate -- the neighbour check is the only remaining gate.
    /// </summary>
    private static GameState TwoNationState(Ruleset ruleset)
    {
        var nations = ValueList.Of(
            Nation("ptolemaic", "Ptolemaic", wealth: 1_000_000, unity: 500),
            Nation("greece", "Greece", wealth: 1, unity: 1));
        var ids = ValueList.Of("ptolemaic", "greece");
        var peace = ruleset.Diplomacy.StateCodes.Peace;

        return new GameState(
            SchemaVersion: 1,
            WorldId: Classical.World.Id,
            RulesetId: ruleset.Id,
            ScenarioId: Classical.Scenario.Id,
            Calendar: new CalendarState(Week: ruleset.Calendar.StartWeek, SeasonIndex: 0, YearBc: 270, TurnIndex: 0),
            TurnOrder: ids,
            ActiveSeatIndex: 0,
            RandomSeed: 1UL,
            Nations: nations,
            Cities: ValueList<CityState>.Empty,
            Armies: ValueList<ArmyState>.Empty,
            Fleets: ValueList<FleetState>.Empty,
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(ids, peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: null);
    }

    [Fact]
    public void PtolemaicCanNoLongerPickGreeceAsAWarTargetOnTheLoadedClassicalWorld()
    {
        var state = TwoNationState(Classical.Ruleset);
        Assert.NotNull(Classical.World.StartingNeighbours);

        var best = AiOwnDiplomacyRule.BestWarTarget(state, Classical.Ruleset, Classical.World, "ptolemaic");

        Assert.Null(best);
    }

    /// <summary>
    /// The other half of N2's own ask: "show it fails if the world field is ignored (force the
    /// fallback)". Forcing <see cref="World.StartingNeighbours"/> to <see langword="null"/> on the exact
    /// same classical geometry restores the pre-T85 geometric derivation, under which Ptolemaic ↔ Greece
    /// <em>is</em> a (false) neighbour pair -- so the identical state, ruleset and nation ids now
    /// <em>do</em> pick Greece, proving it is the loaded field -- not the state, the ruleset or anything
    /// else -- that changed the earlier test's outcome.
    /// </summary>
    [Fact]
    public void PtolemaicWouldStillPickGreeceUnderTheForcedFallbackDerivation()
    {
        var derivedWorld = Classical.World with { StartingNeighbours = null };
        var state = TwoNationState(Classical.Ruleset);

        var best = AiOwnDiplomacyRule.BestWarTarget(state, Classical.Ruleset, derivedWorld, "ptolemaic");

        Assert.Equal("greece", best);
    }
}
