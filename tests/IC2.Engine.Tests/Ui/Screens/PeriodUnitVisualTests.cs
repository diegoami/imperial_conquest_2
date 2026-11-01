using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Armies;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

public sealed class PeriodUnitVisualTests
{
    [Fact]
    public void Chariot_alias_changes_stationary_and_moving_images_but_not_battle_state_or_rng()
    {
        var rules = PeriodUnitPresentationTests.PeriodRules();
        var armies = TacticalSessionTestbed.StrongAttacker();
        armies[0] = armies[0] with { Units = ValueList.Of(TacticalSessionTestbed.Unit("light_cavalry", 5000, 6, "1st Chariots Battalion")) };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, rules, armies);
        var baseline = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies);
        var output = session.Submit("attack-army north-army-1 south-army-1");
        baseline.Submit("attack-army north-army-1 south-army-1");
        var view = Assert.IsType<TacticalBattleView>(output.OpenBattleView);
        Assert.Equal(baseline.OpenTacticalBattle, view);
        Assert.Equal(baseline.State, session.State);
        Assert.Equal(baseline.TacticalDrawState, session.TacticalDrawState);
        var display = BattleDisplay.For(view, session.State, rules);
        var model = TacticalBattleViewModel.From(view, display);
        var cell = model.Cells.Single(c => c.Slot == 0);
        Assert.StartsWith("battle.unit.light_chariot.", cell.IconKey);
        Assert.Equal("light_cavalry", view.UnitTypeIds[view.Slots[0].Type]);
        Assert.Equal("Light chariots", display.UnitTypeNames[view.Slots[0].Type]);
        var unit = view.Slots[0];
        var pose = new TacticalPose(new TacticalGlyph(0, unit.X, unit.Y, cell.Code, 0, "light_cavalry"), unit.X + 0.5, unit.Y, 1, false);
        var snapshot = new TacticalPresentationSnapshot(view, display.BoardWidth, display.BoardHeight, false,
            ValueList<int>.Empty, ValueList<int>.Empty);
        var moving = TacticalBattleViewModel.FromSnapshot(snapshot, display, pose: pose, stage: TacticalPlaybackStage.Travel);
        Assert.Equal(cell.IconKey, moving.VisualUnits.Single(u => u.Cell.Slot == 0).Cell.IconKey);
        Assert.Equal(view.Slots[0], moving.View.Slots[0]);
        var army = session.State.ArmyById("north-army-1")!;
        var panel = InformationPanelModel.OwnArmy(session.State, session.World, rules, army);
        Assert.Contains(panel, line => line.Text.StartsWith("Light chariots:", StringComparison.Ordinal));
        var table = new GameCommandTable(session.World, rules);
        var filter = table.MenuRowById("area_map.show_mercs_light_cavalry")!;
        Assert.Equal("Light chariots", filter.Caption);
        Assert.Equal("unit.light_chariot.icon", filter.IconKey);
    }
}
