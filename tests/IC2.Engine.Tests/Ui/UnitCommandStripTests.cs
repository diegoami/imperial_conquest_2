using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T112 Done-when 2: the Unit-map command strip's buttons for each selection — the selected unit's group
/// (fleet carrying an army shows both, fleet buttons first) plus Cancel selection, and nothing for a
/// foreign unit or no selection <c>[derived: code; original-ui-command-audit.md §3.3]</c>.
/// </summary>
public sealed class UnitCommandStripTests
{
    [Fact]
    public void An_own_army_shows_the_seven_army_buttons_then_cancel()
    {
        var commands = UnitCommandStripLayout.CommandsFor(UnitStripSelection.OwnArmy);

        Assert.Equal(8, commands.Count);
        Assert.Equal(UnitCommandStripLayout.ArmyCommands, commands.Take(7).ToArray());
        Assert.Equal(UnitCommandStripLayout.CancelCommandId, commands[^1]);
    }

    [Fact]
    public void An_own_fleet_shows_the_six_fleet_buttons_then_cancel()
    {
        var commands = UnitCommandStripLayout.CommandsFor(UnitStripSelection.OwnFleet);

        Assert.Equal(7, commands.Count);
        Assert.Equal(UnitCommandStripLayout.FleetCommands, commands.Take(6).ToArray());
        Assert.Equal(UnitCommandStripLayout.CancelCommandId, commands[^1]);
    }

    [Fact]
    public void An_own_city_shows_the_one_city_button_then_cancel()
    {
        var commands = UnitCommandStripLayout.CommandsFor(UnitStripSelection.OwnCity);

        Assert.Equal(2, commands.Count);
        Assert.Equal(UnitCommandStripLayout.CityCommands, commands.Take(1).ToArray());
        Assert.Equal(UnitCommandStripLayout.CancelCommandId, commands[^1]);
    }

    [Fact]
    public void A_fleet_carrying_an_army_shows_the_fleet_buttons_then_the_army_buttons_then_cancel()
    {
        var commands = UnitCommandStripLayout.CommandsFor(UnitStripSelection.OwnFleetCarryingArmy);

        Assert.Equal(14, commands.Count);
        Assert.Equal(UnitCommandStripLayout.FleetCommands, commands.Take(6).ToArray());
        Assert.Equal(UnitCommandStripLayout.ArmyCommands, commands.Skip(6).Take(7).ToArray());
        Assert.Equal(UnitCommandStripLayout.CancelCommandId, commands[^1]);
    }

    [Fact]
    public void A_foreign_unit_or_no_selection_shows_nothing()
    {
        Assert.Empty(UnitCommandStripLayout.CommandsFor(UnitStripSelection.None));
    }

    [Fact]
    public void Every_button_is_a_table_row_and_cancel_is_the_original_fifteenth()
    {
        Assert.All(
            UnitCommandStripLayout.ArmyCommands
                .Concat(UnitCommandStripLayout.FleetCommands)
                .Concat(UnitCommandStripLayout.CityCommands),
            id => Assert.NotNull(GameCommandTable.RowById(id)));
        Assert.NotNull(GameCommandTable.RowById(UnitCommandStripLayout.CancelCommandId));

        // 7 + 6 + 1 = 14, plus Cancel selection = the 15 buttons AllButtonsOff loops over.
        Assert.Equal(
            15,
            UnitCommandStripLayout.ArmyCommands.Count + UnitCommandStripLayout.FleetCommands.Count
            + UnitCommandStripLayout.CityCommands.Count + 1);
    }
}
