using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class TacticalUndoCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var armies = ValueList.Of(
                new StartingArmy("north-army-1", "north", 3, 2, 68, 100, 500, 5,
                    ValueList.Of(new UnitSlot(0, "light_infantry", 9000, 6, "Foot"), new UnitSlot(0, "archers", 3000, 7, "Bows"))),
                new StartingArmy("south-army-1", "south", 4, 2, 59, 40, 60, 5,
                    ValueList.Of(new UnitSlot(0, "heavy_infantry", 600, 6, "Guards"))));
            var world = data.World with { StartingArmies = armies };
            var session = new GameSession(world, data.Ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "north")
                { HumanBattles = HumanBattleMode.Tactical };
            session.Submit("declare-war south");
            var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
                Size = GetViewport().GetVisibleRect().Size };
            AddChild(main);
            main.SubmitForCheck("attack-army north-army-1 south-army-1");
            await Frames();
            var screen = (TacticalBattleScreen)main.ActiveOverlay!;
            screen.SkipPlayback();
            var before = session.OpenTacticalBattle;
            screen.SelectUnit(1);
            screen.ClickCell(1, 2);
            screen.SkipPlayback();
            Check(session.CanUndoTactical && !screen.UndoButton.Disabled, "own placement enables the visible Undo button");
            screen.UndoButton.EmitSignal(BaseButton.SignalName.Pressed);
            screen.SkipPlayback();
            Check(session.OpenTacticalBattle == before && !session.CanUndoTactical, "the button restores the exact board and exhausts history");
            Check(screen.ExchangePlainText.Length == 0 && !screen.UnitListOpen, "rewind clears stale exchange/list presentation");
            var campaign = session.State;
            screen.PressToolbarButton("surrender");
            screen.AnswerSurrender(true);
            screen.SkipPlayback();
            Check(main.ActiveOverlay is BattleEndedScreen && session.BattleResultAwaitingAcknowledgement,
                "Battle ended remains provisional until OK");
            Check(ReferenceEquals(campaign, session.State), "result preview commits no campaign consequences");
            var ended = (BattleEndedScreen)main.ActiveOverlay!;
            Check(ended.UndoButton is { Disabled: false }, "own terminal action offers Undo before OK");
            ended.UndoButton!.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            var restored = (TacticalBattleScreen)main.ActiveOverlay!;
            restored.SkipPlayback();
            Check(session.OpenTacticalBattle == before && !session.BattleResultAwaitingAcknowledgement,
                "terminal Undo reopens the exact own-turn board");
            restored.PressToolbarButton("surrender");
            restored.AnswerSurrender(true);
            restored.SkipPlayback();
            ((BattleEndedScreen)main.ActiveOverlay!).Close();
            Check(!session.BattleResultAwaitingAcknowledgement && session.OpenTacticalBattle is null,
                "OK commits once and closes the battle");
            RemoveChild(main); main.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"TacticalUndoCheck: {ex}"); _ok = false; }
        GD.Print($"TacticalUndoCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Frames()
    {
        for (var i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
