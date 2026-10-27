using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class BattleInspectionCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            var data = GameDataContext.Repository.Resolve("toy-3city");
            StartingArmy Army(string id, string nation, int x, params UnitSlot[] units) =>
                new(id, nation, x, 2, 68, 100, 100, 8, ValueList.Of(units));
            var world = data.World with { StartingArmies = ValueList.Of(
                Army("inspect-a", "north", 3, new UnitSlot(0, "light_infantry", 1000, 6, "Own one"),
                    new UnitSlot(0, "archers", 1000, 6, "Own two")),
                Army("inspect-b", "south", 4, new UnitSlot(0, "heavy_infantry", 2000, 7, "Enemy"))) };
            var session = new GameSession(world, data.Ruleset, data.Scenario) { HumanBattles = HumanBattleMode.Tactical };
            var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
                Size = GetViewport().GetVisibleRect().Size };
            AddChild(main);
            main.SubmitForCheck("declare-war south");
            main.SubmitForCheck("attack-army inspect-a inspect-b");
            var screen = (TacticalBattleScreen)main.ActiveOverlay!;
            screen.SkipPlayback();
            var before = session.State;
            var battle = session.OpenTacticalBattle;
            var enemy = screen.Model.View.Slots[20];
            var commands = 0;
            main.CommandIssued += _ => commands++;
            screen.ClickCell(enemy.X, enemy.Y);
            Check(screen.SelectedSlot == -1 && screen.InspectedSlot == 20 && screen.PanelLabel.Visible,
                "enemy click opens details without selecting a command unit");
            Check(screen.PanelLabel.Text.Contains("Enemy", StringComparison.Ordinal)
                  && !new[] { "Quality:", "Morale:", "Moves:", "Shots:", "Attacks:" }
                      .Any(prefix => screen.PanelLabel.Text.Contains(prefix, StringComparison.Ordinal)),
                "actual inspection panel shows basic identity without enemy private details");
            var own = screen.Model.View.Slots[0];
            screen.ClickCell(own.X, own.Y);
            Check(screen.SelectedSlot >= 0 && screen.InspectedSlot == -1, "own selection closes read-only inspection");
            screen.ClickCell(enemy.X, enemy.Y);
            Check(screen.SelectedSlot == -1 && screen.InspectedSlot == 20, "ineligible enemy click clears the friendly command selection");
            Check(commands == 0 && session.State == before && session.OpenTacticalBattle == battle,
                "inspection issues no order and changes no campaign, battle or RNG state");
            RemoveChild(main); main.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"BattleInspectionCheck: {ex}"); _ok = false; }
        GD.Print($"BattleInspectionCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
