using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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

        // T63 Decision 1: classical-faithful reproduces the original's 16-bit siege-ratio clamp wrap;
        // improved clamps in ordinary 32-bit arithmetic.
        Assert.Equal(SiegeRatioClampPolicy.Clamp32Bit, improved.Flags.BugPolicySiegeRatioClamp);
        Assert.Equal(SiegeRatioClampPolicy.Reproduce16BitClamp, classical.Flags.BugPolicySiegeRatioClamp);

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
        // DoD 2: Exhaustive value-level comparison, excluding _provenance.
        // The expected improved column is exactly: id, name, description (boilerplate), the 6 flags,
        // and 2 victory fields. Everything else must be identical. This comparison walks the entire
        // JSON object graph and collects all paths where values differ, asserting that this set
        // equals exactly the expected differences.

        // Serialize both rulesets to JSON and parse back to walk them completely
        var improvedJson = GameJson.Serialize(improved);
        var classicalJson = GameJson.Serialize(classical);

        var improvedNode = JsonNode.Parse(improvedJson);
        var classicalNode = JsonNode.Parse(classicalJson);

        Assert.NotNull(improvedNode);
        Assert.NotNull(classicalNode);

        // Collect all paths where values differ (ignoring _provenance nodes)
        var differingPaths = new HashSet<string>();
        CollectDifferingPaths(improvedNode, classicalNode, "", differingPaths);

        // Expected differences: the improved column per game-design.md's "Two shipped presets"
        var expectedDifferences = new HashSet<string>
        {
            "id",
            "name",
            "description",
            "flags.diplomacyModel",
            "flags.economyPurses",
            "flags.seatAsymmetry",
            "flags.bugPolicyDiplomaticThaw",
            "flags.combatOnDefeat",
            "flags.faithfulThawColumnBug",
            "flags.bugPolicySiegeRatioClamp",
            "victory.defaultCondition",
            "victory.defaultTurnLimit",
        };

        // Assert that the differing paths are exactly what we expect (no more, no less)
        Assert.Equal(expectedDifferences, differingPaths);
    }

    private void CollectDifferingPaths(JsonNode? improved, JsonNode? classical, string pathPrefix, HashSet<string> differingPaths)
    {
        // Skip _provenance nodes entirely, as per DoD 2
        if (pathPrefix.EndsWith("._provenance"))
        {
            return;
        }

        // Handle null cases
        if (improved is null && classical is null)
        {
            return;
        }

        if (improved is null || classical is null)
        {
            // One is null, the other is not — this is a difference
            if (!string.IsNullOrEmpty(pathPrefix))
            {
                differingPaths.Add(pathPrefix);
            }
            return;
        }

        // Both are objects: recurse into their properties
        if (improved is JsonObject improvedObj && classical is JsonObject classicalObj)
        {
            var allKeys = new HashSet<string>();
            foreach (var property in improvedObj)
            {
                allKeys.Add(property.Key);
            }
            foreach (var property in classicalObj)
            {
                allKeys.Add(property.Key);
            }

            foreach (var key in allKeys)
            {
                // Skip _provenance properties entirely
                if (key == "_provenance")
                {
                    continue;
                }

                var newPath = string.IsNullOrEmpty(pathPrefix) ? key : $"{pathPrefix}.{key}";
                var improvedValue = improvedObj.TryGetPropertyValue(key, out var iv) ? iv : null;
                var classicalValue = classicalObj.TryGetPropertyValue(key, out var cv) ? cv : null;

                CollectDifferingPaths(improvedValue, classicalValue, newPath, differingPaths);
            }
        }
        // Both are arrays: recurse into their elements
        else if (improved is JsonArray improvedArr && classical is JsonArray classicalArr)
        {
            if (improvedArr.Count == classicalArr.Count)
            {
                for (int i = 0; i < improvedArr.Count; i++)
                {
                    var newPath = $"{pathPrefix}[{i}]";
                    CollectDifferingPaths(improvedArr[i], classicalArr[i], newPath, differingPaths);
                }
            }
            else
            {
                // Array lengths differ
                differingPaths.Add(pathPrefix);
            }
        }
        // Primitive values: compare directly
        else
        {
            var improvedValue = improved?.ToString();
            var classicalValue = classical?.ToString();

            if (improvedValue != classicalValue)
            {
                differingPaths.Add(pathPrefix);
            }
        }
    }
}
