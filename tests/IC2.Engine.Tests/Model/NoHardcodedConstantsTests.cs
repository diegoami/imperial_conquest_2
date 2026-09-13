using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// DoD 5: "No gameplay constant is hardcoded in C#: a test asserts that every ruleset-governed number
/// the model exposes is sourced from the loaded <c>Ruleset</c> object."
/// </summary>
/// <remarks>
/// Checked three ways, because any one of them alone has a hole:
/// <list type="number">
/// <item><description>
/// Every number <see cref="RulesetNumbers.Enumerate"/> exposes is present, at the same path and with
/// the same value, in the ruleset file that was loaded.
/// </description></item>
/// <item><description>
/// When every number in that file is changed, every exposed number changes with it. A value that came
/// from a C# literal could not track the file, so this is the check that actually proves sourcing
/// rather than coincidence.
/// </description></item>
/// <item><description>
/// No numeric constant exists anywhere in the model or serialization namespaces, apart from one
/// explicitly allowlisted schema-version identifier, which is a file contract rather than a rule.
/// </description></item>
/// </list>
/// </remarks>
public class NoHardcodedConstantsTests
{
    /// <summary>
    /// The only numeric constant the engine's model and serialization code is allowed to declare.
    /// It identifies the file format, not a rule, so it does not belong in a ruleset.
    /// </summary>
    private static readonly string[] AllowedNumericConstants =
    {
        $"{typeof(GameDataSchema).FullName}.{nameof(GameDataSchema.CurrentVersion)}",
    };

    [Fact]
    public void Every_number_the_ruleset_exposes_is_present_in_the_file_it_was_loaded_from()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyRulesetFile))!;

        var numbers = RulesetNumbers.Enumerate(ruleset);
        Assert.NotEmpty(numbers);

        foreach (var number in numbers)
        {
            var node = ResolvePath(document, number.Path);
            Assert.True(node is not null, $"'{number.Path}' is exposed by the model but absent from the ruleset file.");
            Assert.Equal(number.Value, node!.GetValue<double>());
        }
    }

    [Fact]
    public void Changing_every_number_in_the_file_changes_every_number_the_model_exposes()
    {
        var original = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyRulesetFile))!;

        // An affine change so no two distinct values collide, and so a value left untouched would be
        // visible as an unchanged number rather than as a coincidence.
        MutateNumbers(document, skipKeyAtRoot: "schemaVersion");

        var mutated = GameDataLoader.Load<Ruleset>("mutated-ruleset.json", document.ToJsonString());

        var before = RulesetNumbers.Enumerate(original);
        var after = RulesetNumbers.Enumerate(mutated);

        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Path, after[i].Path);
            Assert.Equal(Mutate(before[i].Value), after[i].Value);
        }
    }

    [Fact]
    public void The_ruleset_surface_covers_every_subsystem_later_tasks_consume()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var paths = RulesetNumbers.Enumerate(ruleset).Select(n => n.Path).ToHashSet(StringComparer.Ordinal);

        string[] required =
        {
            "calendar.weekStep",
            "unitTypes[0].combatPowerWeight",
            "terrain.moveCosts[0].moveCost",
            "economy.purseCapPerUnit",
            "recruitment.troopsPerCostUnit",
            "armyManagement.maxTroopsPerArmy",
            "naval.transportTroopsPerShip",
            "combat.winnerCasualtyNumerator",
            "combat.naval.conditionDivisor",
            "combat.scatteredDefeat.scatterTilesMin",
            "combat.detailedResolver.typeEffectiveness[0][0]",
            "siege.archerStrengthMultiplier",
            "loyalty.defectionFloor",
            "diplomacy.cooldownAfterEndedWar",
            "cityOrders.orders[0].inProgressEncodingRadix",
            "newsLog.ringBufferSlots",
            "mapMarkers.armyTroopTierThresholds[0]",
            "victory.hardEndYearBc",
        };

        foreach (var path in required)
        {
            Assert.True(paths.Contains(path), $"The ruleset no longer exposes '{path}'.");
        }
    }

    [Fact]
    public void The_model_declares_no_numeric_constants_beyond_the_allowlist()
    {
        var assembly = typeof(Ruleset).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace is not "IC2.Engine.Model" and not "IC2.Engine.Serialization")
            {
                continue;
            }

            if (type.IsEnum)
            {
                continue;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags))
            {
                var isConstantLike = field.IsLiteral || (field.IsStatic && field.IsInitOnly);
                if (!isConstantLike || !JsonContract.IsNumeric(field.FieldType))
                {
                    continue;
                }

                var name = $"{type.FullName}.{field.Name}";
                if (!AllowedNumericConstants.Contains(name, StringComparer.Ordinal))
                {
                    offenders.Add(name);
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Gameplay numbers belong in the ruleset, not in C#. Found numeric constants: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Two_rulesets_with_different_numbers_drive_the_model_differently()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var fortify = ruleset.CityOrders.Orders.FindById(o => o.Id, "fortify")!;

        // The same stored fortification word decodes differently under a different encoding radix,
        // which is only possible because the radix is read from the ruleset rather than written in C#.
        var wider = fortify with { InProgressEncodingRadix = 1000, MaxPercent = 1000 };

        Assert.True(FortificationCode.IsOrderInProgress(340, fortify));
        Assert.Equal(40, FortificationCode.FinishedPercent(340, fortify));
        Assert.Equal(3, FortificationCode.PendingPoints(340, fortify));

        Assert.False(FortificationCode.IsOrderInProgress(340, wider));
        Assert.Equal(340, FortificationCode.FinishedPercent(340, wider));
    }

    private static double Mutate(double value) => (value * 2) + 7;

    private static void MutateNumbers(JsonNode node, string? skipKeyAtRoot)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (skipKeyAtRoot is not null && string.Equals(key, skipKeyAtRoot, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var child = obj[key];
                    if (child is JsonValue value && value.TryGetValue<double>(out var number))
                    {
                        obj[key] = JsonValue.Create(Mutate(number));
                    }
                    else if (child is not null)
                    {
                        MutateNumbers(child, skipKeyAtRoot: null);
                    }
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is JsonValue value && value.TryGetValue<double>(out var number))
                    {
                        array[i] = JsonValue.Create(Mutate(number));
                    }
                    else if (child is not null)
                    {
                        MutateNumbers(child, skipKeyAtRoot: null);
                    }
                }

                break;

            default:
                break;
        }
    }

    private static JsonValue? ResolvePath(JsonNode root, string path)
    {
        JsonNode? current = root;
        var index = 0;
        while (index < path.Length && current is not null)
        {
            if (path[index] == '.')
            {
                index++;
                continue;
            }

            if (path[index] == '[')
            {
                var close = path.IndexOf(']', index);
                var number = int.Parse(path[(index + 1)..close], CultureInfo.InvariantCulture);
                current = current is JsonArray array && number < array.Count ? array[number] : null;
                index = close + 1;
                continue;
            }

            var next = path.IndexOfAny(new[] { '.', '[' }, index);
            var name = next < 0 ? path[index..] : path[index..next];
            current = current is JsonObject obj && obj.TryGetPropertyValue(name, out var child) ? child : null;
            index = next < 0 ? path.Length : next;
        }

        return current as JsonValue;
    }
}
