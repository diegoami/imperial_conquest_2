using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// T146 Done-when 2: a world's optional <see cref="NationDefinition.LeaderNames"/> must be a drawable
/// 12-name pool — exactly 12 non-empty names, at least two distinct, with <c>leaderName</c> one of them.
/// A world without a pool validates exactly as before.
/// </summary>
public class LeaderPoolValidationTests
{
    private const string DocumentPath = "world-under-test.json (in-memory)";

    private static World ToyWorld() => GameDataLoader.LoadFile<World>(ModelTestPaths.ToyWorldFile);

    private static World WithNorthPool(ValueList<string>? pool) =>
        ToyWorld() with
        {
            Nations = ValueList.From(ToyWorld().Nations.Select(n =>
                n.Id == "north" ? n with { LeaderNames = pool } : n)),
        };

    private static ValueList<string> Names(params string[] names) => ValueList.Of(names);

    private static ValueList<string> TwelveDistinct() =>
        ValueList.Of(Enumerable.Range(0, 12).Select(i => $"pool-name-{i}").ToArray());

    [Fact]
    public void A_valid_pool_whose_first_name_is_the_leader_passes()
    {
        var leader = ToyWorld().NationById("north")!.LeaderName;
        var names = ValueList.Of(new[] { leader }.Concat(Enumerable.Range(0, 11).Select(i => $"pool-name-{i}")).ToArray());

        GameDataValidation.Validate(DocumentPath, WithNorthPool(names));
    }

    [Fact]
    public void Eleven_names_fails_naming_the_nation()
    {
        var names = ValueList.Of(Enumerable.Range(0, 11).Select(i => $"pool-name-{i}").ToArray());

        var ex = Assert.Throws<MalformedGameDataException>(() =>
            GameDataValidation.Validate(DocumentPath, WithNorthPool(names)));
        Assert.Contains("north", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_name_fails_naming_the_nation()
    {
        var names = TwelveDistinct();
        var withEmpty = ValueList.Of(names.Select((n, i) => i == 3 ? "" : n).ToArray());

        var ex = Assert.Throws<MalformedGameDataException>(() =>
            GameDataValidation.Validate(DocumentPath, WithNorthPool(withEmpty)));
        Assert.Contains("north", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Twelve_identical_names_fails_naming_the_nation()
    {
        var names = ValueList.Of(Enumerable.Repeat("same", 12).ToArray());

        var ex = Assert.Throws<MalformedGameDataException>(() =>
            GameDataValidation.Validate(DocumentPath, WithNorthPool(names)));
        Assert.Contains("north", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_leaderName_outside_the_pool_fails_naming_the_nation()
    {
        var leader = ToyWorld().NationById("north")!.LeaderName;
        var names = TwelveDistinct();
        Assert.DoesNotContain(leader, names);

        var ex = Assert.Throws<MalformedGameDataException>(() =>
            GameDataValidation.Validate(DocumentPath, WithNorthPool(names)));
        Assert.Contains("north", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped toy and example worlds carry no pool and validate unchanged — the optional member's
    /// whole point.
    /// </summary>
    [Theory]
    [InlineData("toy-3city.json")]
    [InlineData("example-tiny-duel.json")]
    public void A_world_without_a_pool_validates_unchanged(string fileName)
    {
        var path = Path.Combine(ModelTestPaths.DataRoot, "worlds", fileName);
        var world = GameDataLoader.LoadFile<World>(path);

        Assert.All(world.Nations, nation => Assert.Null(nation.LeaderNames));
        GameDataValidation.Validate(path, world);
    }
}
