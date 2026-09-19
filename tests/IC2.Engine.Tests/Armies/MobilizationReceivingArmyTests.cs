using IC2.Engine.Armies;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T55 Done-when 2, 3 and 5 at the level of the helper the original calls first —
/// <c>FUN_0044a120</c> and <c>FUN_0044a66c</c>
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §3).
/// </summary>
public sealed class MobilizationReceivingArmyTests
{
    private static Ruleset Ruleset => ArmiesTestbed.Ruleset;

    private static UnitSlot Unit(string name, int troops = 1_000) =>
        ArmiesTestbed.RegularUnit(name, troops: troops);

    /// <summary>A zero-troop unit slot: the original's empty slot inside a 20-slot army record.</summary>
    private static UnitSlot EmptySlot() =>
        new(MercenaryLabel: 0, UnitTypeId: "light_infantry", Troops: 0, Quality: 0, Name: string.Empty);

    private static CityState CityAt(int x, int y, string owner = ArmiesTestbed.NorthNationId) =>
        new("fixture-city", "Fixture", x, y, owner, owner, Loyalty: 100, SupplyTons: 0,
            FortificationCode: 0, PopulationThousands: 10, MaxPopulationThousands: 20, Tribute: 0,
            UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty);

    /// <summary>
    /// Done-when 5: "<c>FUN_0044a66c</c> returns one past the highest occupied slot", not the first
    /// hole — pinned with fixtures that <em>have</em> a gap, in both positions a gap can take.
    /// </summary>
    [Fact]
    public void The_free_unit_slot_is_one_past_the_highest_occupied_slot_and_gaps_are_never_reused()
    {
        var empty = ArmiesTestbed.Army("a", ArmiesTestbed.NorthNationId, 0, 0, Array.Empty<UnitSlot>());
        Assert.Equal(0, MobilizationReceivingArmy.FirstFreeUnitSlot(empty));

        var dense = ArmiesTestbed.Army("a", ArmiesTestbed.NorthNationId, 0, 0, new[] { Unit("1st Foot Battalion") });
        Assert.Equal(1, MobilizationReceivingArmy.FirstFreeUnitSlot(dense));

        // A hole below an occupied slot: the answer is 2, not the hole at 0.
        var holeBelow = ArmiesTestbed.Army(
            "a", ArmiesTestbed.NorthNationId, 0, 0, new[] { EmptySlot(), Unit("1st Foot Battalion") });
        Assert.Equal(2, MobilizationReceivingArmy.FirstFreeUnitSlot(holeBelow));

        // A hole above the highest occupied slot: the answer is that hole.
        var holeAbove = ArmiesTestbed.Army(
            "a", ArmiesTestbed.NorthNationId, 0, 0, new[] { Unit("1st Foot Battalion"), EmptySlot() });
        Assert.Equal(1, MobilizationReceivingArmy.FirstFreeUnitSlot(holeAbove));

        // Two holes, one either side.
        var both = ArmiesTestbed.Army(
            "a", ArmiesTestbed.NorthNationId, 0, 0,
            new[] { EmptySlot(), Unit("1st Foot Battalion"), EmptySlot() });
        Assert.Equal(2, MobilizationReceivingArmy.FirstFreeUnitSlot(both));

        var full = ArmiesTestbed.Army(
            "a", ArmiesTestbed.NorthNationId, 0, 0,
            Enumerable.Range(1, Ruleset.ArmyManagement.MaxUnitsPerArmy).Select(i => Unit($"{i}th Foot Battalion")));
        Assert.Equal(Ruleset.ArmyManagement.MaxUnitsPerArmy, MobilizationReceivingArmy.FirstFreeUnitSlot(full));
    }

    /// <summary>
    /// Done-when 2's asymmetry, both halves: a human seat accepts Chebyshev distance <em>exactly</em>
    /// 1, an AI seat accepts anything under 6.
    /// </summary>
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    [InlineData(7, false, false)]
    public void The_human_radius_is_exactly_one_and_the_ai_radius_is_under_six(
        int distance, bool humanAccepts, bool aiAccepts)
    {
        var rules = Ruleset.Recruitment;

        Assert.Equal(
            humanAccepts,
            MobilizationReceivingArmy.Accepts(distance, SeatControl.Human, rules, SeatAsymmetryModel.Faithful));
        Assert.Equal(
            aiAccepts,
            MobilizationReceivingArmy.Accepts(distance, SeatControl.Ai, rules, SeatAsymmetryModel.Faithful));
    }

    /// <summary>
    /// Under <c>improved</c> the AI keeps no reach the player lacks — the edge
    /// <see cref="MobilizationReceivingArmy.Accepts"/>'s remark claims.
    /// </summary>
    [Fact]
    public void Normalized_gives_an_ai_seat_the_human_radius()
    {
        var rules = Ruleset.Recruitment;

        Assert.True(MobilizationReceivingArmy.Accepts(5, SeatControl.Ai, rules, SeatAsymmetryModel.Faithful));
        Assert.False(MobilizationReceivingArmy.Accepts(5, SeatControl.Ai, rules, SeatAsymmetryModel.Normalized));
        Assert.True(MobilizationReceivingArmy.Accepts(1, SeatControl.Ai, rules, SeatAsymmetryModel.Normalized));
        Assert.False(MobilizationReceivingArmy.Accepts(0, SeatControl.Ai, rules, SeatAsymmetryModel.Normalized));
    }

    /// <summary>
    /// Done-when 2's tie-break: the <em>last</em> matching army index wins, not the nearest. The
    /// fixture makes the two disagree — the earlier army sits on the city's doorstep for both seats,
    /// the later one is further away but still in range for an AI seat.
    /// </summary>
    [Fact]
    public void The_tie_break_is_the_last_matching_index_not_the_nearest()
    {
        var city = CityAt(10, 10);
        var near = ArmiesTestbed.Army("near", ArmiesTestbed.NorthNationId, 11, 10, new[] { Unit("1st Foot Battalion") });
        var far = ArmiesTestbed.Army("far", ArmiesTestbed.NorthNationId, 9, 9, new[] { Unit("2nd Foot Battalion") });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), near, far);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        // Both are at distance 1, so both match; the later index wins.
        var chosen = MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 100);
        Assert.Equal("far", chosen!.Army.Id);

        // Reversed, the answer follows the order rather than the geometry -- which is what proves it
        // is the last index and not, say, the lower id or the further tile.
        var reversed = ArmiesTestbed.WithArmies(state, far, near);
        Assert.Equal("near", MobilizationReceivingArmy.Find(reversed, city, nation, Ruleset, 100)!.Army.Id);
    }

    /// <summary>
    /// Done-when 3: the capacity test runs once, against the army the adjacency loop settled on. A
    /// <em>full</em> last match ends the search — an earlier adjacent army with room is never tried,
    /// and the caller is told to create a new army instead. This is the clause the corpus pair turns
    /// on, and the one a "pick any army with room" implementation would silently get wrong.
    /// </summary>
    [Fact]
    public void A_full_last_match_ends_the_search_rather_than_falling_back_to_an_earlier_army()
    {
        var city = CityAt(10, 10);
        var roomy = ArmiesTestbed.Army(
            "roomy", ArmiesTestbed.NorthNationId, 9, 10, new[] { Unit("1st Foot Battalion") });
        var full = ArmiesTestbed.Army(
            "full", ArmiesTestbed.NorthNationId, 11, 10,
            Enumerable.Range(1, Ruleset.ArmyManagement.MaxUnitsPerArmy).Select(i => Unit($"{i}th Foot Battalion")));

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), roomy, full);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        Assert.Null(MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 100));

        // The control: with the full army removed, the roomy one is taken -- so the null above is the
        // capacity rule and not the fixture failing to match at all.
        var onlyRoomy = ArmiesTestbed.WithArmies(state, roomy);
        Assert.Equal("roomy", MobilizationReceivingArmy.Find(onlyRoomy, city, nation, Ruleset, 100)!.Army.Id);
    }

    /// <summary>
    /// Done-when 3's other cap: an army may not pass <see cref="ArmyManagementRules.MaxTroopsPerArmy"/>,
    /// and the original's test is <c>total + incoming &lt; 100,001</c> — so landing exactly on the cap
    /// is accepted and one more is not.
    /// </summary>
    [Fact]
    public void The_troop_cap_is_inclusive_and_one_troop_over_it_sends_the_recruit_to_a_new_army()
    {
        var city = CityAt(10, 10);
        var cap = Ruleset.ArmyManagement.MaxTroopsPerArmy;
        var heavy = ArmiesTestbed.Army(
            "heavy", ArmiesTestbed.NorthNationId, 11, 10, new[] { Unit("1st Foot Battalion", cap - 5_000) });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), heavy);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        Assert.NotNull(MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 5_000));
        Assert.Null(MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 5_001));
    }

    /// <summary>Another nation's adjacent army is not a candidate, however close it stands.</summary>
    [Fact]
    public void A_foreign_army_is_never_a_candidate()
    {
        var city = CityAt(10, 10);
        var foreign = ArmiesTestbed.Army("foreign", ArmiesTestbed.SouthNationId, 11, 10, new[] { Unit("1st Foot Battalion") });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), foreign);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        Assert.Null(MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 100));
    }

    /// <summary>
    /// The edge <see cref="MobilizationReceivingArmy"/>'s <c>[derived]</c> embarkation remark claims:
    /// an army of the right nation, at the right distance, aboard a fleet, is skipped — the recruit
    /// does not board a ship.
    /// </summary>
    [Fact]
    public void An_embarked_army_is_skipped_even_when_it_is_adjacent()
    {
        var city = CityAt(10, 10);
        var embarked = ArmiesTestbed.Army(
            "embarked", ArmiesTestbed.NorthNationId, 11, 10, new[] { Unit("1st Foot Battalion") },
            coveredTileCode: null, aboardFleetId: "some-fleet");

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), embarked);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        Assert.Null(MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 100));

        // The control: the same army ashore is taken.
        var ashore = ArmiesTestbed.WithArmies(
            state, embarked with { AboardFleetId = null, CoveredTileCode = 2 });
        Assert.Equal("embarked", MobilizationReceivingArmy.Find(ashore, city, nation, Ruleset, 100)!.Army.Id);
    }

    /// <summary>
    /// The seat asymmetry through <see cref="MobilizationReceivingArmy.Find"/> rather than through the
    /// predicate alone: the same army five tiles from the city is found for an AI nation and not for a
    /// human one.
    /// </summary>
    [Fact]
    public void A_five_tile_army_is_found_for_an_ai_seat_and_not_for_a_human_one()
    {
        var city = CityAt(10, 10);
        var distant = ArmiesTestbed.Army("distant", ArmiesTestbed.NorthNationId, 15, 10, new[] { Unit("1st Foot Battalion") });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), distant);
        state = state with { Cities = ValueList.Of(city) };
        var human = state.NationById(ArmiesTestbed.NorthNationId)!;
        Assert.Equal(SeatControl.Human, human.Control);

        Assert.Null(MobilizationReceivingArmy.Find(state, city, human, Ruleset, incomingTroops: 100));

        var asAi = human with { Control = SeatControl.Ai };
        Assert.Equal("distant", MobilizationReceivingArmy.Find(state, city, asAi, Ruleset, 100)!.Army.Id);
    }

    /// <summary>
    /// The one place the two seats' predicates disagree at close range: an army standing <em>on</em>
    /// the city's own tile. A human seat's <c>d == 1</c> refuses it; an AI seat's <c>d &lt; 6</c>
    /// accepts it. No engine seam puts an army on a city tile — <c>MoveArmyCommandHandler</c> blocks
    /// the step and <see cref="MobilizationArmyCreation"/> never places one there — so this fixture is
    /// built by hand, and it is what makes "<c>d == 1</c>, not <c>d &lt;= 1</c>" a checkable claim
    /// rather than a restatement of the report.
    /// </summary>
    [Fact]
    public void An_army_on_the_citys_own_tile_is_refused_for_a_human_seat_and_accepted_for_an_ai_one()
    {
        var city = CityAt(10, 10);
        var onTop = ArmiesTestbed.Army("on-top", ArmiesTestbed.NorthNationId, 10, 10, new[] { Unit("1st Foot Battalion") });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), onTop);
        state = state with { Cities = ValueList.Of(city) };
        var human = state.NationById(ArmiesTestbed.NorthNationId)!;

        Assert.Null(MobilizationReceivingArmy.Find(state, city, human, Ruleset, incomingTroops: 100));
        Assert.Equal(
            "on-top",
            MobilizationReceivingArmy.Find(state, city, human with { Control = SeatControl.Ai }, Ruleset, 100)!.Army.Id);
    }

    /// <summary>The slot the recruit is written to is the receiving army's own free slot.</summary>
    [Fact]
    public void The_chosen_slot_index_is_the_receiving_armys_free_slot()
    {
        var city = CityAt(10, 10);
        var army = ArmiesTestbed.Army(
            "a", ArmiesTestbed.NorthNationId, 11, 11,
            new[] { EmptySlot(), Unit("1st Foot Battalion"), Unit("2nd Foot Battalion") });

        var state = ArmiesTestbed.WithArmies(ArmiesTestbed.InitialState(), army);
        state = state with { Cities = ValueList.Of(city) };
        var nation = state.NationById(ArmiesTestbed.NorthNationId)!;

        var chosen = MobilizationReceivingArmy.Find(state, city, nation, Ruleset, incomingTroops: 100)!;
        Assert.Equal(3, chosen.UnitSlotIndex);
    }
}
