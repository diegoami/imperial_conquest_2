using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

public sealed class ArmyTransferPartialTests
{
    [Fact]
    public void Equal_valued_units_keep_distinct_original_identities_during_crossing_moves()
    {
        var unit = RegularUnit("Same");
        var a = Army("a", NorthNationId, 5, 5, new[] { unit });
        var b = Army("b", NorthNationId, 6, 5, new[] { unit });
        var state = WithArmies(InitialState(), a, b);
        var plan = ArmyTransferUnitAdmission.Plan(a, b, ArmiesTestbed.Ruleset, state, new[] {
            new ArmyTransferUnitStep(true, 0, true), new ArmyTransferUnitStep(false, 0, false) });
        Assert.False(Assert.Single(plan.Selected).SelectedOrigin);
        Assert.True(Assert.Single(plan.Partner).SelectedOrigin);
        Assert.Equal(2, plan.AcceptedSteps.Count);
        Assert.Empty(plan.NotMoved);
    }

    [Theory]
    [InlineData(false, 19, 2)]
    [InlineData(true, 19, 2)]
    [InlineData(false, 18, 3)]
    [InlineData(true, 18, 3)]
    public void A_partial_unit_cap_batch_keeps_highest_selected_units_and_leaves_refused_units(bool back, int receiving, int selected)
    {
        var donor = Army("donor", NorthNationId, 5, 5,
            Enumerable.Range(0, selected + 1).Select(i => RegularUnit($"D{i}", troops: 100)), moves: 7);
        var receiver = Army("receiver", NorthNationId, 6, 5,
            Enumerable.Range(0, receiving).Select(i => RegularUnit($"R{i}", troops: 100)), moves: 6);
        var state = WithArmies(InitialState(), donor, receiver);
        var result = Dispatcher().Dispatch(state, Batch(donor.Id, receiver.Id, Enumerable.Range(0, selected), back));
        Assert.True(result.IsAccepted, result.ToString());
        var after = result.State.ArmyById(receiver.Id)!;
        Assert.Equal(20, after.Units.Count);
        Assert.Equal(Enumerable.Range(1, selected - 1).Reverse().Select(i => $"D{i}"),
            after.Units.Skip(receiving).Select(u => u.Name));
        Assert.Equal(new[] { "D0", $"D{selected}" }, result.State.ArmyById(donor.Id)!.Units.Select(u => u.Name));
        Assert.Equal(7, result.State.ArmyById(donor.Id)!.Moves);
        Assert.Equal(0, after.Moves);
        Assert.Equal(state.Armies.Sum(a => a.TotalTroops), result.State.Armies.Sum(a => a.TotalTroops));
        var feedback = Assert.Single(result.Events.OfType<ArmyTransferUnitsResolved>());
        Assert.Equal(selected - 1, feedback.Moved);
        Assert.Equal(ArmyTransferRejections.CombinedUnitsTooLarge, Assert.Single(feedback.NotMoved).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_troop_cap_refusal_does_not_prevent_a_later_smaller_unit_from_fitting(bool back)
    {
        var donor = Army("donor", NorthNationId, 5, 5,
            new[] { 500, 1000, 2000, 1000, 100 }.Select((n, i) => RegularUnit($"D{i}", troops: n)));
        var receiver = Army("receiver", NorthNationId, 6, 5, new[] { RegularUnit("R", troops: 98000) });
        var state = WithArmies(InitialState(), donor, receiver);
        var result = Dispatcher().Dispatch(state, Batch(donor.Id, receiver.Id, Enumerable.Range(0, 4), back));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(new[] { "R", "D3", "D1" }, result.State.ArmyById(receiver.Id)!.Units.Select(u => u.Name));
        Assert.Equal(100000, result.State.ArmyById(receiver.Id)!.TotalTroops);
        Assert.Equal(new[] { "D0", "D2", "D4" }, result.State.ArmyById(donor.Id)!.Units.Select(u => u.Name));
        Assert.Equal(2, Assert.Single(result.Events.OfType<ArmyTransferUnitsResolved>()).NotMoved.Count);
    }

    [Fact]
    public void Mixed_unit_and_fleet_failures_report_both_classes_without_rolling_back_the_fitting_unit()
    {
        var donor = Army("donor", NorthNationId, 5, 5,
            new[] { 100, 100, 500, 100 }.Select((n, i) => RegularUnit($"D{i}", troops: n)));
        var receiver = Army("receiver", NorthNationId, 6, 5,
            Enumerable.Range(0, 18).Select(i => RegularUnit($"R{i}", troops: 100))
                .Append(RegularUnit("R18", troops: 13200)), aboardFleetId: "carrier", coveredTileCode: null);
        var fleet = new FleetState("carrier", NorthNationId, 6, 5, 7, 30, 100, 0, 0, null, null, receiver.Id, null);
        var state = WithArmies(InitialState(), donor, receiver) with { Fleets = ValueList.Of(fleet) };
        var result = Dispatcher().Dispatch(state, Batch(donor.Id, receiver.Id, new[] { 0, 1, 2 }));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal("D1", result.State.ArmyById(receiver.Id)!.Units[^1].Name);
        Assert.Equal(15100, result.State.ArmyById(receiver.Id)!.TotalTroops);
        Assert.Equal(fleet, result.State.FleetById(fleet.Id));
        var feedback = Assert.Single(result.Events.OfType<ArmyTransferUnitsResolved>());
        Assert.Equal(1, feedback.Moved);
        Assert.Equal(new[] { ArmyTransferRejections.FleetCannotCarry, ArmyTransferRejections.CombinedUnitsTooLarge },
            feedback.NotMoved.Select(f => f.Code));
        Assert.Equal(new[] { "This army already has 20 units.", "This fleet can not carry any more troops." },
            ArmyTransferUnitAdmission.RefusalMessages(feedback.NotMoved, ArmiesTestbed.Ruleset));
    }

    [Fact]
    public void Ordered_return_moves_preserve_append_order_and_zero_both_recipients_moves()
    {
        var a = Army("a", NorthNationId, 5, 5, new[] { RegularUnit("A0"), RegularUnit("A1") }, moves: 7);
        var b = Army("b", NorthNationId, 6, 5, new[] { RegularUnit("B0") }, moves: 6);
        var command = Batch(a.Id, b.Id, Array.Empty<int>()) with { UnitSteps = ValueList.Of(
            new ArmyTransferUnitStep(true, 0, true), new ArmyTransferUnitStep(true, 0, false)) };
        var result = Dispatcher().Dispatch(WithArmies(InitialState(), a, b), command);
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(new[] { "A1", "A0" }, result.State.ArmyById(a.Id)!.Units.Select(u => u.Name));
        Assert.Equal(0, result.State.ArmyById(a.Id)!.Moves);
        Assert.Equal(0, result.State.ArmyById(b.Id)!.Moves);
    }

    private static ArmyTransferCommand Batch(string donor, string receiver, IEnumerable<int> indexes, bool back = false) =>
        back ? new(NorthNationId, receiver, donor, ValueList<int>.Empty, 0, 0, ValueList.From(indexes), 0, 0)
             : new(NorthNationId, donor, receiver, ValueList.From(indexes), 0, 0, ValueList<int>.Empty, 0, 0);
}
