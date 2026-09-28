using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/game-design.md</c> §"User interface" item 4's own grid, built from the real toy scenario
/// (<see cref="CoreTestbed"/>) rather than a hand-rolled <see cref="GameState"/> — the same fixture every
/// other Core test in this project already shares.
/// </summary>
public sealed class DiplomacyGridViewModelTests
{
    [Fact]
    public void BuildRows_excludes_the_active_nation_and_lists_every_other_live_nation()
    {
        var state = CoreTestbed.InitialState();

        var rows = DiplomacyGridViewModel.BuildRows(state, CoreTestbed.Toy.Ruleset, "north");

        Assert.DoesNotContain(rows, r => r.NationId == "north");
        Assert.Contains(rows, r => r.NationId == "south");
    }

    [Fact]
    public void A_fresh_scenario_starts_every_pair_at_peace_with_every_action_but_declare_war_available()
    {
        var state = CoreTestbed.InitialState();

        var row = DiplomacyGridViewModel.BuildRows(state, CoreTestbed.Toy.Ruleset, "north")
            .Single(r => r.NationId == "south");

        Assert.Equal("Peace", row.RelationLabel);
        Assert.True(row.CanDeclareWar);
        Assert.False(row.CanMakePeace);
        Assert.True(row.CanProposeAlliance);
        Assert.True(row.CanProposeTrade);
    }

    [Fact]
    public void A_nation_at_war_can_make_peace_but_not_declare_war_ally_or_trade()
    {
        var state = CoreTestbed.InitialState();
        var codes = CoreTestbed.Toy.Ruleset.Diplomacy.StateCodes;
        var atWar = state with { Relations = state.Relations.WithRelation("north", "south", codes.War) };

        var row = DiplomacyGridViewModel.BuildRows(atWar, CoreTestbed.Toy.Ruleset, "north")
            .Single(r => r.NationId == "south");

        Assert.Equal("War", row.RelationLabel);
        Assert.False(row.CanDeclareWar);
        Assert.True(row.CanMakePeace);
        Assert.False(row.CanProposeAlliance);
        Assert.False(row.CanProposeTrade);
    }

    [Fact]
    public void An_eliminated_nation_gets_no_row()
    {
        var state = CoreTestbed.InitialState();
        var eliminated = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                n.Id == "south" ? n with { Eliminated = true } : n)),
        };

        var rows = DiplomacyGridViewModel.BuildRows(eliminated, CoreTestbed.Toy.Ruleset, "north");

        Assert.DoesNotContain(rows, r => r.NationId == "south");
    }

    [Fact]
    public void A_negative_cooldown_cell_is_worded_as_a_cooldown_not_matched_against_any_state_code()
    {
        var state = CoreTestbed.InitialState();
        var cooldown = state with { Relations = state.Relations.WithRelation("north", "south", -3) };

        var row = DiplomacyGridViewModel.BuildRows(cooldown, CoreTestbed.Toy.Ruleset, "north")
            .Single(r => r.NationId == "south");

        Assert.Equal("Cooldown (3)", row.RelationLabel);
    }
}
