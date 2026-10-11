using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Diplomacy;
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
    private static DiplomacyRelationRow RowFor(int relation, out GameState state)
    {
        var s = CoreTestbed.InitialState();
        state = s with { Relations = s.Relations.WithRelation("north", "south", relation) };
        return DiplomacyGridViewModel.BuildRows(state, CoreTestbed.Toy.Ruleset, "north")
            .Single(r => r.NationId == "south");
    }

    [Fact]
    public void Make_peace_is_enabled_for_trade_alliance_and_war()
    {
        var codes = CoreTestbed.Toy.Ruleset.Diplomacy.StateCodes;

        Assert.True(RowFor(codes.Trade, out _).CanMakePeace);
        Assert.True(RowFor(codes.Alliance, out _).CanMakePeace);
        Assert.True(RowFor(codes.War, out _).CanMakePeace);
    }

    [Fact]
    public void Make_peace_is_disabled_for_plain_peace_and_for_cooldown_values()
    {
        var codes = CoreTestbed.Toy.Ruleset.Diplomacy.StateCodes;

        Assert.False(RowFor(codes.Peace, out _).CanMakePeace);
        Assert.False(RowFor(-1, out _).CanMakePeace);
        Assert.False(RowFor(-8, out _).CanMakePeace);
        Assert.False(RowFor(-24, out _).CanMakePeace);
    }

    /// <summary>
    /// Bug 915: the row's Make Peace button must agree with the engine's own make-peace gate. Both seats are
    /// human so the engine's separate AI-refusal of a war peace never applies and acceptance is exactly the
    /// relation gate.
    /// </summary>
    [Fact]
    public void Make_peace_enabled_equals_the_engine_accepting_the_command_on_the_same_state()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var values = new[] { codes.Peace, codes.Trade, codes.Alliance, codes.War, -1, -3, -8, -24 };

        foreach (var value in values)
        {
            var state = DiplomacyTestbed.StateOf(
                DiplomacyTestbed.Nation("a", "A", control: SeatControl.Human),
                DiplomacyTestbed.Nation("b", "B", control: SeatControl.Human));
            state = state with { Relations = state.Relations.WithRelation("a", "b", value) };

            var row = DiplomacyGridViewModel.BuildRows(state, ruleset, "a").Single(r => r.NationId == "b");
            var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand("a", "b"));

            Assert.True(row.CanMakePeace == result.IsAccepted, $"relation value {value}");
        }
    }
}
