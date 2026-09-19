using IC2.Engine.Armies;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T55 Done-when 8: <c>1_rome_270_autumn_1.sav → 1_rome_270_autumn_3.sav</c>, the one controlled
/// mobilization in the corpus, reproduced end to end.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every figure below is read out of the two saves</strong>, not transcribed from a summary.
/// The parse is <c>decompiled-mobilization-and-mercenary-restock.md</c> §7's own reproduction snippet
/// (army table at <c>0x18A5C</c>, 656-byte records, 32-byte unit slots at <c>+16</c>), run against the
/// local corpus. Nothing here is inferred: the thirteen starting units' individual types, troop counts,
/// qualities, names and <em>array order</em> are the save's, as is Rome owning
/// <strong>exactly one army</strong>.
/// </para>
/// <code>
/// autumn_1  14 armies in the table; Rome (nation 0) owns exactly one, army 0.
///           army 0 @ (100,42)  13 units  48,173 troops  moves 8  covered 9  supply 410  money 296  morale 70
/// autumn_3  15 armies; Rome owns two.
///           army 0 @ (100,42)  20 units  83,173 troops  moves 6  supply 791  money 296  morale 70
///               + slot 13..17  5 x archers 3,500   quality 6   "1st".."5th Bowmen Battalion"
///               + slot 18      light inf 15,000    quality 6   "3rd Foot Battalion"
///               + slot 19      heavy cav 2,500     quality 6   "3rd Dragoons Battalion"
///           army 14 @ (102,44)  4 units  43,000 troops  moves 8  covered 2  supply 410  money 0  morale 60
///               slot 0  light cav  7,000  quality 6  "3rd Lancers Battalion"
///               slot 1  heavy inf  6,000  quality 6  "9th Guards Battalion"
///               slot 2  light inf 15,000  quality 6  "4th Foot Battalion"
///               slot 3  light inf 15,000  quality 6  "5th Foot Battalion"
/// </code>
/// <para>
/// <strong>"Army 14" is not a Roman ordinal.</strong> It is <c>DAT_004a0324++</c>, the index of the
/// next free row in the <em>global</em> army table, which held 14 rows across nine nations before the
/// mobilization. An earlier revision of this fixture read the report's "fifteen armies" as Rome's own
/// and invented a second Roman army to explain the <c>3rd Lancers</c> name. That was contrary to the
/// save, and it concealed a real defect — see
/// <see cref="The_lancers_ordinal_refutes_T15s_naming_rule_and_fails_until_it_is_fixed"/>.
/// </para>
/// <para>
/// <strong>The names are verbatim too, double spaces and all.</strong> The saves store several of the
/// thirteen starting names with a <em>double</em> space before "Battalion"
/// (<c>"2nd Lancers  Battalion"</c>, <c>"1st Dragoons  Battalion"</c>, <c>"1st Foot  Battalion"</c>, …)
/// while all eleven newly created units use a single one. Nothing is normalised here: this fixture runs
/// against exactly what the save holds, which is what makes it the corpus-level proof of
/// <a href="https://github.com/diegoami/imperial_conquest_2/issues/243">#243</a>'s second defect —
/// <see cref="ArmyNaming"/> used to require exactly one space, so it saw neither Dragoons ordinal and
/// would have named the mobilized heavy cavalry <c>1st</c> instead of <c>3rd</c>.
/// </para>
/// <para>
/// <strong>This pair refuted <see cref="ArmyNaming"/>'s ordinal rule, and #243 is the fix.</strong>
/// Rome owns one army and one light-cavalry unit in the whole game, <c>2nd Lancers</c>, with no
/// <c>1st</c> anywhere; the original names the mobilized one <c>3rd Lancers Battalion</c>.
/// <see cref="ArmyNaming"/> shipped the <em>smallest unused</em> ordinal — explicitly
/// <c>[derived]</c>, and flagging this exact ambiguity — which gives <c>1st</c>. Every other name in
/// the pair is a dense series where both readings agree, so that lone gap is the sole discriminating
/// case in the corpus, and it settles the rule as <strong>one past the highest ordinal in use</strong>.
/// The <c>3rd Lancers</c> assertion below is that refutation; it is now simply part of the replay.
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

    /// <summary>
    /// Army 0's thirteen units in <c>autumn_1</c>: type, troops, quality, name and array order exactly
    /// as the save stores them, <strong>including the double spaces</strong> nine of the thirteen names
    /// carry. They sum to the recorded 48,173 troops.
    /// </summary>
    private static UnitSlot[] ArmyZeroRoster() => new[]
    {
        MobilizationFixture.Unit("1st Foot  Battalion", "light_infantry", 4_210, 6),
        MobilizationFixture.Unit("1st Guards  Battalion", "heavy_infantry", 4_900, 8),
        MobilizationFixture.Unit("2nd Guards  Battalion", "heavy_infantry", 4_920, 7),
        MobilizationFixture.Unit("3rd Guards  Battalion", "heavy_infantry", 5_747, 7),
        MobilizationFixture.Unit("1st Dragoons  Battalion", "heavy_cavalry", 774, 7),
        MobilizationFixture.Unit("7th Guards Battalion", "heavy_infantry", 2_583, 7),
        MobilizationFixture.Unit("2nd Dragoons  Battalion", "heavy_cavalry", 1_539, 8),
        MobilizationFixture.Unit("2nd Lancers  Battalion", "light_cavalry", 900, 7),
        MobilizationFixture.Unit("6th Guards  Battalion", "heavy_infantry", 4_787, 7),
        MobilizationFixture.Unit("5th Guards  Battalion", "heavy_infantry", 3_571, 7),
        MobilizationFixture.Unit("4th Guards  Battalion", "heavy_infantry", 5_300, 9),
        MobilizationFixture.Unit("8th Guards Battalion", "heavy_infantry", 3_442, 6),
        MobilizationFixture.Unit("2nd Foot Battalion", "light_infantry", 5_500, 6),
    };

    /// <summary>
    /// Rome's recruitment queue in <c>autumn_1</c>, ordered so that the descending walk the original's
    /// dialog makes produces the landing order <c>autumn_3</c> records.
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

    /// <summary>
    /// <c>autumn_1</c>: Rome, its one army with the save's own stats, and the twelve recruitment slots.
    /// </summary>
    private static GameState Autumn1()
    {
        var state = RecruitmentTestbed.InitialState();

        // moves 8, morale 70, money 296 and 410 tons are army 0's recorded autumn_1 values. Its
        // covered cell reads 9 (a river tile) in the save; the fixture world is uniform plain, so the
        // covered code is the fixture's own -- nothing in the mobilization path reads it.
        var armyZero = MobilizationFixture.Army(
            "army-0", Rome, RomeX - 1, RomeY - 1, ArmyZeroRoster(),
            moves: 8, morale: 70, money: 296, supplyTons: 410);

        state = state with
        {
            Cities = ValueList.Of(MobilizationFixture.City(RomeCityId, RomeX, RomeY, Rome)),
            Armies = ValueList.Of(armyZero),
        };

        state = MobilizationFixture.WithSlots(state, Rome, RomeSlots());
        return RecruitmentTestbed.WithNation(state, state.NationById(Rome)! with { MobilizedPercent = 62 });
    }

    /// <summary>Every regular unit of one type that Rome owns anywhere, in scan order.</summary>
    private static string[] RomanUnitsOfType(GameState state, string unitTypeId) =>
        state.Armies
            .Where(a => string.Equals(a.Nation, Rome, StringComparison.Ordinal))
            .SelectMany(a => a.Units)
            .Where(u => string.Equals(u.UnitTypeId, unitTypeId, StringComparison.Ordinal) && u.IsRegular)
            .Select(u => u.Name)
            .ToArray();

    /// <summary>
    /// The click: the eleven ready rows, mobilized in descending slot order — the only order under
    /// which the dialog's stale row-to-slot table stays correct, because deleting a slot shifts only
    /// the slots above it (§7).
    /// </summary>
    private static GameState MobilizeTheReadyEleven(GameState state)
    {
        var dispatcher = MobilizationFixture.DispatcherOn(World);

        for (var slotIndex = 10; slotIndex >= 0; slotIndex--)
        {
            var result = dispatcher.Dispatch(state, new MobilizeRecruitSlotCommand(Rome, slotIndex, "army-14"));
            Assert.True(result.IsAccepted, $"slot {slotIndex} was refused: {result.Rejection}");
            state = result.State;
        }

        return state;
    }

    /// <summary>
    /// The pair, clause by clause: eleven units at quality 6, army 0 filling to exactly 20 units and
    /// 83,173 troops, army 14 created at <c>(102, 44)</c> with 4 units and 43,000 troops, and all
    /// eleven battalion names — <c>3rd Lancers</c> and <c>3rd Dragoons</c> among them, which together
    /// pin both halves of #243.
    /// </summary>
    [Fact]
    public void The_corpus_mobilization_reproduces_exactly()
    {
        var before = Autumn1();
        Assert.Equal(48_173, before.ArmyById("army-0")!.TotalTroops);
        Assert.Equal(13, before.ArmyById("army-0")!.Units.Count);
        Assert.Single(before.Armies);                   // Rome owns exactly one army in autumn_1

        // The two premises the naming clauses turn on, asserted rather than asserted about: Rome's
        // only light cavalry anywhere is the 2nd (so 1st is free, and is still not reused), and both
        // its heavy cavalry are stored in the double-spaced form.
        Assert.Equal(
            new[] { "2nd Lancers  Battalion" },
            RomanUnitsOfType(before, "light_cavalry"));
        Assert.Equal(
            new[] { "1st Dragoons  Battalion", "2nd Dragoons  Battalion" },
            RomanUnitsOfType(before, "heavy_cavalry"));

        var after = MobilizeTheReadyEleven(before);

        // --- army 0 -------------------------------------------------------------------------------
        var armyZero = after.ArmyById("army-0")!;
        Assert.Equal(RomeX - 1, armyZero.X);
        Assert.Equal(RomeY - 1, armyZero.Y);
        Assert.Equal(Ruleset.ArmyManagement.MaxUnitsPerArmy, armyZero.Units.Count);
        Assert.Equal(20, armyZero.Units.Count);
        Assert.Equal(83_173, armyZero.TotalTroops);
        Assert.Equal(35_000, armyZero.TotalTroops - 48_173);

        // The receiving army's own record is otherwise untouched: money, supply and morale are still
        // the autumn_1 values, because mobilizing writes a unit slot and nothing else.
        Assert.Equal(296, armyZero.Money);
        Assert.Equal(410, armyZero.SupplyTons);
        Assert.Equal(70, armyZero.Morale);

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
        Assert.Equal(0, armyFourteen.SupplyTons);      // the save's 410 tons is a later resupply
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
        Assert.Equal("light_cavalry", armyFourteen.Units[0].UnitTypeId);

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

        // One army became two, and no third appeared.
        Assert.Equal(2, after.Armies.Count);
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
    /// <c>morale 59 + 1</c> for an army above the supply dead band (army 14 read 410 tons on 43,000
    /// troops in <c>autumn_3</c>, far above it), and <c>10 − ⌊43,000 / 20,000⌋ = 8</c>.
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
