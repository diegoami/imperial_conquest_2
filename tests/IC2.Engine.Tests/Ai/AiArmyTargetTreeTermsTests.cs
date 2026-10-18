using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

// T178: arithmetic from research 500e521, strategic-ai-regions-resupply §2.2,
// FUN_0044ece4 / FUN_0044ee60. Synthetic inputs isolate each evidenced term;
// these are code-formula regressions, not an original-game replay claim.
public sealed partial class AiArmyTargetTreeScorerTests
{
    private static ArmyState Composed(string id, string nation, int x, params (string Type, int Troops)[] units) =>
        CaptureFixtures.Army(id, nation, x, 10, 60,
            units.Select(u => CaptureFixtures.Unit(u.Type, u.Troops)).ToArray()) with { Moves = 9 };

    [Fact]
    public void Field_power_is_used_for_actor_and_target_while_city_assault_stays_distinct()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 160));
        var enemy = Composed("enemy", Them, 13, ("light_infantry", 800));
        var view = ViewOf([], [actor, enemy]);
        Assert.Equal(120, FieldStrength(actor, view));
        Assert.Equal(120, FieldStrength(enemy, view));
        Assert.Equal(120, Strength(actor, view));
        Assert.Equal(600, Strength(enemy, view));
        var result = AiArmyTargetTree.ScoreArmyTarget(view, actor);
        Assert.Equal(("enemy", 110L, 3), (result.Army!.Id, result.Score, result.Distance));
        // Reverse the compositions to independently distinguish the actor helper call.
        var reverse = AiArmyTargetTree.ScoreArmyTarget(ViewOf([], [actor with { Nation = Them }, enemy with { Nation = Us }]), enemy with { Nation = Us });
        Assert.Equal(("actor", 110L, 3), (reverse.Army!.Id, reverse.Score, reverse.Distance));
    }

    [Theory]
    [InlineData(true, 55L)]
    [InlineData(false, 1220L)]
    public void Field_scorer_preserves_per_slot_truncation_for_both_sides(bool roundedActor, long expected)
    {
        var rounded = Composed("rounded", roundedActor ? Us : Them, roundedActor ? 10 : 13,
            ("heavy_infantry", 159), ("light_infantry", 3), ("light_infantry", 3));
        var exact = Composed("exact", roundedActor ? Them : Us, roundedActor ? 13 : 10, ("heavy_infantry", 160));
        var actor = roundedActor ? rounded : exact;
        var target = roundedActor ? exact : rounded;
        var view = ViewOf([], [actor, target]);
        // Per-slot weighted total 159, versus sum-first 160: powers 60 versus 120.
        Assert.Equal(60, FieldStrength(rounded, view));
        Assert.Equal(120, FieldStrength(exact, view));
        var scored = AiArmyTargetTree.ScoreArmyTarget(view, actor);
        Assert.Equal((target.Id, expected, 3), (scored.Army!.Id, scored.Score, scored.Distance));
    }

    [Fact]
    public void Winning_distance_is_restored_only_after_candidate_selection_and_id_ties_are_preserved()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 160));
        var near = Composed("z-near", Them, 13, ("heavy_infantry", 160));
        var far = Composed("a-far", Them, 10, ("heavy_infantry", 160)) with { Y = 18 };
        var picked = AiArmyTargetTree.ScoreArmyTarget(ViewOf([], [actor, far, near]), actor);
        // Raw scores 107 > 102. Restoring each distance before selection ties at 110,
        // and the approved ID policy would incorrectly choose a-far.
        Assert.Equal(("z-near", 110L, 3), (picked.Army!.Id, picked.Score, picked.Distance));
        var tie = far with { Y = 13 };
        var tied = AiArmyTargetTree.ScoreArmyTarget(ViewOf([], [actor, near, tie]), actor);
        Assert.Equal(("a-far", 110L, 3), (tied.Army!.Id, tied.Score, tied.Distance));
        var none = AiArmyTargetTree.ScoreArmyTarget(ViewOf([], [actor]), actor);
        Assert.Null(none.Army);
        Assert.Equal((0L, 0), (none.Score, none.Distance));
    }

    [Fact]
    public void Negative_region_subtraction_truncates_before_winning_distance_is_restored()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 79));
        var enemy = Composed("enemy", Them, 13, ("heavy_infantry", 80));
        var view = ViewOf([City("home", Us, 9, 10), City("foreign", Them, 14, 10)],
            [actor, enemy], ("home", 0), ("foreign", 1));
        var scored = AiArmyTargetTree.ScoreArmyTarget(view, actor);
        Assert.Equal(("enemy", 1L, 3), (scored.Army!.Id, scored.Score, scored.Distance)); // -3 - (-3/2) + 3
    }

    private static AiView AllegianceView(ArmyState actor, CityState city, int garrison = 6, int reduction = 10)
    {
        var view = ViewOf([city], [actor]);
        var nations = view.State.Nations.Select(n => n.Id == Them
            ? n with { RecruitmentSlots = ValueList.Of(new RecruitmentSlot(city.Id, "heavy_infantry", garrison, 4)) }
            : n);
        var state = view.State with { Nations = ValueList.From(nations), RandomSeed = 178 };
        var rules = view.Ruleset with { Siege = view.Ruleset.Siege with { AttackerIsAllegianceDefenderReductionPercent = reduction } };
        return new AiView(state, rules, OpenPlain, Us);
    }

    [Theory]
    [InlineData(Us, 10, 3300L)]
    [InlineData(Third, 10, 2200L)]
    [InlineData(Us, 0, 2200L)]
    [InlineData(Us, 50, 6600L)]
    [InlineData(Us, 100, 0L)]
    public void Allegiance_reduction_consumes_existing_key_multiplies_before_division_and_guards_zero(
        string allegiance, int reduction, long expected)
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 80));
        var city = City("city", Them, 18, 10, loyalty: 0, fort: 0, pop: 0) with { Allegiance = allegiance };
        var view = AllegianceView(actor, city, garrison: 7, reduction);
        Assert.Equal(3, Defense(city, view)); // 7/2 garrison; 3*90/100=2, unlike 3-(3*10/100)=3.
        var scored = AiArmyTargetTree.ScoreCityTarget(view, actor);
        Assert.Equal(expected, scored.Score);
        Assert.Equal(reduction == 100 ? null : "city", scored.City?.Id);
        Assert.Equal(reduction == 100 ? 0 : 8, scored.Distance);
    }

    [Theory]
    [InlineData("city", 3, 60, 237L)]
    [InlineData("them-capital", 8, 75, 142L)]
    [InlineData("them-capital", 3, 60, 471L)]
    public void Allegiance_scales_owner_four_fifths_plus_garrison_before_both_doubling_gates(
        string cityId, int distance, int morale, long expected)
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", morale == 75 ? 80 : 160)) with { Morale = morale };
        var city = City(cityId, Them, 10 + distance, 10, loyalty: 1, fort: 0, pop: 0) with { Allegiance = Us };
        var view = AllegianceView(actor, city);
        Assert.Equal(123, Defense(city, view)); // (150*4/5) + 6/2; allegiance then yields 110.
        var scored = AiArmyTargetTree.ScoreCityTarget(view, actor);
        Assert.Equal((cityId, expected, distance), (scored.City!.Id, scored.Score, scored.Distance));
    }

    [Fact]
    public void Returned_distance_changes_real_decision_threshold_with_identical_seed_and_unchanged_state()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 160)) with { SupplyTons = 0 };
        var target = Composed("enemy", Them, 25, ("heavy_infantry", 160));
        var view = ViewOf([], [actor, target]);
        var before = view.State;
        var one = AiArmyTargetTree.Decide(view, actor, atWar: true);
        var two = AiArmyTargetTree.Decide(new AiView(before with { }, view.Ruleset, OpenPlain, Us), actor, atWar: true);
        Assert.Equal(AiArmyTargetTree.Kind.AttackArmy, one.Selected);
        Assert.Equal(one, two);
        Assert.Equal(before, view.State);
        Assert.Equal(before.RandomSeed, view.State.RandomSeed);
        // Score 110 meets the threshold; old return 110-15=95 selected resupply.
        // Neither pure scorer nor Decide accepts an IRng: scorer draw count is structurally zero.
        Assert.Equal(110, AiArmyTargetTree.ScoreArmyTarget(view, actor).Score);
    }
    [Fact]
    public void City_scorer_keeps_assault_strength_when_field_power_differs()
    {
        var actor = Composed("actor", Us, 10, ("light_infantry", 800));
        var city = City("city", Them, 18, 10, loyalty: 0, fort: 0, pop: 0);
        var view = AllegianceView(actor, city, garrison: 1200);
        Assert.Equal(120, FieldStrength(actor, view));
        Assert.Equal(600, Strength(actor, view));
        Assert.Equal(600, Defense(city, view));
        var scored = AiArmyTargetTree.ScoreCityTarget(view, actor);
        Assert.Equal(("city", 110L, 8), (scored.City!.Id, scored.Score, scored.Distance));
    }

    [Fact]
    public void Field_composition_changes_the_real_decision_threshold()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 160)) with { SupplyTons = 0 };
        var target = Composed("enemy", Them, 25, ("light_infantry", 800));
        var view = ViewOf([], [actor, target]);
        Assert.Equal(110, AiArmyTargetTree.ScoreArmyTarget(view, actor).Score);
        Assert.Equal(AiArmyTargetTree.Kind.AttackArmy, AiArmyTargetTree.Decide(view, actor, atWar: true).Selected);
        // Old assault ratio 110*120/600=22 is below 100 even after distance restoration.
    }

    [Fact]
    public void Allegiance_reduction_changes_the_real_city_decision_threshold()
    {
        var actor = Composed("actor", Us, 10, ("heavy_infantry", 80)) with { SupplyTons = 0 };
        var city = City("city", Them, 18, 10, loyalty: 0, fort: 0, pop: 0) with { Allegiance = Us };
        var reduced = AllegianceView(actor, city, garrison: 138);
        var unreduced = AllegianceView(actor, city, garrison: 138, reduction: 0);
        Assert.Equal(69, Defense(city, reduced)); // reduced 69*9/10=62; 6600/62=106, versus 6600/69=95.
        Assert.Equal(106, AiArmyTargetTree.ScoreCityTarget(reduced, actor).Score);
        Assert.Equal(95, AiArmyTargetTree.ScoreCityTarget(unreduced, actor).Score);
        Assert.Equal(AiArmyTargetTree.Kind.AttackCity, AiArmyTargetTree.Decide(reduced, actor, atWar: true).Selected);
        Assert.Equal(AiArmyTargetTree.Kind.MoveToResupplyCity, AiArmyTargetTree.Decide(unreduced, actor, atWar: true).Selected);
    }
}
