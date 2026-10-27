using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class FleetTransferPurseTests
{
    [Fact]
    public void Explicit_money_allocation_keeps_the_owner_no_wrap_field_bound_without_treasury_credit()
    {
        var state = CoreTestbed.InitialState();
        var source = state.Fleets.First(f => f.Nation == state.ActiveNationId) with {
            Id = "bound-a", X = 3, Y = 3, Ships = 10, Money = 100, CarriedArmyId = null, ConstructionTicksRemaining = null };
        var target = source with { Id = "bound-b", X = 4, Money = PurseAccounting.PurseFieldMax - 5 };
        state = state with { Fleets = ValueList.Of(source, target) };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(state.ActiveNationId, source.Id, target.Id, 1, 0, 100));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(PurseAccounting.PurseFieldMax, result.State.FleetById(target.Id)!.Money);
        Assert.Equal(0, result.State.FleetById(source.Id)!.Money);
        Assert.Equal(state.Nations, result.State.Nations);
    }

    [Theory]
    [InlineData(false, 950, 50, 150)]
    [InlineData(false, 1000, 0, 200)]
    [InlineData(false, 1100, -100, 300)]
    [InlineData(true, 950, -50, 150)]
    [InlineData(true, 1000, 0, 200)]
    [InlineData(true, 1100, 100, 300)]
    public void Money_arrows_clamp_working_purses_and_never_sweep_to_treasury(bool back, int receiverMoney, int expectedNet, int expectedDonor)
    {
        var original = new GameSession(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario);
        var template = original.State.Fleets.First(f => f.Nation == original.State.ActiveNationId);
        var a = template with { Id = "money-a", X = 3, Y = 3, Ships = 10, Money = back ? receiverMoney : 200,
            CarriedArmyId = null, ConstructionTicksRemaining = null };
        var b = a with { Id = "money-b", X = 4, Money = back ? 200 : receiverMoney };
        var state = original.State with { Fleets = ValueList.Of(a, b) };
        var save = new SaveGame(state.SchemaVersion, "fleet-purse", "fleet-purse", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(original.World, original.Ruleset, original.Scenario, save);
        var model = FleetTransferModel.ForFleets(a, b, original.Ruleset);
        model.AdjustMoney(back ? -100 : 100);
        Assert.Equal(expectedNet, model.MoneyNet);
        model.AdjustMoney(back ? -100 : 100);
        Assert.Equal(expectedNet, model.MoneyNet);
        Assert.Equal(state, session.State);
        Assert.Empty(model.Cancel());
        var commands = model.ComposeOk();
        if (expectedNet == 0) Assert.Empty(commands); else Assert.Single(commands);
        foreach (var command in commands)
            Assert.DoesNotContain(session.Submit(command).Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(expectedDonor, session.State.FleetById(back ? b.Id : a.Id)!.Money);
        Assert.Equal(1000, session.State.FleetById(back ? a.Id : b.Id)!.Money);
        Assert.Equal(state.Nations, session.State.Nations);
        Assert.Equal(a.Money + b.Money, session.State.Fleets.Sum(f => f.Money));
        Assert.Equal(a.Moves, session.State.FleetById(a.Id)!.Moves);
        Assert.Equal(b.Moves, session.State.FleetById(b.Id)!.Moves);
    }

    [Theory]
    [InlineData(950)]
    [InlineData(1000)]
    [InlineData(1100)]
    public void Ship_only_partial_transfer_does_not_normalize_existing_purses(int money)
    {
        var state = CoreTestbed.InitialState();
        var source = state.Fleets.First(f => f.Nation == state.ActiveNationId) with {
            Id = "ship-a", X = 3, Y = 3, Ships = 10, Money = 200, CarriedArmyId = null, ConstructionTicksRemaining = null };
        var target = source with { Id = "ship-b", X = 4, Money = money };
        state = state with { Fleets = ValueList.Of(source, target) };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(state.ActiveNationId, source.Id, target.Id, 1, 0, 0));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(source with { Ships = 9 }, result.State.FleetById(source.Id));
        Assert.Equal(target with { Ships = 11 }, result.State.FleetById(target.Id));
        Assert.Equal(state.Nations, result.State.Nations);
    }
}
