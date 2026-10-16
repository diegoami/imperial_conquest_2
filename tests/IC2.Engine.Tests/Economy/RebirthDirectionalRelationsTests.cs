using System.Text.Json.Nodes;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Model;
using IC2.Engine.Tests.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Economy;

public class RebirthDirectionalRelationsTests
{
    private static Ruleset Rules => PersistenceTestbed.Toy.Ruleset;

    private static GameState Before()
    {
        var dead = CaptureTestbed.Nation("north", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("south");
        var outsider = CaptureTestbed.Nation("east");
        var cities = Enumerable.Range(0, 8).Select(i =>
            CaptureTestbed.City($"q{i}", $"Q{i}", i, 0, "south", "north", 0, 0, 10, 20, 0))
            .Append(CaptureTestbed.City("spare", "Spare", 0, 1, "south", "south", 50, 0, 10, 20, 0));
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner, outsider }, cities);
        // Start asymmetric; owner keeps a spare city so elimination cannot mask the reset.
        return state with { Relations = state.Relations
            .WithRelation("north", "south", 2)
            .WithRelation("north", "east", 1)
            .WithDirectedRelation("north", "east", -5)
            .WithRelation("south", "east", -3) };
    }

    private static GameState Reborn(GameState before, bool preserve)
    {
        var rng = new ScriptedRng(nextIntDraws: new[] { 0 });
        var result = Rebirth.Run(before,
            Rules with { Economy = Rules.Economy with { RebirthPreservesInboundRelations = preserve } },
            before.NationById("north")!, NullEventSink.Instance, rng);
        rng.AssertAllDrawsConsumed();
        return result;
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Reset_preserves_inbound_only_under_faithful_but_cooldowns_stay_pairwise(bool preserve, int inbound)
    {
        var before = Before();
        var result = Reborn(before, preserve);
        Assert.False(result.NationById("north")!.Eliminated);
        Assert.Equal(0, result.Relations.Get("north", "east"));
        Assert.Equal(inbound, result.Relations.Get("east", "north"));
        Assert.Equal(-5, before.Relations.Get("north", "east"));
        Assert.Equal(Rules.Economy.RebirthDefectionRelationPenalty, result.Relations.Get("north", "south"));
        Assert.Equal(Rules.Economy.RebirthDefectionRelationPenalty, result.Relations.Get("south", "north"));
        Assert.Equal(-3, result.Relations.Get("south", "east"));
        Assert.Equal(-3, result.Relations.Get("east", "south"));
        foreach (var id in result.Relations.NationIds) Assert.Equal(0, result.Relations.Get(id, id));
        Assert.Equal(result, Reborn(before, preserve));
    }

    [Fact]
    public void Directed_write_is_immutable_and_pairwise_setter_only_changes_its_pair()
    {
        var before = Before().Relations;
        var directed = before.WithDirectedRelation("south", "east", -7);
        Assert.Equal(-3, before.Get("south", "east"));
        Assert.Equal(-7, directed.Get("south", "east"));
        Assert.Equal(-3, directed.Get("east", "south"));
        var paired = directed.WithRelation("north", "east", 2);
        Assert.Equal(2, paired.Get("north", "east"));
        Assert.Equal(2, paired.Get("east", "north"));
        Assert.Equal(-7, paired.Get("south", "east"));
        Assert.Equal(-3, paired.Get("east", "south"));
        Assert.True(paired.IsStructurallyValid());
        Assert.False(paired.IsWellFormed());
        Assert.Throws<ArgumentOutOfRangeException>(() => before.WithDirectedRelation("missing", "east", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => before.WithDirectedRelation("east", "missing", 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_rebirth_roundtrips_twice_and_continues_using_the_requested_direction(bool preserve)
    {
        var state = Reborn(Before(), preserve);
        var save = new SaveGame(state.SchemaVersion, "rebirth", "Rebirth", state.ScenarioId,
            state.WorldId, state.RulesetId, state);
        var text = SaveManager.Serialize(save);
        var loaded = SaveManager.Load("rebirth.json", text, PersistenceTestbed.Toy.World, Rules);
        Assert.Equal(state, loaded.State);
        Assert.Equal(text, SaveManager.Serialize(loaded));
        var twice = SaveManager.Load("rebirth-again.json", SaveManager.Serialize(loaded), PersistenceTestbed.Toy.World, Rules);
        Assert.Equal(state, twice.State);
        var continued = RelationTransitions.BreakToPeace(twice.State, Rules, "east", "north");
        Assert.Equal(preserve ? Rules.Diplomacy.CooldownAfterBrokenTrade : 0, continued.Relations.Get("east", "north"));
        Assert.Equal(continued, RelationTransitions.BreakToPeace(state, Rules, "east", "north"));
        Assert.Equal(state, RelationTransitions.BreakToPeace(twice.State, Rules, "north", "east"));
    }

    [Fact]
    public void Save_validation_accepts_directionality_but_rejects_invalid_shape_duplicate_and_unresolved_ids()
    {
        var state = Reborn(Before(), true);
        GameDataValidation.Validate("state", state);
        var matrix = state.Relations;
        var bad = new[] {
            matrix with { Matrix = ValueList.From(matrix.Matrix.Take(2)) },
            matrix with { Matrix = ValueList.From(matrix.Matrix.Select((r,i) => i == 1 ? ValueList.Of(0) : r)) },
            matrix with { NationIds = ValueList.Of("north", "south", "north") }
        };
        foreach (var relations in bad)
            Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("bad", state with { Relations = relations }));
        Assert.Throws<UnresolvedReferenceException>(() => GameDataValidation.Validate("bad", state with {
            Relations = matrix with { NationIds = ValueList.Of("north", "south", "missing") } }));
    }

    [Theory]
    [InlineData("toy-ruleset", true)]
    [InlineData("classical-faithful", true)]
    [InlineData("improved", false)]
    public void Policy_is_explicit_boolean_and_omitted_legacy_documents_keep_symmetric_reset(string preset, bool expected)
    {
        var path = Path.Combine(TestPaths.DataRoot, "rulesets", preset + ".json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(expected, node["economy"]!["rebirthPreservesInboundRelations"]!.GetValue<bool>());
        Assert.Equal(expected, GameDataLoader.Load<Ruleset>(path, node.ToJsonString()).Economy.RebirthPreservesInboundRelations);
        node["economy"]!.AsObject().Remove("rebirthPreservesInboundRelations");
        var legacy = GameDataLoader.Load<Ruleset>(path, node.ToJsonString());
        Assert.False(legacy.Economy.RebirthPreservesInboundRelations);
        Assert.Equal(Reborn(Before(), false), Rebirth.Run(Before(), Rules with { Economy = legacy.Economy },
            Before().NationById("north")!, NullEventSink.Instance, new ScriptedRng(nextIntDraws: new[] { 0 })));
        node["economy"]!["rebirthPreservesInboundRelations"] = "true";
        Assert.Throws<MalformedGameDataException>(() => GameDataLoader.Load<Ruleset>(path, node.ToJsonString()));
    }
}
