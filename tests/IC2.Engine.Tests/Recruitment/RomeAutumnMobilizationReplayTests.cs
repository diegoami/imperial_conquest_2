using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T55 Done-when 8: <c>1_rome_270_autumn_1.sav → 1_rome_270_autumn_3.sav</c>, the one controlled
/// mobilization in the corpus, reproduced end to end.
/// <c>decompiled-mobilization-and-mercenary-restock.md</c> §7 re-parses both army tables byte for byte
/// and checks every clause of §§1–5 against them; this replays the same click through the engine and
/// asserts the same figures.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The recorded pair, verbatim from §7:</strong>
/// </para>
/// <code>
/// autumn_1  army 0 @ (100,42)  13 units  48,173 troops
/// autumn_3  army 0 @ (100,42)  20 units  83,173 troops
///               + slot 13..17  5 x archers 3,500   quality 6   "1st".."5th Bowmen Battalion"
///               + slot 18      light inf 15,000    quality 6   "3rd Foot Battalion"
///               + slot 19      heavy cav 2,500     quality 6   "3rd Dragoons Battalion"
/// autumn_3  army 14 @ (102,44)  4 units  43,000 troops  moves 8  covered 2  money 0  morale 60
///               slot 0  light cav  7,000  quality 6  "3rd Lancers Battalion"
///               slot 1  heavy inf  6,000  quality 6  "9th Guards Battalion"
///               slot 2  light inf 15,000  quality 6  "4th Foot Battalion"
///               slot 3  light inf 15,000  quality 6  "5th Foot Battalion"
/// </code>
/// <para>
/// <strong>What the fixture transcribes and what it infers.</strong> Transcribed: Rome's coordinates
/// <c>(101, 43)</c>, army 0's position and unit count, both troop totals, the twelve slots' types and
/// sizes, the eleven at state 24 and the twelfth at state 8, and Rome's 62 % mobilization rate.
/// Inferred, and marked here because a reviewer should not have to guess: (a) the report gives army 0's
/// <em>total</em> of 48,173 troops but not its per-unit split, so the thirteen units share it evenly
/// (twelve of 3,705 and one of 3,713) — nothing in the replay depends on the split, only on the total;
/// (b) the report says the naming scan is <strong>nation-wide</strong> and lists army 0's roster as
/// holding <c>2nd Lancers</c> with no <c>1st</c>, yet the mobilized light cavalry came out
/// <c>3rd Lancers</c> — so a <c>1st Lancers Battalion</c> stood in another of Rome's fifteen armies,
/// which is exactly what a nation-wide scan is for. The fixture parks one such army well out of range,
/// and <see cref="A_per_army_naming_scan_would_get_the_lancers_ordinal_wrong"/> shows that removing it
/// changes the answer — which is what makes "nation-wide" a checked claim here rather than a repeated
/// one.
/// </para>
/// </remarks>
public sealed class RomeAutumnMobilizationReplayTests
{
    private const string Rome = "north";          // the toy scenario's human seat, standing in for Rome
    private const string RomeCityId = "rome";
    private const int RomeX = 101;
    private const int RomeY = 43;

    private static Ruleset Ruleset => RecruitmentTestbed.Ruleset;

    private static readonly World World = MobilizationFixture.OpenWorld(110, 50);

    /// <summary>Army 0's thirteen units in autumn_1, totalling the recorded 48,173 troops.</summary>
    private static UnitSlot[] ArmyZeroRoster()
    {
        var units = new List<UnitSlot>
        {
            MobilizationFixture.Unit("1st Foot Battalion", "light_infantry", 3_705),
            MobilizationFixture.Unit("2nd Foot Battalion", "light_infantry", 3_705),
        };

        for (var i = 1; i <= 8; i++)
        {
            units.Add(MobilizationFixture.Unit($"{Ordinal(i)} Guards Battalion", "heavy_infantry", 3_705));
        }

        units.Add(MobilizationFixture.Unit("1st Dragoons Battalion", "heavy_cavalry", 3_705));
        units.Add(MobilizationFixture.Unit("2nd Dragoons Battalion", "heavy_cavalry", 3_705));
        units.Add(MobilizationFixture.Unit("2nd Lancers Battalion", "light_cavalry", 3_713));

        return units.ToArray();
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th",
    };

    /// <summary>
    /// Rome's recruitment queue in autumn_1, ordered so that the descending walk the original's dialog
    /// makes produces the landing order the save records.
    /// </summary>
    private static RecruitmentSlot[] RomeSlots() => new[]
    {
        new RecruitmentSlot(RomeCityId, "light_infantry", 15_000, 24),   // 0  -> 5th Foot
        new RecruitmentSlot(RomeCityId, "light_infantry", 15_000, 24),   // 1  -> 4th Foot
        new RecruitmentSlot(RomeCityId, "heavy_infantry", 6_000, 24),    // 2  -> 9th Guards
        new RecruitmentSlot(RomeCityId, "light_cavalry", 7_000, 24),     // 3  -> 3rd Lancers
        new RecruitmentSlot(RomeCityId, "heavy_cavalry", 2_500, 24),     // 4  -> 3rd Dragoons
        new RecruitmentSlot(RomeCityId, "light_infantry", 15_000, 24),   // 5  -> 3rd Foot
        new RecruitmentSlot(RomeCityId, "archers", 3_500, 24),           // 6  -> 5th Bowmen
        new RecruitmentSlot(RomeCityId, "archers", 3_500, 24),           // 7  -> 4th Bowmen
        new RecruitmentSlot(RomeCityId, "archers", 3_500, 24),           // 8  -> 3rd Bowmen
        new RecruitmentSlot(RomeCityId, "archers", 3_500, 24),           // 9  -> 2nd Bowmen
        new RecruitmentSlot(RomeCityId, "archers", 3_500, 24),           // 10 -> 1st Bowmen
        new RecruitmentSlot(RomeCityId, "light_cavalry", 7_000, 8),      // 11  the twelfth: stays behind
    };

    private static GameState Autumn1(bool withTheOtherRomanArmy = true)
    {
        var state = RecruitmentTestbed.InitialState();

        var armyZero = MobilizationFixture.Army("army-0", Rome, RomeX - 1, RomeY - 1, ArmyZeroRoster());
        var armies = withTheOtherRomanArmy
            ? new[]
            {
                armyZero,
                // Another of Rome's fifteen armies, six tiles off: out of range for a human seat's
                // d == 1, so it never receives a unit -- but in range of the nation-wide naming scan.
                MobilizationFixture.Army(
                    "army-7", Rome, RomeX - 6, RomeY - 6,
                    new[] { MobilizationFixture.Unit("1st Lancers Battalion", "light_cavalry", 4_000) }),
            }
            : new[] { armyZero };

        state = state with
        {
            Cities = ValueList.Of(MobilizationFixture.City(RomeCityId, RomeX, RomeY, Rome)),
            Armies = ValueList.Of(armies),
        };

        state = MobilizationFixture.WithSlots(state, Rome, RomeSlots());
        return RecruitmentTestbed.WithNation(state, state.NationById(Rome)! with { MobilizedPercent = 62 });
    }

    /// <summary>
    /// The click: the eleven ready rows, mobilized in descending slot order — the only order under
    /// which the dialog's stale row-to-slot table stays correct, because deleting a slot shifts only
    /// the slots above it (§7).
    /// </summary>
    private static GameState MobilizeTheReadyEleven(GameState state, IEventSink? sink = null)
    {
        var dispatcher = MobilizationFixture.DispatcherOn(World, sink);

        for (var slotIndex = 10; slotIndex >= 0; slotIndex--)
        {
            var result = dispatcher.Dispatch(state, new MobilizeRecruitSlotCommand(Rome, slotIndex, "army-14"));
            Assert.True(result.IsAccepted, $"slot {slotIndex} was refused: {result.Rejection}");
            state = result.State;
        }

        return state;
    }

    /// <summary>
    /// The whole pair, clause by clause: eleven units at quality 6, army 0 filling to exactly 20 units
    /// and 83,173 troops, army 14 created at <c>(102, 44)</c> with 4 units and 43,000 troops, money 0,
    /// and the nation-wide naming series across both armies.
    /// </summary>
    [Fact]
    public void The_corpus_mobilization_reproduces_exactly()
    {
        var before = Autumn1();
        Assert.Equal(48_173, before.ArmyById("army-0")!.TotalTroops);
        Assert.Equal(13, before.ArmyById("army-0")!.Units.Count);

        var after = MobilizeTheReadyEleven(before);

        // --- army 0 -------------------------------------------------------------------------------
        var armyZero = after.ArmyById("army-0")!;
        Assert.Equal(RomeX - 1, armyZero.X);
        Assert.Equal(RomeY - 1, armyZero.Y);
        Assert.Equal(Ruleset.ArmyManagement.MaxUnitsPerArmy, armyZero.Units.Count);
        Assert.Equal(20, armyZero.Units.Count);
        Assert.Equal(83_173, armyZero.TotalTroops);
        Assert.Equal(35_000, armyZero.TotalTroops - 48_173);

        var arrivals = armyZero.Units.Skip(13).ToArray();
        Assert.Equal(
            new[]
            {
                "1st Bowmen Battalion", "2nd Bowmen Battalion", "3rd Bowmen Battalion",
                "4th Bowmen Battalion", "5th Bowmen Battalion", "3rd Foot Battalion",
                "3rd Dragoons Battalion",
            },
            arrivals.Select(u => u.Name).ToArray());
        Assert.Equal(
            new[] { 3_500, 3_500, 3_500, 3_500, 3_500, 15_000, 2_500 },
            arrivals.Select(u => u.Troops).ToArray());

        // --- army 14 ------------------------------------------------------------------------------
        var armyFourteen = after.ArmyById("army-14")!;
        Assert.Equal(RomeX + 1, armyFourteen.X);       // (102, 44) = Rome (101, 43) + (+1, +1)
        Assert.Equal(RomeY + 1, armyFourteen.Y);
        Assert.Equal(102, armyFourteen.X);
        Assert.Equal(44, armyFourteen.Y);
        Assert.Equal(4, armyFourteen.Units.Count);
        Assert.Equal(43_000, armyFourteen.TotalTroops);
        Assert.Equal(0, armyFourteen.Money);
        Assert.Equal(0, armyFourteen.SupplyTons);
        Assert.Equal(
            new[]
            {
                "3rd Lancers Battalion", "9th Guards Battalion",
                "4th Foot Battalion", "5th Foot Battalion",
            },
            armyFourteen.Units.Select(u => u.Name).ToArray());
        Assert.Equal(
            new[] { 7_000, 6_000, 15_000, 15_000 },
            armyFourteen.Units.Select(u => u.Troops).ToArray());

        // The placement cell's terrain is one an army may stand on -- the save read covered = 2.
        var covered = World.TileTypeByCode(armyFourteen.CoveredTileCode!.Value);
        Assert.True(covered!.PassableByArmies);

        // --- every mobilized unit ------------------------------------------------------------------
        var mobilized = arrivals.Concat(armyFourteen.Units).ToArray();
        Assert.Equal(11, mobilized.Length);
        Assert.All(mobilized, unit =>
        {
            Assert.Equal(6, unit.Quality);            // quality = state 24 / 4
            Assert.Equal(0, unit.MercenaryLabel);     // origin label 0 on all of them
        });
        Assert.Equal(78_000, mobilized.Sum(u => u.Troops));

        // --- what did not happen -------------------------------------------------------------------
        // The state-8 slot stayed behind, and it is the only slot left.
        var leftBehind = Assert.Single(after.NationById(Rome)!.RecruitmentSlots);
        Assert.Equal(8, leftBehind.StateCode);
        Assert.Equal("light_cavalry", leftBehind.UnitTypeId);
        Assert.Equal(7_000, leftBehind.Troops);

        // Rome's mobilization rate is untouched by mobilizing.
        Assert.Equal(62, after.NationById(Rome)!.MobilizedPercent);

        // The city is not a garrison being drained: it never held these units.
        Assert.Empty(after.CityById(RomeCityId)!.Garrison);

        // Two armies received, one army was created, and nothing else moved.
        Assert.Equal(3, after.Armies.Count);
        Assert.Equal(before.ArmyById("army-7"), after.ArmyById("army-7"));
    }

    /// <summary>
    /// The state-8 slot the gate excluded "stayed, and ticked to 10" — the same weekly tick that would
    /// have advanced it if it had not been mobilized, run through
    /// <see cref="RecruitmentSlotReadinessSystem"/>.
    /// </summary>
    [Fact]
    public void The_slot_left_behind_ticks_from_eight_to_ten()
    {
        var after = MobilizeTheReadyEleven(Autumn1());
        Assert.Equal(8, Assert.Single(after.NationById(Rome)!.RecruitmentSlots).StateCode);

        var ticked = RecruitmentTestbed
            .CoordinatorOnly(sink: null, typeof(RecruitmentSlotReadinessSystem))
            .RunRoundTick(after).State;

        Assert.Equal(10, Assert.Single(ticked.NationById(Rome)!.RecruitmentSlots).StateCode);
    }

    /// <summary>
    /// The save's <c>morale 60</c> and <c>moves 8</c> are not creation values: the army is created at
    /// morale 59 with 0 moves, and the weekly tick that ran between the two saves produced both
    /// figures. Both are checked through the engine's own merged T08 rule rather than restated —
    /// <c>morale 59 + 1</c> for an army above the supply dead band (army 14 held 791 tons on 43,000
    /// troops, far above it), and <c>10 − ⌊43,000 / 20,000⌋ = 8</c>.
    /// </summary>
    [Fact]
    public void Morale_sixty_and_moves_eight_are_the_weekly_tick_applied_to_a_created_army()
    {
        var after = MobilizeTheReadyEleven(Autumn1());
        var created = after.ArmyById("army-14")!;

        Assert.Equal(59, created.Morale);
        Assert.Equal(0, created.Moves);               // a human seat's new army cannot act this week

        var supplyRules = Ruleset.Economy.SupplyMorale;
        var (moraleAfterTick, movesPenalty) = SupplyMoraleRule.ApplyToMorale(
            created.Morale, supplyRules.DeadBandUpperPercent + 1, Ruleset);

        Assert.Equal(60, moraleAfterTick);
        Assert.Equal(0, movesPenalty);
        Assert.Equal(8, SupplyMoraleRule.BaseMoves(created.TotalTroops, Ruleset));
        Assert.Equal(8, SupplyMoraleRule.BaseMoves(43_000, Ruleset));
    }

    /// <summary>
    /// The naming scan is nation-wide, not per-army. With Rome's other army holding the
    /// <c>1st Lancers</c>, the mobilized light cavalry is the <c>3rd</c>, as the save records; take
    /// that army away and the free-ordinal scan answers <c>1st</c> instead. The difference is the
    /// whole content of "nation-wide".
    /// </summary>
    [Fact]
    public void A_per_army_naming_scan_would_get_the_lancers_ordinal_wrong()
    {
        var withOtherArmy = MobilizeTheReadyEleven(Autumn1());
        Assert.Equal("3rd Lancers Battalion", withOtherArmy.ArmyById("army-14")!.Units[0].Name);

        var withoutOtherArmy = MobilizeTheReadyEleven(Autumn1(withTheOtherRomanArmy: false));
        Assert.Equal("1st Lancers Battalion", withoutOtherArmy.ArmyById("army-14")!.Units[0].Name);

        // And the series that does not depend on the other army is unaffected either way.
        Assert.Equal("9th Guards Battalion", withoutOtherArmy.ArmyById("army-14")!.Units[1].Name);
    }

    /// <summary>
    /// The twelfth slot is refused for exactly the reason the gate gives, rather than being skipped by
    /// the fixture: mobilizing it directly is rejected while it reads state 8.
    /// </summary>
    [Fact]
    public void The_state_eight_slot_is_refused_when_it_is_mobilized_directly()
    {
        var before = Autumn1();

        var result = MobilizationFixture.DispatcherOn(World)
            .Dispatch(before, new MobilizeRecruitSlotCommand(Rome, 11, "army-14"));

        Assert.Equal(MobilizeRecruitSlotRejections.SlotNotReady, result.Code);
        Assert.Same(before, result.State);
    }
}
