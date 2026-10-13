using IC2.Engine.Battle.Tactical;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T127.md</c> Done-when 1: the battle screen's model, on views T125's session builds from scripted
/// battles. Every cell's key and tint side, the caption of each phase, each panel line, the morale tier names at
/// their edges and the clicks' orders equal the engine's values.
/// </summary>
public sealed class TacticalBattleViewModelTests
{
    private static (GameSession Session, TacticalBattleView View, BattleDisplay Display) Opened()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        var output = session.Submit("attack-army north-army-1 south-army-1");
        var view = Assert.IsType<TacticalBattleView>(output.OpenBattleView);
        return (session, view, BattleDisplay.For(view, session.State, session.Ruleset));
    }

    [Fact]
    public void Every_cell_carries_the_engines_icon_code_the_key_of_its_type_and_size_and_its_tint_side()
    {
        var (_, view, display) = Opened();
        var model = TacticalBattleViewModel.From(view, display);

        Assert.Equal(display.BoardWidth * display.BoardHeight, model.Cells.Count);
        foreach (var cell in model.Cells)
        {
            var code = view.Grid[(cell.X * display.BoardHeight) + cell.Y];
            Assert.Equal(code, cell.Code);
            if (code == TacticalIcon.Empty)
            {
                Assert.Null(cell.IconKey);
                Assert.Equal(-1, cell.TintSide);
                Assert.Equal(-1, cell.Slot);
                Assert.Null(model.TintNationId(cell));
                continue;
            }

            var side = code >= TacticalIcon.DefenderOffset ? 1 : 0;
            var type = (code % TacticalIcon.DefenderOffset) / TacticalIcon.SizesPerType;
            var size = code % TacticalIcon.SizesPerType;
            Assert.Equal(BattleAssetKeys.UnitIcon(view.UnitTypeIds[type], size), cell.IconKey);
            Assert.Equal(side, cell.TintSide);
            Assert.Equal(side == 0 ? view.AttackerNationId : view.DefenderNationId, model.TintNationId(cell));

            // The slot standing there is the live slot of that side at that position, and its type matches the code.
            Assert.True(cell.Slot >= 0, $"cell ({cell.X},{cell.Y}) has a unit but no slot");
            Assert.Equal(side, model.SideOf(cell.Slot));
            Assert.Equal(type, view.Slots[cell.Slot].Type);
        }

        Assert.Equal(view.Slots.Count(s => s.IsLive), model.Cells.Count(c => c.IconKey is not null));
    }

    [Fact]
    public void The_selected_cell_carries_the_cursor()
    {
        var (_, view, display) = Opened();
        var model = TacticalBattleViewModel.From(view, display, selectedSlot: 1);

        var cursor = Assert.Single(model.Cells, c => c.Selected);
        Assert.Equal(1, cursor.Slot);
        Assert.Equal(view.Slots[1].X, cursor.X);
        Assert.Equal(view.Slots[1].Y, cursor.Y);
        Assert.DoesNotContain(TacticalBattleViewModel.From(view, display).Cells, c => c.Selected);
    }

    [Fact]
    public void Two_live_units_in_one_cell_resolve_to_the_last_slot_as_the_engine_does()
    {
        var (_, view, display) = Opened();
        var live = Enumerable.Range(0, view.Slots.Count).Where(s => view.Slots[s].IsLive).ToList();
        var first = live[0];
        var last = live[^1];
        Assert.NotEqual(first, last);

        // Put the last live slot on the first's cell: both are live there, and the later slot wins.
        var slots = view.Slots.ToList();
        slots[last] = slots[last] with { X = slots[first].X, Y = slots[first].Y };
        var shared = view with { Slots = new IC2.Engine.Model.ValueList<TacticalSlot>(slots) };
        var model = TacticalBattleViewModel.From(shared, display);

        Assert.Equal(last, model.SlotAt(slots[first].X, slots[first].Y));
    }

    [Fact]
    public void The_caption_names_both_sides_and_the_side_to_move_in_each_phase()
    {
        var (session, view, display) = Opened();
        var north = session.State.NationById("north")!.Name;
        var south = session.State.NationById("south")!.Name;

        Assert.False(view.Placed);
        Assert.Equal($"{north}  v  {south}          {north} to place units.", TacticalBattleViewModel.From(view, display).Caption);

        // Placement ends with the human's End turn; the movement half-rounds then name the nation to move.
        var after = session.Submit("battle end").OpenBattleView;
        var movement = Assert.IsType<TacticalBattleView>(after);
        Assert.True(movement.Placed);
        var mover = movement.SideToMove == 0 ? north : south;
        Assert.Equal($"{north}  v  {south}          {mover} to move units.", TacticalBattleViewModel.From(movement, display).Caption);
    }

    [Fact]
    public void The_toolbar_has_the_originals_eight_buttons_with_their_tooltips_and_glyphs()
    {
        var (_, view, display) = Opened();
        var buttons = TacticalBattleViewModel.From(view, display).Buttons;

        Assert.Equal(
            new[] { "Unit moves", "Friendly units", "Enemy units", "Cancel selection", "End turn", "Change pauses", "Computer general on", "Surrender" },
            buttons.Select(b => b.Tooltip).ToArray());
        Assert.Equal(BattleAssetKeys.ToolbarButtons.ToArray(), buttons.Select(b => b.Id).ToArray());
        Assert.Equal(BattleAssetKeys.ToolbarButtons.Select(BattleAssetKeys.ToolbarGlyph).ToArray(), buttons.Select(b => b.GlyphKey).ToArray());
        Assert.All(buttons, b => Assert.True(b.Enabled));
        Assert.All(TacticalBattleViewModel.From(view, display, controlsEnabled: false).Buttons, b => Assert.False(b.Enabled));
    }

    [Theory]
    [InlineData(0, "very low")]
    [InlineData(26, "very low")]
    [InlineData(27, "very low")]
    [InlineData(34, "very low")]
    [InlineData(35, "low")]
    [InlineData(42, "low")]
    [InlineData(43, "normal")]
    [InlineData(50, "normal")]
    [InlineData(51, "high")]
    [InlineData(58, "high")]
    [InlineData(59, "very high")]
    [InlineData(66, "very high")]
    [InlineData(67, "excellent")]
    [InlineData(74, "excellent")]
    [InlineData(90, "excellent")]
    public void The_morale_tier_is_morale_minus_27_div_8_on_the_six_words(int morale, string expected)
    {
        Assert.Equal(expected, TacticalBattleViewModel.MoraleTier(morale));
    }

    [Fact]
    public void The_panel_shows_an_own_units_full_details_and_an_enemys_public_ones()
    {
        var (session, view, display) = Opened();
        var own = view.Slots[0];
        var enemy = view.Slots[20];
        var north = session.State.NationById("north")!.Name;
        var south = session.State.NationById("south")!.Name;

        var ownLines = TacticalBattleViewModel.From(view, display, selectedSlot: 0).PanelLines;
        Assert.Equal(
            new[]
            {
                north,
                own.Name,
                display.UnitTypeNames[own.Type],
                $"{own.Troops} troops",
                $"Quality: {IC2.Slice.UI.ArmyDialogModels.QualityCaption(own.Quality)}",
                $"Morale: {TacticalBattleViewModel.MoraleTier(own.Morale)}",
                $"Moves: {own.Moves}",
                $"Shots: {own.Shots}",
                "Attacks: nobody",
            },
            ownLines.ToArray());

        var enemyLines = TacticalBattleViewModel.From(view, display, selectedSlot: 20).PanelLines;
        Assert.Equal(new[] { south, enemy.Name, display.UnitTypeNames[enemy.Type], $"{enemy.Troops} troops" }, enemyLines.ToArray());
        Assert.Empty(TacticalBattleViewModel.From(view, display).PanelLines);
    }

    [Fact]
    public void A_mercenary_unit_is_named_as_one()
    {
        var (_, view, display) = Opened();
        var slots = view.Slots.ToList();
        slots[0] = slots[0] with { Origin = 3 };
        var changed = view with { Slots = IC2.Engine.Model.ValueList.From(slots) };

        var lines = TacticalBattleViewModel.From(changed, display, selectedSlot: 0).PanelLines;

        Assert.Equal(view.Slots[0].Name + " mercenaries", lines[1]);
    }

    [Fact]
    public void A_melee_target_is_named_in_the_panel_and_in_the_unit_moves_row()
    {
        var (_, view, display) = Opened();
        var slots = view.Slots.ToList();
        slots[0] = slots[0] with { Target = 20 };
        var changed = view with { Slots = IC2.Engine.Model.ValueList.From(slots) };

        var model = TacticalBattleViewModel.From(changed, display, selectedSlot: 0);

        Assert.Equal($"Attacks: {view.Slots[20].Name}", model.PanelLines[^1]);
        Assert.Equal(view.Slots[20].Name, model.UnitMovesRows.Single(r => r.Slot == 0).Cells[4]);
    }

    [Fact]
    public void The_three_unit_lists_hold_the_live_units_with_the_originals_columns()
    {
        var (_, view, display) = Opened();
        var model = TacticalBattleViewModel.From(view, display);
        var ownLive = Enumerable.Range(0, 20).Where(s => view.Slots[s].IsLive).ToList();
        var enemyLive = Enumerable.Range(20, 20).Where(s => view.Slots[s].IsLive).ToList();

        Assert.Equal(ownLive, model.UnitMovesRows.Select(r => r.Slot).ToList());
        Assert.Equal(ownLive, model.FriendlyRows.Select(r => r.Slot).ToList());
        Assert.Equal(enemyLive, model.EnemyRows.Select(r => r.Slot).ToList());

        var s0 = view.Slots[0];
        Assert.Equal(
            new[] { s0.Name, display.UnitTypeNames[s0.Type], s0.Moves.ToString(), s0.Shots.ToString(), "-" },
            model.UnitMovesRows[0].Cells.ToArray());
        Assert.Equal(
            new[]
            {
                s0.Name, display.UnitTypeNames[s0.Type], s0.Troops.ToString(),
                IC2.Slice.UI.ArmyDialogModels.QualityCaption(s0.Quality), TacticalBattleViewModel.MoraleTier(s0.Morale),
            },
            model.FriendlyRows[0].Cells.ToArray());
        var e0 = view.Slots[enemyLive[0]];
        Assert.Equal(new[] { e0.Name, display.UnitTypeNames[e0.Type], e0.Troops.ToString() }, model.EnemyRows[0].Cells.ToArray());
    }

    [Fact]
    public void Clicks_become_the_originals_mouse_orders()
    {
        var (session, view, display) = Opened();
        var own = view.Slots[1];
        var enemy = view.Slots[20];
        var none = TacticalBattleViewModel.From(view, display);
        var selected = TacticalBattleViewModel.From(view, display, selectedSlot: 1);

        // Left click: an own unit selects, an empty cell places (placement) or moves (movement), an enemy sets a target.
        Assert.Equal(new BattleClick(BattleClickKind.Select, 1, own.X, own.Y, -1), none.ForLeftClick(own.X, own.Y));
        Assert.Equal(BattleClickKind.None, none.ForLeftClick(0, 5).Kind);
        Assert.Equal(new BattleClick(BattleClickKind.Place, 1, 3, 1, -1), selected.ForLeftClick(3, 1));
        Assert.Equal(new BattleClick(BattleClickKind.Attack, 1, enemy.X, enemy.Y, 20), selected.ForLeftClick(enemy.X, enemy.Y));

        // Right click: an enemy is shot at by the selected unit, an own unit's target is cleared, ground does nothing.
        Assert.Equal(new BattleClick(BattleClickKind.Shoot, 1, enemy.X, enemy.Y, 20), selected.ForRightClick(enemy.X, enemy.Y));
        Assert.Equal(BattleClickKind.None, none.ForRightClick(enemy.X, enemy.Y).Kind);
        Assert.Equal(new BattleClick(BattleClickKind.Clear, 1, own.X, own.Y, -1), selected.ForRightClick(own.X, own.Y));
        Assert.Equal(BattleClickKind.None, selected.ForRightClick(0, 5).Kind);

        var movement = Assert.IsType<TacticalBattleView>(session.Submit("battle end").OpenBattleView);
        var moving = TacticalBattleViewModel.From(movement, display, selectedSlot: 1);
        Assert.Equal(BattleClickKind.Move, moving.ForLeftClick(5, 5).Kind);
    }

    [Fact]
    public void Exchanges_are_read_from_the_engines_lines_with_the_names_of_the_view_before_the_order()
    {
        var (_, view, _) = Opened();
        var shooter = view.Slots[1].Name;
        var target = view.Slots[20].Name;

        var shots = TacticalBattleViewModel.ParseExchanges(
            new[]
            {
                "> battle shoot 1 20",
                "Unit 1 shoots unit 20: 30 troops lost, 570 left, morale 40.",
                "Unit 1 shoots unit 20: 600 troops lost, 0 left, morale 10.",
            },
            view);
        Assert.Equal(2, shots.Count);
        Assert.Equal(new[] { $"{shooter} SHOOTS AT {target}", "TROOP LOSSES 30" }, shots[0].Lines.ToArray());
        Assert.Equal(new[] { $"{shooter} SHOOTS AT {target}", "Unit destroyed !" }, shots[1].Lines.ToArray());

        var melee = TacticalBattleViewModel.ParseExchanges(
            new[]
            {
                "Unit 0 fights unit 20: attacker loses 5 (95 left, morale 40), defender loses 100 (0 left, morale 10).",
                "Unit 20 routs.",
                "Unit 0 fights unit 21: attacker loses 7 (88 left, morale 38), defender loses 9 (50 left, morale 30).",
            },
            view);
        Assert.Equal(2, melee.Count);
        Assert.Equal(
            new[] { $"{view.Slots[0].Name} ATTACKS {target}", "UNIT  LOSSES", $"{view.Slots[0].Name}: 5", $"{target}: routed" },
            melee[0].Lines.ToArray());
        Assert.False(melee[1].TargetRouted);
        Assert.Equal(9, melee[1].TargetLoss);
        Assert.Empty(TacticalBattleViewModel.ParseExchanges(new[] { "Unit 1 placed at (1,2)." }, view));
    }

    [Fact]
    public void The_exchanges_of_a_played_battle_match_the_troops_the_engine_took()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        var shots = 0;
        var melees = 0;
        TacticalBattleView? before = null;
        foreach (var line in TacticalSessionTestbed.WonBattleScript)
        {
            var output = session.Submit(line);
            if (before is not null)
            {
                var exchanges = TacticalBattleViewModel.ParseExchanges(output.Lines, before);
                shots += exchanges.Count(e => e.Kind == BattleExchangeKind.Shot);
                melees += exchanges.Count(e => e.Kind == BattleExchangeKind.Melee);
                if (line.StartsWith("battle shoot", StringComparison.Ordinal) && output.OpenBattleView is { } after && exchanges.Count == 1)
                {
                    var target = int.Parse(line.Split(' ')[3]);
                    Assert.Equal(before.Slots[target].Troops - after.Slots[target].Troops, exchanges[0].TargetLoss);
                }
            }

            before = output.OpenBattleView ?? before;
        }

        Assert.True(shots > 0, "the script shoots");
        Assert.True(melees > 0, "the script fights in melee");
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.04, 0.0)]
    [InlineData(0.26, 0.3)]
    [InlineData(2.96, 3.0)]
    [InlineData(9.0, 3.0)]
    public void The_shot_pause_snaps_to_tenths_of_a_second_between_0_and_3(double wanted, double expected)
    {
        Assert.Equal(expected, BattlePauses.SnapShot(wanted));
    }

    [Theory]
    [InlineData(-2.0, 0.0)]
    [InlineData(0.4, 0.0)]
    [InlineData(1.4, 1.0)]
    [InlineData(1.6, 2.0)]
    [InlineData(7.0, 3.0)]
    public void The_attack_pause_snaps_to_whole_seconds_between_0_and_3(double wanted, double expected)
    {
        Assert.Equal(expected, BattlePauses.SnapAttack(wanted));
    }
}
