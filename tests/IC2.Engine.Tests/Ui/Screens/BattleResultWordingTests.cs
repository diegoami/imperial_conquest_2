using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// bug #499 rework round 1 (PR #508 review): the battle-result screen's own wording for the three states
/// the first round's text got wrong, each driven by the real engine path that produces it rather than a
/// hand-built <see cref="BattleResult"/>:
/// <list type="bullet">
/// <item><description>
/// B1: a besieger that wins the strength comparison but is emptied by this same attempt's own casualties
/// (<c>T63 N7</c>, <c>InstantBattleResolver.ResolveSiege</c>) — the first round rendered
/// "The attack fell short by -100: 300 against 200."
/// </description></item>
/// <item><description>
/// N1: an exact tie — the first round rendered "The attack fell short by 0: X against X."
/// </description></item>
/// <item><description>
/// N2: a naval battle, where <c>LoserCasualties</c> counts the sunken fleet's hulls while
/// <c>WinnerCasualties</c> counts the winning fleet's carried army's troops — the first round printed the
/// hulls as "casualties".
/// </description></item>
/// </list>
/// </summary>
public sealed class BattleResultWordingTests
{
    /// <summary>
    /// B1: the T63 N7 state — <c>attack 300 &gt; defence 200</c>, but the besieger's own 8 lost troops
    /// empty its only unit, so the engine reports the defender as the winner. The screen's shortfall line
    /// must never go negative: on the reviewed head it read
    /// "The attack fell short by -100: 300 against 200."
    /// </summary>
    /// <remarks>
    /// The fixture is <c>SiegeAttritionTests.EmptiedBesieger_ReportsTheDefenderAsWinner_NotACaptureTheCityDidNotYield</c>'s
    /// own, replayed here through the same real <see cref="InstantBattleResolver.ResolveSiege"/> so the
    /// view model's text is asserted against the engine's actual result, never a hand-built one.
    /// </remarks>
    [Fact]
    public void AnEmptyingBesiegerOnAStrongerAttackShowsNoNegativeShortfall()
    {
        var siege = ResolveEmptiedBesieger();

        // The N7 state itself, from the real resolver: the attacker won on strength but this attempt's
        // own attrition emptied it, so the defender is reported the winner.
        Assert.Equal(300, siege.Result.AttackerPower);
        Assert.Equal(200, siege.Result.DefenderPower);
        Assert.Equal(BattleSide.Defender, siege.Result.Winner);
        Assert.False(siege.Result.AttackerWon);
        Assert.Null(siege.State.ArmyById("army"));

        var viewModel = BattleResultViewModel.FromResult(siege.Result);
        var shortfall = viewModel.FailedSiegeShortfallText();

        // B1: no negative figure anywhere in the line -- on the reviewed head this was "-100".
        Assert.DoesNotContain("-", shortfall ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(shortfall);
    }

    /// <summary>
    /// N1: an exact tie. The confirmed rule is "the attacker wins only if <c>atk &gt; def</c>, ties to the
    /// defender", so the line must say the tie goes to the defender, not "fell short by 0".
    /// The 4,000-against-4,000 pair comes from the real resolver: the city's loyalty 20 × 150 plus its
    /// population 5k × 200 against the army's 3,200 troops / 80 × morale 100.
    /// </summary>
    [Fact]
    public void AnExactTieSaysTheTieGoesToTheDefender()
    {
        var siege = ResolveExactTie();

        Assert.Equal(4_000, siege.Result.AttackerPower);
        Assert.Equal(4_000, siege.Result.DefenderPower);
        Assert.Equal(BattleSide.Defender, siege.Result.Winner);
        Assert.NotNull(siege.State.ArmyById("army")); // an ordinary tie, not the emptied-besieger case

        var viewModel = BattleResultViewModel.FromResult(siege.Result);
        Assert.Equal(
            "The attack fell short: a tie goes to the defender (4000 against 4000).",
            viewModel.FailedSiegeShortfallText());
    }

    /// <summary>
    /// N2: a real naval battle. The winning fleet's own figure is its carried army's troops while the
    /// loser's is the sunken fleet's hulls, so the loser line has to say "ships lost" rather than
    /// "casualties" — otherwise "South's casualties: 10" reads as ten soldiers.
    /// </summary>
    [Fact]
    public void ANavalBattleWordsTheLosersLossesAsShips()
    {
        var resolution = ResolveNavalBattle();
        var result = resolution.Result;

        Assert.Equal(BattleKind.Naval, result.Kind);
        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.Null(resolution.State.FleetById("south-fleet"));

        // Two different units in one result: the winner's figure is its carried army's troops (the
        // per-slot report), the loser's is the whole ten-hull fleet.
        Assert.True(result.WinnerCasualties > 0);
        Assert.Equal(result.UnitCasualties.Sum(c => c.TroopsLost), result.WinnerCasualties);
        Assert.Equal(10, result.LoserCasualties);

        var viewModel = BattleResultViewModel.FromResult(result);
        var winnerName = resolution.State.NationById(result.WinnerNationId)!.Name;
        var loserName = resolution.State.NationById(result.LoserNationId)!.Name;

        Assert.Equal(
            $"{winnerName}'s casualties: {result.WinnerCasualties}",
            viewModel.WinnerCasualtiesText(winnerName));
        Assert.Equal($"{loserName}'s ships lost: 10", viewModel.LoserCasualtiesText(loserName));
    }

    /// <summary>
    /// The engine test's own fixture and RNG: a low-loyalty, low-population city against one 255-troop
    /// <c>heavy_cavalry</c> unit at morale 100 (300 attack against 200 defence), with the casualty divisor
    /// pinned to its minimum so the 8-troop loss that empties the unit is exact.
    /// </summary>
    private static BattleResolution ResolveEmptiedBesieger()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "Emptied", 0, 0, "defender", "defender", loyalty: 0, fortificationCode: 0,
            populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 100, CaptureTestbed.Unit("heavy_cavalry", 255));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        return InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, new FixedDivisorRng(), CaptureTestbed.ArcherUnitTypeId,
            CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
    }

    /// <summary>
    /// A designed exact tie (4,000 against 4,000), resolved through the real
    /// <see cref="InstantBattleResolver.ResolveSiege"/>: the city's loyalty 20 and population 5k give
    /// 20 × 150 + 5 × 200 = 4,000, the one 3,200-troop <c>heavy_infantry</c> unit at morale 100 gives
    /// 3,200 / 80 × 100 = 4,000. Owner and allegiance match, so neither scaling branch fires, and loyalty
    /// 20 is below the 59 threshold.
    /// </summary>
    private static BattleResolution ResolveExactTie()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "Tied", 0, 0, "defender", "defender", loyalty: 20, fortificationCode: 0,
            populationThousands: 5, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 100, CaptureTestbed.Unit("heavy_infantry", 3_200));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        return InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, new FixedDivisorRng(), CaptureTestbed.ArcherUnitTypeId,
            CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
    }

    /// <summary>
    /// A real naval battle: a 20-ship, full-condition fleet carrying an army against a 10-ship fleet,
    /// both under the toy scenario's own world and ruleset. The attacker wins, so the loser's ten hulls
    /// are the whole <c>LoserCasualties</c>, while the winner's figure is its carried army's troop losses.
    /// </summary>
    private static BattleResolution ResolveNavalBattle()
    {
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.EmbarkedArmy(
                    "north-cargo", "north", "north-fleet", 0, 2, 60,
                    BattleTestbed.Unit("light_infantry", 1_000, 6, "Marine Foot")),
                BattleTestbed.EmbarkedArmy(
                    "south-cargo", "south", "south-fleet", 0, 3, 60,
                    BattleTestbed.Unit("light_infantry", 1_000, 6, "Marine Foot")),
            },
            fleets: new[]
            {
                BattleTestbed.Fleet("north-fleet", "north", 0, 2, 20, 100, "north-cargo"),
                BattleTestbed.Fleet("south-fleet", "south", 0, 3, 10, 80, "south-cargo"),
            });

        return InstantBattleResolver.ResolveNaval(
            state, "north-fleet", "south-fleet", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), "archers", NullEventSink.Instance);
    }

    /// <summary>
    /// An <see cref="IRng"/> that always draws the minimum, so <c>BattleCasualties</c>'s divisor lands on
    /// <c>CasualtyDivisorBase</c> (105 in the shipped rulesets) and the attrition is an exact number.
    /// </summary>
    private sealed class FixedDivisorRng : IRng
    {
        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

        public int NextInt(int exclusiveUpperBound) => 0;

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => inclusiveLowerBound;

        public bool NextChance(int numerator, int denominator) => false;

        public IRng ForStream(string streamName) => this;
    }
}
