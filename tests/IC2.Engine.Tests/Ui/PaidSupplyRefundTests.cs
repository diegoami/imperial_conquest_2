using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

public sealed class PaidSupplyRefundTests
{
    [Theory]
    [InlineData(false, -105, 330, -105)]
    [InlineData(false, -5, 330, -5)]
    [InlineData(false, -4, 330, -4)]
    [InlineData(false, -1, 330, -1)]
    [InlineData(false, 0, 330, 0)]
    [InlineData(false, 51, 330, 51)]
    [InlineData(false, -105, 0, -105)]
    [InlineData(true, -105, 330, -105)]
    [InlineData(true, -5, 330, -5)]
    [InlineData(true, -4, 330, -4)]
    [InlineData(true, -1, 330, -1)]
    [InlineData(true, 0, 330, 0)]
    [InlineData(true, 51, 330, 51)]
    [InlineData(true, -105, 0, -105)]
    public void Paid_staging_and_real_buy_preserve_signed_returns_and_foreign_owner_accounting(
        bool fleet, int room, int stock, int expected)
    {
        var rules = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").Ruleset;
        var initial = CoreTestbed.InitialState();
        var army = initial.Armies.First(a => a.Nation == "north") with {
            X = 3, Y = 2, SupplyTons = 101 - room, Money = 100, AboardFleetId = null,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")) };
        var carrier = initial.Fleets.First(f => f.Nation == "north") with {
            X = 3, Y = 2, Ships = 20, SupplyTons = 160 - room, Money = 100,
            CarriedArmyId = null, ConstructionTicksRemaining = null };
        var city = initial.Cities.First(c => c.Owner == "south") with { X = 2, Y = 2, SupplyTons = stock };
        var state = initial with { RulesetId = rules.Id, Armies = ValueList.Of(army), Fleets = ValueList.Of(carrier),
            Cities = ValueList.Of(city), Relations = initial.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.Peace)
                .WithRelation("south", "north", rules.Diplomacy.StateCodes.Peace) };
        var scenario = CoreTestbed.Toy.Scenario with { RulesetId = rules.Id };
        var save = new SaveGame(state.SchemaVersion, "paid-refund", "paid-refund",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(CoreTestbed.Toy.World, rules, scenario, save);
        var model = fleet ? SupplyDialogModel.ForFleet(state, carrier, rules) : SupplyDialogModel.ForArmy(state, army, rules);
        var provider = model.Providers.ToList().FindIndex(p => p.Id == city.Id);
        Assert.True(provider >= 0);
        model.SelectProvider(provider);
        Assert.Null(model.PressSupply(100));
        Assert.Equal(expected, model.StagedTons);
        Assert.Equal(expected / 5, model.StagedCostTalents);
        Assert.Equal(state, session.State); // Staging changes no live resource.
        var command = model.TransferStaged();
        if (expected == 0) { Assert.Null(command); return; }
        Assert.NotNull(command);
        Assert.EndsWith($" {Math.Max(1, expected)}", command);
        var output = session.Submit(command);
        Assert.DoesNotContain(output.Lines, l => l.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.Equal(stock - expected, session.State.CityById(city.Id)!.SupplyTons);
        Assert.Equal(state.NationById("north")!.Treasury, session.State.NationById("north")!.Treasury);
        Assert.Equal(state.NationById("south")!.Treasury + expected / 5, session.State.NationById("south")!.Treasury);
        if (fleet)
        {
            var after = session.State.FleetById(carrier.Id)!;
            Assert.Equal(carrier with { SupplyTons = carrier.SupplyTons + expected, Money = 100 - expected / 5 }, after);
            model.Refresh(session.State, after);
        }
        else
        {
            var after = session.State.ArmyById(army.Id)!;
            Assert.Equal(army with { SupplyTons = army.SupplyTons + expected, Money = 100 - expected / 5 }, after);
            model.Refresh(session.State, after);
        }
        Assert.Equal(0, model.StagedTons);
        var afterPurchase = session.State;
        Assert.Null(model.Close());
        Assert.Equal(afterPurchase, session.State); // Closing does not roll back an applied purchase.
    }
}
