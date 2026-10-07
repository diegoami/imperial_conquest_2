using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/tasks/T76.md</c> Done-when 3: a computer nation's turn hires every qualifying offer exactly as
/// <c>FUN_0044E41C</c> states <strong>[confirmed:
/// decompiled-mercenary-offer-list-and-position.md §3]</strong>. Every gate gets its own boundary test,
/// because removing any one of them must fail the suite: distance (4 hires, 5 does not), "at war with
/// someone", the money threshold (50 no, 51 yes), not at war with the city's owner, and a free unit slot.
/// One more test shows the hire charges nothing and ignores the 100,000-troop cap.
/// </summary>
/// <remarks>
/// The radius and the money threshold are read from <see cref="Ruleset.Recruitment"/> and re-asserted
/// here, never written as a literal 4 or 50 in the assertions that matter.
/// </remarks>
public sealed class AiMercenaryHirePassTests
{
    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    private const string ArmyNation = "north";

    private static MercenaryPoolSlot Offer(int slotIndex, int x, int y, int troops = 100) =>
        new(SlotIndex: slotIndex, X: x, Y: y, NameLabel: 11, UnitTypeId: "light_infantry", Troops: troops, Quality: 6);

    /// <summary>
    /// A north AI army at (10,10), a south-owned offer city at the caller's tile, and a third nation
    /// ("east") so that "at war with someone" can be true while the offer city's owner is at peace. The
    /// default is north at war with east only.
    /// </summary>
    private static GameState StateWithOffer(
        int cityX,
        int cityY,
        string cityOwner = "south",
        int armyMoney = 1000,
        int unitCount = 1,
        bool northEastWar = true,
        params MercenaryPoolSlot[] pool)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation("north", AiScriptedStates.DefaultPersonality, treasury: 5000),
            AiScriptedStates.AiNation("south", AiScriptedStates.DefaultPersonality, treasury: 5000),
            AiScriptedStates.AiNation("east", AiScriptedStates.DefaultPersonality, treasury: 5000),
        };

        var cities = new[]
        {
            OfferCity(cityX, cityY, cityOwner),
            CaptureTestbed.City("north-home", "North Home", 0, 0, "north", "north", 80, 0, 100, 200, 10),
            CaptureTestbed.City("south-home", "South Home", 30, 30, "south", "south", 80, 0, 100, 200, 10),
            CaptureTestbed.City("east-home", "East Home", 31, 31, "east", "east", 80, 0, 100, 200, 10),
        };

        var units = Enumerable.Range(0, unitCount)
            .Select(i => CaptureTestbed.Unit("light_infantry", 100))
            .ToArray();
        var army = CaptureTestbed.Army("ai-army", ArmyNation, 10, 10, morale: 60, units)
            with { Money = armyMoney };

        var state = BattleCommandTestbed.StateWith(nations, cities, new[] { army });
        state = state with { MercenaryPool = ValueList.Of(pool) };
        if (northEastWar)
        {
            state = state with
            {
                Relations = state.Relations.WithRelation("north", "east", Ruleset.Diplomacy.StateCodes.War),
            };
        }

        return AiScriptedStates.WithActiveSeat(state, ArmyNation);
    }

    /// <summary>
    /// The two-nation variant used only for a full driven turn and its determinism: the toy world holds
    /// exactly north and south, and the offer city is north's own, so the owner gate passes while north
    /// is at war with south.
    /// </summary>
    private static GameState TwoNationStateWithOffer(
        int cityX,
        int cityY,
        int armyMoney = 1000,
        int unitCount = 1,
        params MercenaryPoolSlot[] pool)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation("north", AiScriptedStates.DefaultPersonality, treasury: 5000),
            AiScriptedStates.AiNation("south", AiScriptedStates.DefaultPersonality, treasury: 5000),
        };

        var cities = new[]
        {
            OfferCity(cityX, cityY, "north"),
            CaptureTestbed.City("north-home", "North Home", 0, 0, "north", "north", 80, 0, 100, 200, 10),
            CaptureTestbed.City("south-home", "South Home", 30, 30, "south", "south", 80, 0, 100, 200, 10),
        };

        var units = Enumerable.Range(0, unitCount)
            .Select(i => CaptureTestbed.Unit("light_infantry", 100))
            .ToArray();
        var army = CaptureTestbed.Army("ai-army", ArmyNation, 10, 10, morale: 60, units)
            with { Money = armyMoney };

        var state = BattleCommandTestbed.StateWith(nations, cities, new[] { army });
        state = state with
        {
            MercenaryPool = ValueList.Of(pool),
            Relations = state.Relations.WithRelation(
                "north", "south", Ruleset.Diplomacy.StateCodes.War),
        };

        return AiScriptedStates.WithActiveSeat(state, ArmyNation);
    }

    private static CityState OfferCity(int x, int y, string owner) =>
        CaptureTestbed.City("offer-city", "Offer City", x, y, owner, owner, 80, 0, 100, 200, 10);

    [Fact]
    public void A_computer_army_hires_every_offer_on_a_city_within_radius()
    {
        var state = StateWithOffer(14, 12, pool: new[] { Offer(5, 14, 12), Offer(9, 14, 12) });
        var beforeUnits = state.ArmyById("ai-army")!.Units.Count;

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(2, result.OffersHired);
        Assert.Equal(1, result.ArmiesHired);
        Assert.Empty(result.State.MercenaryPool);
        Assert.Equal(beforeUnits + 2, result.State.ArmyById("ai-army")!.Units.Count);
    }

    [Fact]
    public void The_ai_radius_is_four_so_four_hires_and_five_does_not()
    {
        Assert.Equal(4, Ruleset.Recruitment.MercenaryHireRangeAiSeat);

        var atFour = StateWithOffer(14, 12, pool: new[] { Offer(5, 14, 12) });
        Assert.Equal(1, AiMercenaryHirePass.Run(atFour, Ruleset, ArmyNation).OffersHired);

        var atFive = StateWithOffer(15, 12, pool: new[] { Offer(5, 15, 12) });
        var five = AiMercenaryHirePass.Run(atFive, Ruleset, ArmyNation);
        Assert.Equal(0, five.OffersHired);
        Assert.True(AiSubstantiveState.AreEquivalent(atFive, five.State));
    }

    [Fact]
    public void A_nation_not_at_war_with_anyone_hires_nothing()
    {
        var state = StateWithOffer(14, 12, northEastWar: false, pool: new[] { Offer(5, 14, 12) });

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(0, result.OffersHired);
        Assert.True(AiSubstantiveState.AreEquivalent(state, result.State));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(51)]
    public void The_money_gate_is_more_than_fifty(int armyMoney)
    {
        Assert.Equal(50, Ruleset.Recruitment.MercenaryAiHireMinMoney);

        var state = StateWithOffer(14, 12, armyMoney: armyMoney, pool: new[] { Offer(5, 14, 12) });

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        // 50 is not "more than 50"; anything above it is.
        Assert.Equal(armyMoney > 50 ? 1 : 0, result.OffersHired);
    }

    [Fact]
    public void An_offer_on_a_city_the_nation_is_at_war_with_is_not_hired()
    {
        var state = StateWithOffer(14, 12, cityOwner: "east", pool: new[] { Offer(5, 14, 12) });

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(0, result.OffersHired);
        Assert.True(AiSubstantiveState.AreEquivalent(state, result.State));
    }

    [Fact]
    public void An_army_with_twenty_units_has_no_free_slot_and_hires_nothing()
    {
        var state = StateWithOffer(14, 12, unitCount: 20, pool: new[] { Offer(5, 14, 12) });

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(0, result.OffersHired);
    }

    /// <summary>
    /// The original computes the first free slot once per army and requires <c>lastPlus1 &lt; 19</c>, so
    /// an 18-unit army can fill both remaining slots while a 19-unit army hires nothing
    /// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §3]</strong>.
    /// </summary>
    [Fact]
    public void An_eighteen_unit_army_fills_to_twenty_but_a_nineteen_unit_army_hires_nothing()
    {
        var atEighteen = StateWithOffer(
            14, 12, unitCount: 18,
            pool: new[] { Offer(5, 14, 12), Offer(6, 14, 12), Offer(7, 14, 12) });
        var result = AiMercenaryHirePass.Run(atEighteen, Ruleset, ArmyNation);
        Assert.Equal(2, result.OffersHired);
        Assert.Equal(Ruleset.ArmyManagement.MaxUnitsPerArmy, result.State.ArmyById("ai-army")!.Units.Count);

        var atNineteen = StateWithOffer(14, 12, unitCount: 19, pool: new[] { Offer(5, 14, 12) });
        Assert.Equal(0, AiMercenaryHirePass.Run(atNineteen, Ruleset, ArmyNation).OffersHired);
    }

    /// <summary>
    /// N2: as the player's <c>FindChosenCity</c> guards, the AI hires a city's offers in slot-index order,
    /// not in <see cref="GameState.MercenaryPool"/> list order. With an 18-unit army (room for exactly two)
    /// and an unsorted pool of slots 7, 5 and 6, slot order leaves slot 7 behind; list order would leave
    /// slot 6.
    /// </summary>
    [Fact]
    public void The_ai_hires_a_citys_offers_in_slot_index_order()
    {
        var state = StateWithOffer(
            14, 12, unitCount: 18,
            pool: new[] { Offer(7, 14, 12), Offer(5, 14, 12), Offer(6, 14, 12) });

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(2, result.OffersHired);
        Assert.Equal(7, Assert.Single(result.State.MercenaryPool).SlotIndex);
    }

    [Fact]
    public void The_ai_hire_charges_nothing_and_ignores_the_100k_troop_cap()
    {
        const int offerTroops = 5000;
        var state = StateWithOffer(
            14, 12, armyMoney: 1000, unitCount: 1,
            pool: new[] { Offer(5, 14, 12, offerTroops) });

        // Replace the single unit with one already at the player cap minus one, so the hire crosses it.
        var army = state.ArmyById("ai-army")!;
        var huge = army with
        {
            Units = ValueList.Of(CaptureTestbed.Unit(
                "light_infantry", Ruleset.ArmyManagement.MaxTroopsPerArmy - 1)),
        };
        state = state with { Armies = ValueList.Of(huge) };
        Assert.True(huge.TotalTroops + offerTroops > Ruleset.ArmyManagement.MaxTroopsPerArmy);

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(1, result.OffersHired);
        var after = result.State.ArmyById("ai-army")!;
        Assert.Equal(1000, after.Money);
        Assert.True(after.TotalTroops > Ruleset.ArmyManagement.MaxTroopsPerArmy);
    }

    [Fact]
    public void A_computer_nations_turn_hires_every_qualifying_offer()
    {
        var state = TwoNationStateWithOffer(14, 12, pool: new[] { Offer(5, 14, 12), Offer(9, 14, 12) });

        var driven = AiScriptedStates.DriveOneTurn(state, seed: 1);

        Assert.Empty(driven.Outcome.State.MercenaryPool);
        Assert.Contains(
            driven.Outcome.Log, l => l.StartsWith("mercenary hire: 2", StringComparison.Ordinal));
    }

    /// <summary>
    /// R7: an embarked army sits on its fleet's tile (<c>EmbarkArmyCommandHandler</c> copies the fleet's
    /// <c>X</c>/<c>Y</c> onto it; only <c>CoveredTileCode</c> carries the <c>-1</c> sentinel), and
    /// <c>FUN_0044E41C</c> runs for every army of the acting nation — so it hires like any other. The
    /// original applies no fleet-capacity check here either
    /// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §3;
    /// 2026-10-05-split-army-aboard-a-fleet.md]</strong>.
    /// </summary>
    [Fact]
    public void An_embarked_army_hires_like_any_other()
    {
        var state = StateWithOffer(14, 12, armyMoney: 1000, pool: new[] { Offer(5, 14, 12) });
        var army = state.ArmyById("ai-army")!;
        state = state with
        {
            Armies = ValueList.Of(army with { AboardFleetId = "ai-fleet", CoveredTileCode = null }),
        };

        var result = AiMercenaryHirePass.Run(state, Ruleset, ArmyNation);

        Assert.Equal(1, result.OffersHired);
        Assert.Empty(result.State.MercenaryPool);
        Assert.True(result.State.ArmyById("ai-army")!.IsEmbarked);
    }

    /// <summary>
    /// R3: the original captures the army's money once at <c>FUN_0044E41C</c>'s entry, before resupply
    /// spends it, and gates the hire on that captured value. A 51-talent army that buys 10 tons at a
    /// foreign city (5 tons per talent) drops to 49, but still hires because the entry purse was above
    /// 50; gating on the post-resupply purse alone would wrongly block it.
    /// </summary>
    [Fact]
    public void The_money_gate_reads_the_entry_purse_not_the_post_resupply_one()
    {
        var state = MoneyGateState();
        var moneyAtTurnStart = new Dictionary<string, int>(StringComparer.Ordinal) { ["ai-army"] = 51 };

        var resupplied = AiResupplyPass.Run(state, Ruleset, ArmyNation);
        Assert.Equal(49, resupplied.State.ArmyById("ai-army")!.Money); // 10 tons / 5 tons per talent.

        // With the entry purse the hire happens; on the post-resupply purse alone it would not.
        Assert.Equal(1, AiMercenaryHirePass.Run(resupplied.State, Ruleset, ArmyNation, moneyAtTurnStart).OffersHired);
        Assert.Equal(0, AiMercenaryHirePass.Run(resupplied.State, Ruleset, ArmyNation).OffersHired);
    }

    /// <summary>
    /// R3, end to end: a real AI turn passes the entry-purse snapshot to the hire pass, so the same
    /// 51-talent army that resupplies down to 49 still hires. Reverting <see cref="AiTurn"/> to the
    /// snapshot-less call makes this assert zero hires.
    /// </summary>
    [Fact]
    public void A_full_ai_turn_gates_the_hire_on_the_entry_purse()
    {
        var state = MoneyGateState();

        var driven = AiScriptedStates.DriveOneTurn(state, seed: 1);

        Assert.Equal(49, driven.Outcome.State.ArmyById("ai-army")!.Money);
        Assert.Empty(driven.Outcome.State.MercenaryPool);
        Assert.Contains(
            driven.Outcome.Log, l => l.StartsWith("mercenary hire: 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The R3 fixture: a 5,000-troop north AI army holding 51 money and no supply, four tiles from the
    /// peaceful south offer city whose 100 tons it buys from, while north is at war with the third
    /// nation east. At 5 tons per talent the purchase costs 2 talents and leaves 49.
    /// </summary>
    private static GameState MoneyGateState()
    {
        var state = StateWithOffer(
            14, 12, cityOwner: "south", armyMoney: 51, pool: new[] { Offer(5, 14, 12) });
        var army = state.ArmyById("ai-army")!;
        state = state with
        {
            Armies = ValueList.Of(
                army with { Units = ValueList.Of(CaptureTestbed.Unit("light_infantry", 5000)) }),
            Cities = ValueList.From(state.Cities.Select(
                c => string.Equals(c.Id, "offer-city", StringComparison.Ordinal)
                    ? c with { SupplyTons = 100 }
                    : c)),
        };

        return AiScriptedStates.WithActiveSeat(state, ArmyNation);
    }

    /// <summary>
    /// Done-when 5: the pass adds zero random draws, so the same fixed seed reproduces the same turn and
    /// log exactly. Two independent streams from seed 7, one driven turn each, must agree.
    /// </summary>
    [Fact]
    public void A_fixed_seed_ai_turn_with_offers_reproduces_exactly()
    {
        var state = TwoNationStateWithOffer(14, 12, pool: new[] { Offer(5, 14, 12) });

        var first = AiScriptedStates.DriveOneTurn(state, seed: 7);
        var second = AiScriptedStates.DriveOneTurn(state, seed: 7);

        Assert.Equal(
            GameStateHash.Compute(first.Outcome.State), GameStateHash.Compute(second.Outcome.State));
        Assert.Equal(first.Outcome.State, second.Outcome.State);
        Assert.Equal(first.Outcome.Log, second.Outcome.Log);
        Assert.Empty(first.Outcome.State.MercenaryPool);
    }

    /// <summary>
    /// R1: the original resupplies and hires inside the same city iteration
    /// (<c>FUN_0044E41C</c>, report §3), so a hire at an earlier city raises the troop count that a later
    /// city's resupply sees. A 100-troop army (cap 1 ton) hires a 5,000-troop offer at city 0, whose own
    /// stock is empty, then reaches the depot at city 1 with 5,100 troops and fills to
    /// <c>cap(5,100)</c> = 51 tons. Running every resupply before every hire would cap the army at its
    /// pre-hire 1 ton and leave it at 1.
    /// </summary>
    [Fact]
    public void A_hire_before_a_later_supply_city_fills_the_army_to_its_new_capacity()
    {
        var before = InterleavedSupplyProbeState();
        Assert.Equal(100, before.ArmyById("ai-army")!.TotalTroops);
        Assert.Equal(0, before.ArmyById("ai-army")!.SupplyTons);

        var driven = AiScriptedStates.DriveOneTurn(before, seed: 1);

        var after = driven.Outcome.State.ArmyById("ai-army")!;
        Assert.Equal(100 + 5000, after.TotalTroops);
        Assert.Equal(51, after.SupplyTons);
        Assert.Equal(SupplyCapacity.ArmyCapacityTons(after.TotalTroops, Ruleset), after.SupplyTons);
        Assert.Empty(driven.Outcome.State.MercenaryPool);
    }

    /// <summary>
    /// R1, round 3: the same 100-troop army and 5,000-troop offer, but the offer city is the army's only
    /// supply city and holds 10,000 tons. The resupply inside that one city iteration comes before the
    /// hire there, so it fills to <c>cap(100)</c> = 1 ton and only then takes the offer: the army ends at
    /// 5,100 troops and 1 ton. Hiring before resupplying in the same iteration would fill to
    /// <c>cap(5,100)</c> = 51 instead, and the ordering test above cannot tell the two apart.
    /// </summary>
    [Fact]
    public void The_resupply_at_the_offer_city_precedes_its_hire()
    {
        var before = SameCitySupplyProbeState();
        Assert.Equal(100, before.ArmyById("ai-army")!.TotalTroops);
        Assert.Equal(0, before.ArmyById("ai-army")!.SupplyTons);

        var driven = AiScriptedStates.DriveOneTurn(before, seed: 1);

        var after = driven.Outcome.State.ArmyById("ai-army")!;
        Assert.Equal(100 + 5000, after.TotalTroops);
        Assert.Equal(1, after.SupplyTons);
        Assert.Equal(SupplyCapacity.ArmyCapacityTons(100, Ruleset), after.SupplyTons);
        Assert.Empty(driven.Outcome.State.MercenaryPool);
    }

    /// <summary>
    /// The R1 probe fixture: one north AI army of 100 troops at (10,10) with 1,000 money and no supply;
    /// city 0 is north's own at (12,10) with no stock and a 5,000-troop offer; city 1 is north's own depot
    /// at (13,10) with 10,000 tons. Both are within radius 4, and north is at war with south so the "at
    /// war with someone" gate holds. City order puts the offer before the depot.
    /// </summary>
    private static GameState InterleavedSupplyProbeState()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation("north", AiScriptedStates.DefaultPersonality, treasury: 5000),
            AiScriptedStates.AiNation("south", AiScriptedStates.DefaultPersonality, treasury: 5000),
        };

        var cities = new[]
        {
            CaptureTestbed.City("offer-city", "Offer City", 12, 10, "north", "north", 80, 0, 100, 200, 10),
            CaptureTestbed.City("depot", "Depot", 13, 10, "north", "north", 80, 0, 100, 200, 10)
                with { SupplyTons = 10000 },
            CaptureTestbed.City("south-home", "South Home", 30, 30, "south", "south", 80, 0, 100, 200, 10),
        };

        var army = CaptureTestbed.Army(
                "ai-army", ArmyNation, 10, 10, morale: 60,
                CaptureTestbed.Unit("light_infantry", 100))
            with { Money = 1000, SupplyTons = 0 };

        var state = BattleCommandTestbed.StateWith(nations, cities, new[] { army });
        state = state with
        {
            MercenaryPool = ValueList.Of(Offer(5, 12, 10, troops: 5000)),
            Relations = state.Relations.WithRelation(
                "north", "south", Ruleset.Diplomacy.StateCodes.War),
        };

        return AiScriptedStates.WithActiveSeat(state, ArmyNation);
    }

    /// <summary>
    /// The round-3 R1 probe: <see cref="InterleavedSupplyProbeState"/> with the depot removed and its
    /// 10,000 tons given to the offer city itself, so that one city is the army's only supply. Everything
    /// else — the 100-troop army, the 5,000-troop offer, the war and the radius — is unchanged, and the
    /// offer city stays first in city order.
    /// </summary>
    private static GameState SameCitySupplyProbeState()
    {
        var state = InterleavedSupplyProbeState();
        var cities = state.Cities
            .Where(c => !string.Equals(c.Id, "depot", StringComparison.Ordinal))
            .Select(c => string.Equals(c.Id, "offer-city", StringComparison.Ordinal)
                ? c with { SupplyTons = 10000 }
                : c);

        return state with { Cities = ValueList.From(cities) };
    }

    /// <summary>
    /// Bug #796: the class remark's fixed-order sentence once read "... by slot index since T76), so it
    /// adds zero draws", a closing parenthesis orphaned from the ordering list it belonged to. A comment
    /// asserts nothing at runtime, so only a test that reads the source can guard its wording: this test
    /// fails while the orphan is present and passes once it is gone. The balance check keeps a fix from
    /// simply deleting the parenthesis and leaving the opening one unmatched.
    /// </summary>
    [Fact]
    public void The_class_remark_has_no_orphaned_closing_parenthesis_after_the_T76_note()
    {
        var source = File.ReadAllText(Path.Combine(
            TestPaths.RepositoryRoot, "src", "IC2.Engine", "Ai", "AiMercenaryHirePass.cs"));

        var remark = RemarkBlock(source);

        // The rationale is still there, so the check below cannot pass on a deleted note.
        Assert.Contains("PoolSlotsAt", remark, StringComparison.Ordinal);
        Assert.Contains("since T76", remark, StringComparison.Ordinal);

        // The bug: the ")" that closes the fixed-order list sat after the em-dash note instead.
        Assert.DoesNotContain("since T76), so it adds zero draws", remark, StringComparison.Ordinal);

        // A closing parenthesis belongs to the list it closes, so the remark stays balanced.
        Assert.Equal(
            remark.Count(c => c == '('),
            remark.Count(c => c == ')'));
    }

    /// <summary>The class-level <c>&lt;remarks&gt;</c> block of a source file, opening tag included.</summary>
    private static string RemarkBlock(string source)
    {
        const string Open = "/// <remarks>";
        const string Close = "/// </remarks>";
        var start = source.IndexOf(Open, StringComparison.Ordinal);
        Assert.True(start >= 0, "AiMercenaryHirePass.cs has no <remarks> block.");
        var end = source.IndexOf(Close, start, StringComparison.Ordinal);
        Assert.True(end > start, "AiMercenaryHirePass.cs's <remarks> block is not closed.");
        return source[start..(end + Close.Length)];
    }
}
