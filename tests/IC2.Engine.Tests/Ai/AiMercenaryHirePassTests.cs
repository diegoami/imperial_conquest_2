using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Cities.Capture;
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
}
