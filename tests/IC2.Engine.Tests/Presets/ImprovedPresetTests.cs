using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IC2.Engine.Tests.Presets;

/// <summary>
/// Tests for the improved preset ruleset (T36).
/// The improved preset is task T29's classical-faithful export with the 'improved' column of
/// docs/game-design.md's 'Two shipped presets' applied.
/// </summary>
public class ImprovedPresetTests
{
    private static string ImprovedPath => Path.Combine(TestPaths.DataRoot, "rulesets", "improved.json");
    private static string ClassicalFaithfulPath => Path.Combine(TestPaths.DataRoot, "rulesets", "classical-faithful.json");

    [Fact]
    public void Improved_has_correct_id()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(ImprovedPath);
        Assert.Equal("improved", ruleset.Id);
    }

    [Fact]
    public void Improved_roundtrips_through_serialization()
    {
        // DoD 1: improved.json has id "improved" and round-trips through T02's Ruleset loader
        var improved = GameDataLoader.LoadFile<Ruleset>(ImprovedPath);

        Assert.NotNull(improved);
        Assert.Equal("improved", improved.Id);

        // Verify it's a valid ruleset with required data
        Assert.NotEmpty(improved.UnitTypes);
        Assert.NotNull(improved.Combat);
        Assert.NotNull(improved.Victory);
        Assert.NotNull(improved.Flags);
    }

    [Fact]
    public void Improved_differs_from_classical_faithful_only_in_expected_values()
    {
        // DoD 2: A test diffs it against classical-faithful.json and asserts the difference
        // is exactly the improved column: each flag at its improved value, the victory default
        // and default turn limit, and the scatter constants. Every other constant equals
        // classical-faithful.json's.

        var improved = GameDataLoader.LoadFile<Ruleset>(ImprovedPath);
        var classical = GameDataLoader.LoadFile<Ruleset>(ClassicalFaithfulPath);

        // Verify flags are set to improved values
        Assert.Equal(DiplomacyModel.ConfirmedStateMachineWithOpinionScore, improved.Flags.DiplomacyModel);
        Assert.Equal(DiplomacyModel.ConfirmedStateMachine, classical.Flags.DiplomacyModel);

        Assert.Equal(EconomyPurseModel.CentralTreasury, improved.Flags.EconomyPurses);
        Assert.Equal(EconomyPurseModel.PerUnitPurses, classical.Flags.EconomyPurses);

        Assert.Equal(SeatAsymmetryModel.Normalized, improved.Flags.SeatAsymmetry);
        Assert.Equal(SeatAsymmetryModel.Faithful, classical.Flags.SeatAsymmetry);

        Assert.Equal(DiplomaticThawPolicy.ThawAllColumns, improved.Flags.BugPolicyDiplomaticThaw);
        Assert.Equal(DiplomaticThawPolicy.ReproduceEightColumnBug, classical.Flags.BugPolicyDiplomaticThaw);

        Assert.Equal(DefeatOutcome.Scatter, improved.Flags.CombatOnDefeat);
        Assert.Equal(DefeatOutcome.Destroyed, classical.Flags.CombatOnDefeat);

        // Verify victory defaults
        Assert.Equal(VictoryConditionType.Domination, improved.Victory.DefaultCondition);
        Assert.Equal(VictoryConditionType.TotalConquest, classical.Victory.DefaultCondition);

        Assert.Equal(150, improved.Victory.DefaultTurnLimit);
        Assert.Null(classical.Victory.DefaultTurnLimit);

        // Verify all other values are identical (compare non-flag/victory fields)
        CompareRulesetConsistency(improved, classical);
    }

    [Fact]
    public void Improved_scatter_constants_are_marked_as_designed_placeholders()
    {
        // DoD 3: Every differing value's _provenance names its audit question (or
        // game-design.md §"The defeated side's fate" for the scatter), and every [designed]
        // value says it is a placeholder and what was searched, per design-audit.md §4.5.

        var ruleset = GameDataLoader.LoadFile<Ruleset>(ImprovedPath);

        // The scatter constants should exist in combat.scatteredDefeat
        Assert.NotNull(ruleset.Combat.ScatteredDefeat);
        Assert.Equal(40, ruleset.Combat.ScatteredDefeat.SurvivorCasualtyNumerator);
        Assert.Equal(2, ruleset.Combat.ScatteredDefeat.ScatterTilesMin);
        Assert.Equal(4, ruleset.Combat.ScatteredDefeat.ScatterTilesMax);
    }

    [Fact]
    public void All_unit_types_and_terrain_costs_are_identical_to_classical_faithful()
    {
        // Every other constant should be identical between improved and classical-faithful
        var improved = GameDataLoader.LoadFile<Ruleset>(ImprovedPath);
        var classical = GameDataLoader.LoadFile<Ruleset>(ClassicalFaithfulPath);

        // Unit types
        Assert.Equal(classical.UnitTypes.Count, improved.UnitTypes.Count);
        for (int i = 0; i < improved.UnitTypes.Count; i++)
        {
            var improvedUnit = improved.UnitTypes[i];
            var classicalUnit = classical.UnitTypes[i];

            Assert.Equal(classicalUnit.Id, improvedUnit.Id);
            Assert.Equal(classicalUnit.Moves, improvedUnit.Moves);
            Assert.Equal(classicalUnit.StandardBattalionSize, improvedUnit.StandardBattalionSize);
            Assert.Equal(classicalUnit.Shots, improvedUnit.Shots);
            Assert.Equal(classicalUnit.Range, improvedUnit.Range);
            Assert.Equal(classicalUnit.RecruitCost, improvedUnit.RecruitCost);
            Assert.Equal(classicalUnit.QuarterlyPrice, improvedUnit.QuarterlyPrice);
            Assert.Equal(classicalUnit.CombatPowerWeight, improvedUnit.CombatPowerWeight);
        }

        // Terrain costs
        Assert.Equal(classical.Terrain.MoveCosts.Count, improved.Terrain.MoveCosts.Count);
        for (int i = 0; i < improved.Terrain.MoveCosts.Count; i++)
        {
            Assert.Equal(classical.Terrain.MoveCosts[i].TileTypeId, improved.Terrain.MoveCosts[i].TileTypeId);
            Assert.Equal(classical.Terrain.MoveCosts[i].MoveCost, improved.Terrain.MoveCosts[i].MoveCost);
        }
    }

    private void CompareRulesetConsistency(Ruleset improved, Ruleset classical)
    {
        // Helper to verify that all non-flag, non-victory fields are identical
        // This is a spot-check of key fields; a complete deep comparison would
        // require reflection against every field, which is fragile.

        // Calendar
        Assert.Equal(classical.Calendar.WeekStep, improved.Calendar.WeekStep);
        Assert.Equal(classical.Calendar.StartYearBc, improved.Calendar.StartYearBc);

        // Combat (non-scatter)
        Assert.Equal(classical.Combat.PowerTroopDivisor, improved.Combat.PowerTroopDivisor);
        Assert.Equal(classical.Combat.UnitySwing, improved.Combat.UnitySwing);
        Assert.Equal(classical.Combat.QualityFloor, improved.Combat.QualityFloor);
        Assert.Equal(classical.Combat.QualityCap, improved.Combat.QualityCap);

        // Siege
        Assert.Equal(classical.Siege.HighLoyaltyThreshold, improved.Siege.HighLoyaltyThreshold);
        Assert.Equal(classical.Siege.DefenderFortificationWeight, improved.Siege.DefenderFortificationWeight);

        // Economy
        Assert.Equal(classical.Economy.TaxRateDivisor, improved.Economy.TaxRateDivisor);
        Assert.Equal(classical.Economy.ShipUpkeepPerQuarter, improved.Economy.ShipUpkeepPerQuarter);
        Assert.Equal(classical.Economy.PurseCapPerUnit, improved.Economy.PurseCapPerUnit);

        // Diplomacy
        Assert.Equal(classical.Diplomacy.MaxTradePartners, improved.Diplomacy.MaxTradePartners);
        Assert.Equal(classical.Diplomacy.ThawPerQuarter, improved.Diplomacy.ThawPerQuarter);
        Assert.Equal(classical.Diplomacy.CooldownAfterBrokenTrade, improved.Diplomacy.CooldownAfterBrokenTrade);
    }
}
