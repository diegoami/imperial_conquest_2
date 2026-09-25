using System.Text.RegularExpressions;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Strength;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The field variant of the instant resolver — <c>FUN_0044AEE4</c>. Done-when lines 1, 2, 3, 4, 5, 6,
/// 8 and 9 of <c>docs/task-catalogue.md</c> T16, all under <see cref="BattleTestbed.Seed"/> with exact
/// assertions.
/// </summary>
/// <remarks>
/// The fixture is deliberately lopsided so that who wins is never in doubt and every derived number is
/// checkable by hand from the ruleset: the attacker's <see cref="ArmyPower"/> is 6,120 and the
/// defender's 3,600, on the toy ruleset's own weights.
/// </remarks>
public class FieldBattleTests
{
    private const string Attacker = "north-attacker";
    private const string Defender = "south-defender";

    /// <summary>Done-when 1, first half: the higher-power side wins.</summary>
    [Fact]
    public void DoD01_TheHigherPowerSideWins()
    {
        var state = Fixture();
        var (_, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(6120, result.AttackerPower);
        Assert.Equal(3600, result.DefenderPower);
        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.Equal(Attacker, result.WinnerId);
        Assert.Equal(Defender, result.LoserId);
    }

    /// <summary>
    /// Done-when 1, second half: an exact tie goes to the defender — <c>winner = (pB &lt; pA) ? attacker
    /// : defender</c> is a strict less-than (<c>battle.instantResolver.tieGoesToDefender</c>).
    /// </summary>
    [Fact]
    public void DoD01_AnExactTieGoesToTheDefender()
    {
        var identical = new[]
        {
            BattleTestbed.Unit("light_infantry", 9000, 5, "Twin A"),
        };

        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Attacker, "north", 1, 2, 60, 100, 50, identical),
                BattleTestbed.Army(Defender, "south", 2, 2, 60, 100, 50, identical),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(result.AttackerPower, result.DefenderPower);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Null(after.ArmyById(Attacker));
        Assert.NotNull(after.ArmyById(Defender));
    }

    /// <summary>Done-when 2: under <c>classical-faithful</c> the loser's army is destroyed outright.</summary>
    [Fact]
    public void DoD02_TheLosersArmyIsDestroyedOutright()
    {
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Equal(DefeatOutcome.Destroyed, result.AppliedDefeatOutcome);
        Assert.Null(after.ArmyById(Defender));
        Assert.Single(after.Armies);

        // Destroyed outright means every troop, not a fraction: the loser's whole force is the casualty
        // figure this result reports.
        Assert.Equal(12000, result.LoserCasualties);
        Assert.Null(result.Scatter);
    }

    /// <summary>
    /// Done-when 3: <c>loserPower × 40 / winnerPower</c> is a <strong>ratio</strong>, applied to every
    /// unit as <c>troops -= troops / (Random(15) + 105) × ratio</c>, with the integer semantics pinned —
    /// and unaffected by <c>combat.onDefeat</c>.
    /// </summary>
    /// <remarks>
    /// The worked example this test pins, from the seed's own first three draws (13, 5, 11, so divisors
    /// 118, 110, 116) and a ratio of 23:
    /// <code>
    /// slot 0  12,000 / 118 = 101   101 x 23 = 2,323   -&gt;  9,677
    /// slot 1   4,000 / 110 =  36    36 x 23 =   828   -&gt;  3,172
    /// slot 2   2,000 / 116 =  17    17 x 23 =   391   -&gt;  1,609
    ///                                       total 3,542 of 18,000 = 19.7%
    /// </code>
    /// </remarks>
    [Fact]
    public void DoD03_WinnerCasualtiesAreAPerUnitRatioNotATroopCount()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        // The ratio, computed here from the ruleset's own numerator rather than pasted in:
        // 3600 * 40 / 6120 = 144000 / 6120 = 23 (23.52 truncated).
        var ratio = (result.LoserPower * rules.WinnerCasualtyNumerator) / result.WinnerPower;
        Assert.Equal(23, ratio);

        // Multiplication first, then ONE truncating division. The other reading, loserPower * (40 /
        // winnerPower), truncates the inner quotient to zero for any winnerPower above the numerator.
        Assert.Equal(0, rules.WinnerCasualtyNumerator / result.WinnerPower);

        // Each slot's own loss is troops / divisor, THEN x ratio, with the divisor drawn per slot from
        // [105, 120). The bounds are checked against the ruleset rather than taken on trust.
        var divisors = new[] { 118, 110, 116 };
        Assert.All(divisors, d => Assert.InRange(
            d, rules.CasualtyDivisorBase, rules.CasualtyDivisorBase + rules.CasualtyDivisorRandomSpan - 1));

        var troops = new[] { 12000, 4000, 2000 };
        var expected = new[]
        {
            (troops[0] / divisors[0]) * ratio,
            (troops[1] / divisors[1]) * ratio,
            (troops[2] / divisors[2]) * ratio,
        };

        Assert.Equal(new[] { 2323, 828, 391 }, expected);
        Assert.Equal(
            new[] { (0, 2323), (1, 828), (2, 391) },
            result.UnitCasualties.Select(c => (c.SlotIndex, c.TroopsLost)).ToArray());
        Assert.Equal(3542, result.WinnerCasualties);
        Assert.Equal(result.WinnerCasualties, result.UnitCasualties.Sum(c => c.TroopsLost));

        // It really is the ratio reading: the withdrawn count reading would have cost this winner 23
        // troops in all, 154 times fewer than the 3,542 it actually loses.
        Assert.True(result.WinnerCasualties > ratio * 100);

        // And the per-unit grouping is troops/divisor first: (12000 * 23) / 118 = 2,338, not 2,323.
        Assert.NotEqual((troops[0] * ratio) / divisors[0], expected[0]);

        var winner = after.ArmyById(Attacker)!;
        Assert.Equal(new[] { 9677, 3172, 1609 }, winner.Units.Select(u => u.Troops).ToArray());
        Assert.Equal(18000 - 3542, winner.TotalTroops);

        // Unaffected by combat.onDefeat.
        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.WinnerCasualties, scattered.WinnerCasualties);
        Assert.Equal(result.UnitCasualties, scattered.UnitCasualties);
    }

    /// <summary>
    /// Done-when 4: the winner absorbs the loser's money, and supplies capped at <c>troops / 100</c> —
    /// unaffected by <c>combat.onDefeat</c>.
    /// </summary>
    /// <remarks>
    /// This is the instant-battle <em>attacker</em>-wins branch, the one
    /// <c>supply-capacity-rounding.md</c> records as capped
    /// (<c>FUN_0044AEE4</c> lines 49647-49654). The defender-wins branch is
    /// <see cref="DoD04_ADefenderThatWinsTakesAPlainSumWithNoCap"/>, which the same report records as
    /// uncapped — an asymmetry copied exactly rather than made consistent.
    /// </remarks>
    [Fact]
    public void DoD04_TheWinnerAbsorbsMoneyAndSuppliesCappedAtTroopsOverOneHundred()
    {
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        var winner = after.ArmyById(Attacker)!;
        Assert.Equal(90, result.AbsorbedMoney);
        Assert.Equal(256 + 90, winner.Money);

        // 185 + 40 = 225 tons on offer; the cap is the winner's POST-casualty troops over the ruleset's
        // own divisor, 14,458 / 100 = 144, and it binds.
        var cap = winner.TotalTroops / BattleTestbed.Destroyed.Economy.ArmySupplyTonsPerTroops;
        Assert.Equal(144, cap);
        Assert.Equal(cap, result.AbsorbedSupplyTons);
        Assert.Equal(cap, winner.SupplyTons);
        Assert.True(225 > cap, "the fixture must put more supply on offer than the cap allows");

        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.AbsorbedMoney, scattered.AbsorbedMoney);
        Assert.Equal(result.AbsorbedSupplyTons, scattered.AbsorbedSupplyTons);
    }

    /// <summary>
    /// Done-when 4, the other half of the asymmetry: an instant-battle <em>defender</em> that wins takes
    /// a plain sum with no cap at all (<c>supply-capacity-rounding.md</c>, lines 49687-49690).
    /// </summary>
    [Fact]
    public void DoD04_ADefenderThatWinsTakesAPlainSumWithNoCap()
    {
        // The same two armies, with the stronger one defending.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 60, 90, 40,
                    BattleTestbed.Unit("light_infantry", 9000, 6, "Weak Foot"),
                    BattleTestbed.Unit("heavy_infantry", 3000, 6, "Weak Guards")),
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 68, 256, 185,
                    BattleTestbed.Unit("light_infantry", 12000, 5, "Strong Foot"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 6, "Strong Guards"),
                    BattleTestbed.Unit("archers", 2000, 9, "Strong Bowmen")),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(BattleSide.Defender, result.Winner);

        var winner = after.ArmyById(Defender)!;
        var cap = winner.TotalTroops / BattleTestbed.Destroyed.Economy.ArmySupplyTonsPerTroops;

        Assert.Equal(185 + 40, result.AbsorbedSupplyTons);
        Assert.Equal(225, winner.SupplyTons);
        Assert.True(result.AbsorbedSupplyTons > cap, "the uncapped branch must exceed the cap the other branch would apply");
    }

    /// <summary>
    /// Done-when 5: every surviving unit ends at ≥ "average", and exactly the 1-in-4 further promotions
    /// fire for the seeded roll.
    /// </summary>
    [Fact]
    public void DoD05_EverySurvivorReachesTheFloorAndExactlyTheSeededPromotionsFire()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        var winner = after.ArmyById(Attacker)!;

        // The floor applies to every survivor: the slot that started at 5 is raised to 6 with no roll.
        Assert.All(winner.Units, unit => Assert.True(unit.Quality >= combat.QualityFloor));

        // Exactly one of the three rolls fires under this seed -- the middle slot's -- and it is the only
        // slot whose quality moved for that reason.
        Assert.Equal(
            new[] { (0, 5, 6, false), (1, 6, 7, true), (2, 9, 9, false) },
            result.Promotions
                .Select(p => (p.SlotIndex, p.QualityBefore, p.QualityAfter, p.PromotedByRoll))
                .ToArray());
        Assert.Equal(1, result.Promotions.Count(p => p.PromotedByRoll));
        Assert.Equal(new[] { 6, 7, 9 }, winner.Units.Select(u => u.Quality).ToArray());

        // Only the winner is promoted; nothing touches the loser (which in this ruleset no longer exists).
        Assert.Single(after.Armies);
    }

    /// <summary>
    /// Done-when 5, the cap clause: a unit already at "elite" that wins the further roll stays at
    /// "elite" rather than climbing past it.
    /// </summary>
    [Fact]
    public void DoD05_QualityIsCappedAtElite()
    {
        var combat = BattleTestbed.Destroyed.Combat;

        // Three winner slots, so the seed's promotion draws are the same 2, 0, 1 as the main fixture's
        // and the one that fires lands on the elite slot in the middle.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 68, 0, 0,
                    BattleTestbed.Unit("heavy_infantry", 4000, 5, "Green Guards"),
                    BattleTestbed.Unit("heavy_infantry", 4000, combat.QualityCap, "Elite Guards"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 5, "Second Green Guards")),
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 30, 0, 0,
                    BattleTestbed.Unit("light_infantry", 1000, 6, "Doomed Foot")),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.True(result.Promotions[1].PromotedByRoll, "the seed's second promotion draw must be the promoting one");
        Assert.Equal(combat.QualityCap, result.Promotions[1].QualityBefore);
        Assert.Equal(combat.QualityCap, result.Promotions[1].QualityAfter);
        Assert.Equal(combat.QualityCap, after.ArmyById(Attacker)!.Units[1].Quality);
    }

    /// <summary>
    /// Done-when 6, the land half: unity moves the loser −25 and the winner +25, and is unaffected by
    /// <c>combat.onDefeat</c>.
    /// </summary>
    [Fact]
    public void DoD06_UnityMovesLoserDownAndWinnerUpByTheRulesetsSwing()
    {
        var swing = BattleTestbed.Destroyed.Combat.UnitySwing;
        Assert.Equal(25, swing);

        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(swing, result.WinnerUnityDelta);
        Assert.Equal(-swing, result.LoserUnityDelta);
        Assert.Equal(600 + swing, after.NationById("north")!.Unity);
        Assert.Equal(560 - swing, after.NationById("south")!.Unity);

        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.WinnerUnityDelta, scattered.WinnerUnityDelta);
        Assert.Equal(result.LoserUnityDelta, scattered.LoserUnityDelta);
    }

    /// <summary>Done-when 6, the clamp: the winner's unity stops at the ruleset's cap.</summary>
    [Fact]
    public void DoD06_TheWinnersUnityIsClampedAtTheCap()
    {
        var cap = BattleTestbed.Destroyed.Economy.UnityCap;
        Assert.Equal(990, cap);

        var baseState = Fixture();
        var state = baseState with
        {
            Nations = ValueList.From(new[]
            {
                BattleTestbed.NationWithUnity(baseState, "north", cap - 10),
                BattleTestbed.NationWithUnity(baseState, "south", 560),
            }),
        };

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(cap, after.NationById("north")!.Unity);
        Assert.Equal(10, result.WinnerUnityDelta);
    }

    /// <summary>
    /// Done-when 8: <c>PeaceTreatyTriggered</c> is published on the 2-in-5 roll, gated on the loser's
    /// unity above 500 and its city count above 7 — and is observable with no diplomacy system
    /// registered.
    /// </summary>
    [Fact]
    public void DoD08_PeaceTreatyTriggeredIsPublishedWhenTheRollAndBothGatesPass()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = PeaceGateFixture(loserUnity: 560, loserCityCount: 9);

        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        // Both gates are read AFTER the battle's own unity swing, as the original's own order does.
        var loser = after.NationById("south")!;
        Assert.True(loser.Unity > combat.AutoPeaceLoserUnityThreshold);
        Assert.True(after.CountCitiesOwnedBy("south") > combat.AutoPeaceLoserCityThreshold);

        Assert.True(result.PeaceTreatyFired);
        var treaty = Assert.Single(events.Events.OfType<PeaceTreatyTriggered>());
        Assert.Equal("north", treaty.WinnerNationId);
        Assert.Equal("south", treaty.LoserNationId);
        Assert.Equal(535, treaty.LoserUnity);
        Assert.Equal(9, treaty.LoserCityCount);

        // "Observable in a test with no diplomacy system registered" (T16 DoD 8) is already proved by
        // the three lines above: the resolver was called directly with a bare RecordingEventSink -- no
        // registry, no pipeline, nothing registered because there is nothing to register into -- and
        // the event still arrived. That is the proof that the resolver publishes rather than calling
        // diplomacy, and it needs nothing further.
        //
        // What DOES need its own check is the actual contract behind that wording -- T16's own Scope
        // note: "Emits a PeaceTreatyTriggered domain event rather than calling diplomacy, so this task
        // and T19 do not depend on each other's internals." The checkable, durable form of that is a
        // source guard: src/IC2.Engine/Battle/** must never reference the Diplomacy namespace or its
        // types, so nobody can quietly turn "publish" back into "call". (#194: the previous replacement
        // here -- a registry scoped to an invented TestFixtureGroup nothing tagged itself with -- was
        // the same defect class as the assertion it replaced: true only because of what did not exist,
        // not because of what the resolver does.)
        AssertNoBattleSourceReferencesDiplomacy();
    }

    /// <summary>
    /// DoD 8's decoupling guard, modeled on <see cref="BattleDeterminismTests.NoReserveTacticalResearchAppearsInTheBattleNamespacesCode"/>:
    /// a text scan of every <c>src/IC2.Engine/Battle/**</c> file (comments stripped, so a doc comment
    /// naming the namespace for exposition does not trip it) for the Diplomacy namespace and the
    /// concrete types this task introduces there. Unlike a registry-emptiness check, this fails the
    /// moment battle code actually references diplomacy, whether or not anything happens to be
    /// registered anywhere else in the assembly.
    /// </summary>
    private static void AssertNoBattleSourceReferencesDiplomacy()
    {
        var offenders = DiplomacyReferencesInBattleSource();
        Assert.True(
            offenders.Count == 0,
            "Battle must publish PeaceTreatyTriggered rather than call diplomacy directly: "
            + string.Join(", ", offenders));
    }

    private static IReadOnlyList<string> DiplomacyReferencesInBattleSource()
    {
        string[] forbidden =
        {
            "IC2.Engine.Diplomacy",
            "RelationTransitions",
            "PeaceTreatySystem",
            "PendingOfferSystem",
            "QuarterlyThawSystem",
            "TradePartnerCap",
            "ReparationsFormula",
            "HonourablePeaceGate",
        };

        var battleSources = Directory.GetFiles(
            Path.Combine(TestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(battleSources);

        var offenders = new List<string>();
        foreach (var file in battleSources)
        {
            var code = StripCommentsForDiplomacyGuard(File.ReadAllText(file));
            foreach (var name in forbidden)
            {
                if (code.Contains(name, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {name}");
                }
            }
        }

        return offenders;
    }

    private static string StripCommentsForDiplomacyGuard(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }

    /// <summary>Done-when 8, the unity gate: the same seeded roll fires, and the gate still refuses.</summary>
    [Fact]
    public void DoD08_TheUnityGateBlocksTheTreaty()
    {
        var combat = BattleTestbed.Destroyed.Combat;

        // 520 − 25 = 495, one swing below the threshold.
        var state = PeaceGateFixture(loserUnity: 520, loserCityCount: 9);
        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        Assert.Equal(495, after.NationById("south")!.Unity);
        Assert.False(after.NationById("south")!.Unity > combat.AutoPeaceLoserUnityThreshold);
        Assert.False(result.PeaceTreatyFired);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyTriggered);
    }

    /// <summary>Done-when 8, the city gate: exactly at the threshold is not above it.</summary>
    [Fact]
    public void DoD08_TheCityCountGateBlocksTheTreaty()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = PeaceGateFixture(loserUnity: 560, loserCityCount: combat.AutoPeaceLoserCityThreshold);

        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        Assert.Equal(7, after.CountCitiesOwnedBy("south"));
        Assert.False(result.PeaceTreatyFired);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyTriggered);
    }

    // ---- T88 DoD 3: the human-consent treaty (report §2.3) ----
    //
    // The toy scenario's own seats are "north" (human) and "south" (AI) -- Attacker/Defender below --
    // so every fixture in this region is naturally the mixed-human case the report's own gate differs
    // for: armies(winner) < armies(loser), tested first and with no draw; then unity(loser) > 500; then
    // cities(loser) > 7; only then Random(5) < 2, and never taken if an earlier gate failed.

    /// <summary>
    /// DoD 3, the strength gate at its boundary: equal army power fails (not strictly less), one more on
    /// the loser's side passes. Both other gates comfortably pass in both cases, so this isolates the
    /// strength gate alone. Proves the draw itself is skipped on failure via <see cref="ConstantRng"/>'s
    /// own draw count, the same differential proof <see cref="StrengthGate_DrawSkippedOnFailure"/> reuses
    /// for the "not taken" half of the Done-when line.
    /// </summary>
    [Fact]
    public void HumanConsent_StrengthGate_EqualFails_OneMoreOnTheLoserPasses()
    {
        var ruleset = BattleTestbed.Destroyed;

        var equal = HumanConsentFixture(
            ruleset, winnerArmyPower: 10, loserOtherArmyPower: 10, loserUnityBeforeSwing: 600, loserCityCount: 9);
        var equalEvents = new RecordingEventSink();
        var (_, equalResult) = ResolveWithRng(equal, ruleset, new ConstantRng(0), equalEvents);
        Assert.False(equalResult.PeaceTreatyOffered);
        Assert.DoesNotContain(equalEvents.Events, e => e is PeaceTreatyOffered);

        var oneMore = HumanConsentFixture(
            ruleset, winnerArmyPower: 10, loserOtherArmyPower: 11, loserUnityBeforeSwing: 600, loserCityCount: 9);
        var oneMoreEvents = new RecordingEventSink();
        var (_, oneMoreResult) = ResolveWithRng(oneMore, ruleset, new ConstantRng(0), oneMoreEvents);
        Assert.True(oneMoreResult.PeaceTreatyOffered);
        Assert.False(oneMoreResult.PeaceTreatyFired);
        var offered = Assert.Single(oneMoreEvents.Events.OfType<PeaceTreatyOffered>());
        Assert.Equal("north", offered.WinnerNationId);
        Assert.Equal("south", offered.LoserNationId);
        Assert.DoesNotContain(oneMoreEvents.Events, e => e is PeaceTreatyTriggered);
    }

    /// <summary>
    /// DoD 3, the unity gate at its boundary: 500 (the swung value) fails, 501 passes. Pre-swing values
    /// are 25 higher, matching <see cref="DoD08_TheUnityGateBlocksTheTreaty"/>'s own arithmetic.
    /// </summary>
    [Fact]
    public void HumanConsent_UnityGate_500Fails_501Passes()
    {
        var ruleset = BattleTestbed.Destroyed;
        var swing = ruleset.Combat.UnitySwing;

        var atThreshold = HumanConsentFixture(
            ruleset, winnerArmyPower: 5, loserOtherArmyPower: 50,
            loserUnityBeforeSwing: 500 + swing, loserCityCount: 9);
        var (afterAt, resultAt) = ResolveWithRng(atThreshold, ruleset, new ConstantRng(0));
        Assert.Equal(500, afterAt.NationById("south")!.Unity);
        Assert.False(resultAt.PeaceTreatyOffered);

        var aboveThreshold = HumanConsentFixture(
            ruleset, winnerArmyPower: 5, loserOtherArmyPower: 50,
            loserUnityBeforeSwing: 501 + swing, loserCityCount: 9);
        var (afterAbove, resultAbove) = ResolveWithRng(aboveThreshold, ruleset, new ConstantRng(0));
        Assert.Equal(501, afterAbove.NationById("south")!.Unity);
        Assert.True(resultAbove.PeaceTreatyOffered);
    }

    /// <summary>DoD 3, the city gate at its boundary: 7 cities fails (not strictly above), 8 passes.</summary>
    [Fact]
    public void HumanConsent_CityGate_7Fails_8Passes()
    {
        var ruleset = BattleTestbed.Destroyed;

        var atThreshold = HumanConsentFixture(
            ruleset, winnerArmyPower: 5, loserOtherArmyPower: 50, loserUnityBeforeSwing: 600, loserCityCount: 7);
        var (afterAt, resultAt) = ResolveWithRng(atThreshold, ruleset, new ConstantRng(0));
        Assert.Equal(7, afterAt.CountCitiesOwnedBy("south"));
        Assert.False(resultAt.PeaceTreatyOffered);

        var aboveThreshold = HumanConsentFixture(
            ruleset, winnerArmyPower: 5, loserOtherArmyPower: 50, loserUnityBeforeSwing: 600, loserCityCount: 8);
        var (afterAbove, resultAbove) = ResolveWithRng(aboveThreshold, ruleset, new ConstantRng(0));
        Assert.Equal(8, afterAbove.CountCitiesOwnedBy("south"));
        Assert.True(resultAbove.PeaceTreatyOffered);
    }

    /// <summary>
    /// DoD 3: "The Random(5) draw is not taken when an earlier gate fails." Proved differentially, not
    /// just by outcome: two otherwise-identical fixtures (same winner army shape, so the same number of
    /// casualty/promotion draws precede the gate either way) differ only in whether the strength gate
    /// passes, and <see cref="ConstantRng.DrawCount"/> differs by exactly one between them -- the one
    /// extra draw the passing case alone takes.
    /// </summary>
    [Fact]
    public void StrengthGate_DrawSkippedOnFailure()
    {
        var ruleset = BattleTestbed.Destroyed;

        var failing = HumanConsentFixture(
            ruleset, winnerArmyPower: 10, loserOtherArmyPower: 10, loserUnityBeforeSwing: 600, loserCityCount: 9);
        var failingRng = new ConstantRng(0);
        ResolveWithRng(failing, ruleset, failingRng);

        var passing = HumanConsentFixture(
            ruleset, winnerArmyPower: 10, loserOtherArmyPower: 11, loserUnityBeforeSwing: 600, loserCityCount: 9);
        var passingRng = new ConstantRng(0);
        ResolveWithRng(passing, ruleset, passingRng);

        Assert.Equal(failingRng.DrawCount + 1, passingRng.DrawCount);
    }

    /// <summary>
    /// A battle between two AI seats never raises <see cref="PeaceTreatyOffered"/> — that is
    /// <see cref="DoD08_PeaceTreatyTriggeredIsPublishedWhenTheRollAndBothGatesPass"/>'s own path, publishing
    /// <see cref="PeaceTreatyTriggered"/> instead, unaffected by this task apart from the cascade fixes
    /// (DoD 3's own last bullet).
    /// </summary>
    [Fact]
    public void HumanConsent_BothAi_NeverPublishesPeaceTreatyOffered()
    {
        var ruleset = BattleTestbed.Destroyed;
        var state = PeaceGateFixture(loserUnity: 560, loserCityCount: 9);

        var events = new RecordingEventSink();
        var (_, result) = ResolveWithRng(state, ruleset, new ConstantRng(0), events);

        Assert.True(result.PeaceTreatyFired);
        Assert.False(result.PeaceTreatyOffered);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyOffered);
    }

    /// <summary>
    /// A battle between two human seats (hotseat) never raises either treaty event — the original's
    /// <c>THVHBatPols</c> writes no relation at all (report §2.3).
    /// </summary>
    [Fact]
    public void HumanConsent_BothHuman_NeverPublishesEitherTreatyEvent()
    {
        var ruleset = BattleTestbed.Destroyed;
        var initial = BattleTestbed.Initial();
        var state = HumanConsentFixture(
            ruleset, winnerArmyPower: 10, loserOtherArmyPower: 50, loserUnityBeforeSwing: 600, loserCityCount: 9);
        state = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, "south", StringComparison.Ordinal) ? n with { Control = SeatControl.Human } : n)),
        };

        var events = new RecordingEventSink();
        var (_, result) = ResolveWithRng(state, ruleset, new ConstantRng(0), events);

        Assert.False(result.PeaceTreatyFired);
        Assert.False(result.PeaceTreatyOffered);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyTriggered or PeaceTreatyOffered);
    }

    /// <summary>
    /// A field battle fixture between the toy scenario's own human seat ("north") and AI seat ("south"),
    /// with every input to the human-consent gate independently controllable and exact:
    /// <see cref="Attacker"/>'s single <c>light_infantry</c> slot at morale 1 has power
    /// <paramref name="winnerArmyPower"/> exactly (<see cref="TroopsForExactPower"/>); <see cref="Defender"/>'s
    /// battling slot has troops = 1, which floors to army power 0 on this ruleset (<c>20 × 1 / 100 = 0</c>),
    /// so it draws the winner zero casualties and the winner's post-battle power equals
    /// <paramref name="winnerArmyPower"/> without resolving the battle first to find out; a second,
    /// untouched "south" army elsewhere gives <c>armies(south)</c> exactly <paramref name="loserOtherArmyPower"/>
    /// once the battling loser army is annihilated; <paramref name="loserUnityBeforeSwing"/> is read after
    /// the battle's own -25 swing, exactly as the gate does; <paramref name="loserCityCount"/> is exact via
    /// <see cref="BattleTestbed.FillerCities"/>.
    /// </summary>
    private static GameState HumanConsentFixture(
        Ruleset ruleset, int winnerArmyPower, int loserOtherArmyPower, int loserUnityBeforeSwing, int loserCityCount)
    {
        var initial = BattleTestbed.Initial();

        return BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, morale: 1, money: 0, supplyTons: 0,
                    BattleTestbed.Unit(
                        "light_infantry", TroopsForExactPower(ruleset, winnerArmyPower), 5, "Winner Force")),
                BattleTestbed.Army(
                    Defender, "south", 2, 2, morale: 60, money: 0, supplyTons: 0,
                    BattleTestbed.Unit("light_infantry", 1, 5, "Doomed Outpost")),
                BattleTestbed.Army(
                    "south-home-guard", "south", 50, 50, morale: 1, money: 0, supplyTons: 0,
                    BattleTestbed.Unit(
                        "light_infantry", TroopsForExactPower(ruleset, loserOtherArmyPower), 5, "Home Guard")),
            },
            cities: BattleTestbed.FillerCities("south", loserCityCount, 0, 5),
            nations: new[]
            {
                BattleTestbed.NationWithUnity(initial, "north", 600),
                BattleTestbed.NationWithUnity(initial, "south", loserUnityBeforeSwing),
            });
    }

    /// <summary>
    /// The exact troop count a single <c>light_infantry</c> slot at morale 1 needs to reach
    /// <paramref name="power"/> on this ruleset, with no truncation to account for:
    /// <c>ArmyPower.Compute</c> is <c>floor(floor(weight × troops / PowerTroopDivisor) / PowerDivisor) × morale</c>,
    /// so <c>troops = power × PowerTroopDivisor × PowerDivisor / weight</c> lands the outer floor on
    /// <paramref name="power"/> exactly whenever that product divides evenly by <c>weight</c> (true for
    /// every <paramref name="power"/> this file passes, on <c>light_infantry</c>'s toy-ruleset weight).
    /// </summary>
    private static int TroopsForExactPower(Ruleset ruleset, int power)
    {
        var weight = ruleset.UnitTypeById("light_infantry")!.CombatPowerWeight;
        return power * ruleset.Combat.PowerTroopDivisor * ruleset.Combat.PowerDivisor / weight;
    }

    private static BattleResolution ResolveWithRng(GameState state, Ruleset ruleset, IRng rng, IEventSink? events = null) =>
        InstantBattleResolver.ResolveField(
            state, Attacker, Defender, ruleset, BattleTestbed.World, rng, events ?? NullEventSink.Instance);

    /// <summary>
    /// Returns the same fixed value for every draw, regardless of the requested bound, and counts how
    /// many draws it served — the two things <see cref="StrengthGate_DrawSkippedOnFailure"/> and this
    /// region's other boundary tests need: a favourable, deterministic peace roll when the gates do pass,
    /// and a way to prove the draw was skipped, not merely unfavourable, when they do not.
    /// </summary>
    private sealed class ConstantRng : IRng
    {
        private readonly int _value;

        public ConstantRng(int value) => _value = value;

        public int DrawCount { get; private set; }

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64()
        {
            DrawCount++;
            return (ulong)_value;
        }

        public int NextInt(int exclusiveUpperBound)
        {
            DrawCount++;
            return _value;
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound)
        {
            DrawCount++;
            return inclusiveLowerBound;
        }

        public bool NextChance(int numerator, int denominator)
        {
            DrawCount++;
            return true;
        }

        public IRng ForStream(string streamName) => this;
    }

    /// <summary>
    /// Done-when 9, the land half: the confirmed news message
    /// <c>"&lt;winner&gt; destroys army of &lt;loser&gt;."</c> is emitted and renders through the
    /// already-registered catalog template.
    /// </summary>
    [Fact]
    public void DoD09_TheConfirmedDestroysArmyNewsMessageIsEmitted()
    {
        var state = Fixture();
        var events = new RecordingEventSink();
        var (after, _) = Resolve(state, BattleTestbed.Destroyed, events);

        var destroyed = Assert.Single(events.Events.OfType<BattleArmyDestroyed>());
        Assert.Equal("battle.army-destroyed", destroyed.Kind);
        Assert.True(destroyed.IsNewsWorthy);
        Assert.Equal("Northern League", destroyed.Winner);
        Assert.Equal("Southern League", destroyed.Loser);

        var logged = NewsLogWriter.Append(after, events.Events, BattleTestbed.Destroyed.NewsLog);
        Assert.Equal(
            "Northern League destroys army of Southern League.",
            logged.NewsLog.Slots[logged.NewsLog.MostRecentSlot].Text);
    }

    /// <summary>
    /// The battle spends the attacker's whole move whatever the outcome
    /// (<c>attacker.moves = 0</c>, the first line of <c>FUN_0044AEE4</c>) — which is also what stops the
    /// victor chasing a scattered survivor in the same turn.
    /// </summary>
    [Fact]
    public void TheAttackersMoveIsSpentWhetherItWinsOrLoses()
    {
        var (won, _) = Resolve(Fixture(), BattleTestbed.Destroyed);
        Assert.Equal(0, won.ArmyById(Attacker)!.Moves);

        var lostState = BattleTestbed.StateWith(
            armies: new[]
            {
                // Close enough that the mirrored ratio (5,100 x 40 / 4,080 = 50, well under the divisor)
                // leaves survivors to relocate.
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 68, 0, 0,
                    BattleTestbed.Unit("light_infantry", 12000, 6, "Outmatched Foot"),
                    BattleTestbed.Unit("light_infantry", 12000, 6, "Outmatched Foot II")),
                BattleTestbed.Army(Defender, "south", 2, 2, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 6000, 6, "Strong Guards")),
            });

        var (lost, result) = Resolve(lostState, BattleTestbed.Scatter);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(LoserFate.Scattered, result.LoserFate);
        Assert.Equal(0, lost.ArmyById(Attacker)!.Moves);
    }

    /// <summary>
    /// T52 DoD 8: <c>ClearCarrierLinks</c> is no longer an untested branch. A field battle is fought
    /// ashore, so <em>this</em> resolver never puts a fleet's carrier claim on either combatant itself --
    /// but nothing stops some other fleet's <c>CarriedArmyId</c> from going stale by pointing at an army
    /// that a field battle then deletes (the class of bug the guard exists for, not a path this resolver
    /// can reach on its own). Constructed directly, per <c>docs/build-process.md</c> §4.2 gate 5's own
    /// "prefer the test to the delete": a fleet with a claim on the loser, deliberately inconsistent
    /// before the battle (the loser isn't embarked; the pointer is simply stale), must have that claim
    /// cleared once the loser is deleted -- and the two-entity probe: a second fleet's claim on an
    /// unrelated, surviving army must be left alone.
    /// </summary>
    [Fact]
    public void DoD08_ClearCarrierLinksRepairsAStaleClaimOnADeletedLoserAndLeavesAnUnrelatedOneAlone()
    {
        const string StaleFleet = "stale-fleet";
        const string ValidFleet = "valid-fleet";
        const string UnrelatedCargo = "unrelated-cargo";

        var initial = Fixture();
        var state = initial with
        {
            Armies = ValueList.From(initial.Armies.Append(
                BattleTestbed.EmbarkedArmy(
                    UnrelatedCargo, "north", ValidFleet, 0, 0, 60,
                    BattleTestbed.Unit("light_infantry", 500, 6, "Untouched")))),
            Fleets = ValueList.From(new[]
            {
                // Stale on purpose: this fleet claims the DEFENDER (about to be this battle's loser), even
                // though the defender is ashore and fighting, not aboard it -- the one-way pointer a save
                // loader would reject, and exactly the shape a bug elsewhere (a disembark that forgot to
                // clear the fleet's own side of the link) would leave behind.
                BattleTestbed.Fleet(StaleFleet, "south", 5, 5, 10, 100, Defender),
                // The control: a second fleet with a VALID claim on an army untouched by this battle.
                BattleTestbed.Fleet(ValidFleet, "north", 0, 0, 10, 100, UnrelatedCargo),
            }),
        };

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Null(after.ArmyById(Defender));

        // Repaired: the stale claim on the deleted loser is gone.
        Assert.Null(after.FleetById(StaleFleet)!.CarriedArmyId);

        // Left alone: the unrelated, still-valid claim survives untouched.
        Assert.Equal(UnrelatedCargo, after.FleetById(ValidFleet)!.CarriedArmyId);
        Assert.NotNull(after.ArmyById(UnrelatedCargo));

        // And the repaired state is one that reloads -- a dangling id is exactly what this guard, and
        // gate 5, exist to keep out of a save. B7 (T63 review round 1): through T20's SaveManager, not
        // just GameDataLoader/GameDataValidation -- see AssertRoundTripsThroughSaveManager's own remarks.
        AssertRoundTripsThroughSaveManager(after);
    }

    /// <summary>
    /// T63 DoD 1's "Delete sweep": if the WINNER's own deletion pass (#289) empties it entirely, the army
    /// is deleted outright, not persisted as a zero-unit record. Two armies, one emptied and one
    /// surviving, per the Done-when line's own wording, plus the same round-trip proof
    /// <see cref="DoD08_ClearCarrierLinksRepairsAStaleClaimOnADeletedLoserAndLeavesAnUnrelatedOneAlone"/>
    /// uses.
    /// </summary>
    [Fact]
    public void WinnerEmptiedByItsOwnDeletionPass_IsDeletedNotPersistedAsZeroUnits()
    {
        const string Bystander = "bystander-army";

        // The defender wins (the attacker below is essentially powerless, and ties go to the defender
        // anyway), but its own single unit starts at 340 troops -- already below archers' 350 national
        // deletion threshold -- so the deletion pass removes it regardless of how small the casualty
        // ratio itself turns out to be: the pass reads the post-casualty troop count, not the ratio that
        // produced it (bug #289's own point, DoD 1's "naval or storm pass at ratio 0" restated for field).
        var winner = BattleTestbed.Army(
            Defender, "south", 5, 5, 60, 0, 0,
            BattleTestbed.Unit("archers", 340, 6, "TooSmall"));
        var loser = BattleTestbed.Army(
            Attacker, "north", 4, 5, 1, 0, 0,
            BattleTestbed.Unit("light_infantry", 1, 1, "Negligible"));
        var bystander = BattleTestbed.Army(
            Bystander, "north", 20, 20, 60, 0, 0,
            BattleTestbed.Unit("light_infantry", 12000, 6, "Untouched"));

        var state = BattleTestbed.StateWith(armies: new[] { winner, loser, bystander });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(LoserFate.Destroyed, result.LoserFate);

        // The winner's own army is gone -- not present with zero units, gone entirely -- alongside the
        // loser's.
        Assert.Null(after.ArmyById(Defender));
        Assert.Null(after.ArmyById(Attacker));

        // The unrelated bystander survives untouched.
        var survivor = after.ArmyById(Bystander);
        Assert.NotNull(survivor);
        Assert.Equal(12000, survivor!.TotalTroops);

        // And the resulting state -- with the winner deleted alongside the loser -- still round-trips.
        // B7 (T63 review round 1): through T20's SaveManager, the round-trip DoD 1 actually names, not
        // just GameDataLoader/GameDataValidation (a weaker check the loader's own Validate call already
        // makes redundant here).
        AssertRoundTripsThroughSaveManager(after);
    }

    /// <summary>A battle between two of one nation's own armies is a caller bug, not an outcome.</summary>
    [Fact]
    public void TwoArmiesOfOneNationCannotFightEachOther()
    {
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Attacker, "north", 1, 2, 60, 0, 0, BattleTestbed.Unit("light_infantry", 1000, 6, "A")),
                BattleTestbed.Army("north-other", "north", 2, 2, 60, 0, 0, BattleTestbed.Unit("light_infantry", 1000, 6, "B")),
            });

        Assert.Throws<ArgumentException>(() => InstantBattleResolver.ResolveField(
            state, Attacker, "north-other", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance));
    }

    /// <summary>The lopsided fixture every Done-when line above is measured against.</summary>
    internal static GameState Fixture()
    {
        var initial = BattleTestbed.Initial();

        return BattleTestbed.StateWith(
            armies: new[]
            {
                // 20×12000/100 + 100×4000/100 + 40×2000/100 = 7,200; 7,200/80 = 90; 90 × 68 = 6,120.
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 68, 256, 185,
                    BattleTestbed.Unit("light_infantry", 12000, 5, "1st Foot Battalion"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 6, "1st Guards Battalion"),
                    BattleTestbed.Unit("archers", 2000, 9, "1st Bowmen Battalion")),

                // 20×9000/100 + 100×3000/100 = 4,800; 4,800/80 = 60; 60 × 60 = 3,600.
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 60, 90, 40,
                    BattleTestbed.Unit("light_infantry", 9000, 6, "2nd Foot Battalion"),
                    BattleTestbed.Unit("heavy_infantry", 3000, 6, "2nd Guards Battalion")),
            },
            nations: new[]
            {
                BattleTestbed.NationWithUnity(initial, "north", 600),
                BattleTestbed.NationWithUnity(initial, "south", 560),
            });
    }

    /// <summary>
    /// The same battle, with the loser given enough unity and cities to reach the peace gate. T88: both
    /// sides are forced AI, so this exercises the unconditional-draw AI-vs-AI path — the toy scenario's
    /// own "north" seat is human, and the AI-vs-AI gate is unaffected by this task apart from the cascade
    /// fixes (DoD 3's own last bullet), so pinning it against an actual mixed battle would test the wrong
    /// path (see <see cref="HumanConsentFixture"/> for the human-involved gate instead).
    /// </summary>
    private static GameState PeaceGateFixture(int loserUnity, int loserCityCount)
    {
        var initial = BattleTestbed.Initial();
        var baseState = Fixture();

        return baseState with
        {
            // Placed on the map's southern water row, well clear of the battle's own tiles.
            Cities = ValueList.From(BattleTestbed.FillerCities("south", loserCityCount, 0, 5)),
            Nations = ValueList.From(new[]
            {
                AsAi(BattleTestbed.NationWithUnity(initial, "north", 600)),
                BattleTestbed.NationWithUnity(initial, "south", loserUnity),
            }),
        };
    }

    /// <summary>T88: the toy scenario's own "north" seat is human; this forces it AI for a fixture that means to test the AI-vs-AI path specifically.</summary>
    private static NationState AsAi(NationState nation) =>
        nation with { Control = SeatControl.Ai, Personality = nation.Personality ?? new AiPersonality(0.5, 0.5, 0.5) };

    private static BattleResolution Resolve(GameState state, Ruleset ruleset, IEventSink? events = null) =>
        InstantBattleResolver.ResolveField(
            state,
            Attacker,
            Defender,
            ruleset,
            BattleTestbed.World,
            BattleTestbed.BattleRng(),
            events ?? NullEventSink.Instance);

    /// <summary>B7 (T63 review round 1): the delete-class round-trip DoD 1 names is through T20's <see cref="SaveManager"/>, not just <see cref="GameDataLoader"/>/<see cref="GameDataValidation"/>.</summary>
    private static void AssertRoundTripsThroughSaveManager(GameState state)
    {
        var save = new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "probe-save",
            Label: "T63 delete-class probe",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        var json = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("probe-save.json", json, BattleTestbed.World, BattleTestbed.Destroyed);

        Assert.Equal(json, SaveManager.Serialize(reloaded));
    }
}
