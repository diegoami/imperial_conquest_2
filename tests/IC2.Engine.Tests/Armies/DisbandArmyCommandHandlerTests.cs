using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Core;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 3: disband is refused away
/// from an owned city; money moves to the treasury and supplies to that city, both conserved exactly.
/// Uses the toy scenario's real cities (<c>arx</c>, <c>portus</c> owned by <c>north</c>; <c>meridia</c>
/// owned by <c>south</c>) rather than fabricated ones.
/// </summary>
public sealed class DisbandArmyCommandHandlerTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("position")]
    [InlineData("link")]
    [InlineData("construction")]
    public void Invalid_carried_context_refuses_before_any_refund(string invalid)
    {
        var state = InitialState();
        var city = state.CityById("arx")!;
        var army = Army("cargo", NorthNationId, city.X + 1, city.Y, new[] { RegularUnit("Cargo") },
            money: 37, supplyTons: 23, coveredTileCode: null, aboardFleetId: "carrier");
        var fleet = new FleetState("carrier", invalid == "owner" ? SouthNationId : NorthNationId,
            invalid == "position" ? army.X + 1 : army.X, army.Y, 7, 10, 100, 80, 60,
            invalid == "construction" ? 1 : null, null, invalid == "link" ? null : army.Id, 0);
        state = state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet) };
        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, army.Id));
        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.InvalidCarrier, result.Code);
        Assert.Same(state, result.State);
        Assert.Equal(state.RandomSeed, result.State.RandomSeed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Carried_army_disband_refunds_at_an_own_town_and_safely_unlinks_the_carrier(bool near)
    {
        var state = InitialState();
        var city = state.CityById("arx")! with { SupplyTons = 330 };
        var army = Army("cargo", NorthNationId, near ? city.X + 1 : 0, near ? city.Y : 5,
            new[] { RegularUnit("Cargo", troops: 5000) }, money: 37, supplyTons: 23,
            coveredTileCode: null, aboardFleetId: "carrier");
        var fleet = new FleetState("carrier", NorthNationId, army.X, army.Y, 7, 10, 100, 80, 60, null, null, army.Id, 0);
        state = state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? city : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == NorthNationId ? n with { Treasury = 1730 } : n)) };
        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, army.Id));
        if (!near)
        {
            Assert.True(result.IsRejected);
            Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
            Assert.Same(state, result.State);
            return;
        }
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById(army.Id));
        Assert.Equal(fleet with { CarriedArmyId = null }, result.State.FleetById(fleet.Id));
        Assert.Equal(city with { SupplyTons = 353 }, result.State.CityById(city.Id));
        Assert.Equal(1767, result.State.NationById(NorthNationId)!.Treasury);
        var save = new SaveGame(result.State.SchemaVersion, "cargo-disband", "cargo-disband",
            result.State.ScenarioId, result.State.WorldId, result.State.RulesetId, result.State);
        Assert.Equal(result.State, SaveManager.Load("cargo-disband.json", SaveManager.Serialize(save),
            CoreTestbed.Toy.World, ArmiesTestbed.Ruleset).State);
    }

    [Fact]
    public void Disband_AtAnOwnedCity_ConservesMoneyAndSuppliesExactly()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!; // (2, 1), owned by north.
        var army = Army("disband-a", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") }, money: 90, supplyTons: 30);
        state = WithArmies(state, army);

        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;
        var citySupplyBefore = arx.SupplyTons;

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-a"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("disband-a"));
        Assert.Equal(treasuryBefore + 90, result.State.NationById(NorthNationId)!.Treasury);
        Assert.Equal(citySupplyBefore + 30, result.State.CityById("arx")!.SupplyTons);
    }

    /// <summary>
    /// #1343: with two own cities adjoining the army, the original's scan (FUN_004494e4: x offset outer,
    /// y offset inner, last match kept) gives the supplies to the larger x offset, then the larger y offset,
    /// whatever the cities' list order.
    /// </summary>
    [Theory]
    [InlineData(3, 2)] // portus at dx = +1 beats arx at dx = 0.
    [InlineData(2, 3)] // same dx = 0: portus at dy = +1 beats arx at dy = -1.
    public void Disband_BetweenTwoOwnCities_SuppliesTheLastInTheOriginalsScan(int portusX, int portusY)
    {
        var state = InitialState();
        state = state with
        {
            Cities = IC2.Engine.Model.ValueList.From(state.Cities.Select(c => c.Id == "portus" ? c with { X = portusX, Y = portusY } : c)),
        };
        var arx = state.CityById("arx")!; // (2, 1), first in list order.
        Assert.Equal((2, 1), (arx.X, arx.Y));
        var army = Army("disband-two", NorthNationId, 2, 2, new[] { RegularUnit("a") }, money: 0, supplyTons: 30);
        state = WithArmies(state, army);
        var portusBefore = state.CityById("portus")!.SupplyTons;

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-two"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(portusBefore + 30, result.State.CityById("portus")!.SupplyTons);
        Assert.Equal(arx.SupplyTons, result.State.CityById("arx")!.SupplyTons);
    }

    [Fact]
    public void Disband_AwayFromAnyCity_IsRejected()
    {
        var state = InitialState();
        var army = Army("disband-b", NorthNationId, 99, 99, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-b"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
        Assert.NotNull(result.State.ArmyById("disband-b")); // nothing changed on rejection.
    }

    [Fact]
    public void Disband_AtAForeignCity_IsRejected()
    {
        var state = InitialState();
        var meridia = state.CityById("meridia")!; // (3, 4), owned by south.
        var army = Army("disband-c", NorthNationId, meridia.X, meridia.Y, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-c"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
    }

    [Fact]
    public void Disband_CarriedArmyWithMissingCarrier_IsRejectedAtomically()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var army = Army("disband-d", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") },
            coveredTileCode: null, aboardFleetId: "some-fleet");
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-d"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.InvalidCarrier, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Disband_NotYourArmy_IsRejected()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var army = Army("disband-e", SouthNationId, arx.X, arx.Y, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-e"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotYourArmy, result.Code);
    }

    [Fact]
    public void Disband_UnknownArmy_IsRejected()
    {
        var result = Dispatcher().Dispatch(InitialState(), new DisbandArmyCommand(NorthNationId, "no-such-army"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.UnknownArmy, result.Code);
    }

    /// <summary>
    /// The two-entity probe (<c>docs/build-process.md</c> §4.2 gate 5): a second army at the very same
    /// city must survive a disband completely untouched — an over-broad clear would fail this, not a
    /// single-entity fixture.
    /// </summary>
    [Fact]
    public void Disband_ASecondArmyAtTheSameCity_IsUntouched()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var toDisband = Army("disband-f", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") }, money: 20, supplyTons: 5);
        var bystander = Army("disband-bystander", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("z") }, money: 60, supplyTons: 8);
        state = WithArmies(state, toDisband, bystander);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-f"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(bystander, result.State.ArmyById("disband-bystander"));
    }
}
