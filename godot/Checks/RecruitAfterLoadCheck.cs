using Godot;
using IC2.Engine.Recruitment;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T162 Done-when 2 (bug #1001): recruit at a non-capital city through the Recruit unit dialog, save,
/// load through the Load screen, open Recruit unit again, find the unit (the dialog opens on All cities
/// because it trains outside the capital), and mobilize it once ready. On <c>origin/main</c> the dialog
/// has no All cities entry and opens on the first town, so the unit is not in the list and this fails.
/// </summary>
public partial class RecruitAfterLoadCheck : Node
{
    private bool _ok = true;
    private string? _savedPath;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"RecruitAfterLoadCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUp();
        }

        GD.Print($"RecruitAfterLoadCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private void CleanUp()
    {
        try
        {
            if (_savedPath is not null && File.Exists(_savedPath))
            {
                File.Delete(_savedPath);
                var dir = Path.GetDirectoryName(_savedPath);
                if (dir is not null && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"RecruitAfterLoadCheck: cleanup failed: {ex.Message}");
        }
    }

    private void Run()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);
        appRoot.ShowNewGameFlow();
        var flow = appRoot.CurrentScreen as NewGameFlow;
        if (!Check(flow?.Chooser is not null, "New Game reaches the ruleset chooser")) { return; }
        flow!.Chooser!.ConfirmSelection();
        flow.SeatScreen!.ConfirmSeatAndStart();
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Start Game reaches the main game screen")) { return; }

        var screen = (MainGameScreen)appRoot.CurrentScreen!;
        var state = screen.Session.State;
        var ruleset = screen.Session.Ruleset;
        var nation = state.NationById(state.ActiveNationId)!;

        // A non-capital town the Recruit order is accepted at.
        var town = state.Cities.FirstOrDefault(c =>
            c.Owner == nation.Id && c.Id != nation.CapitalCityId && RecruitmentEligibility.MayTakeOrder(c, nation, ruleset));
        if (!Check(town is not null, "the seat owns a non-capital town that accepts a Recruit order")) { return; }

        var slotsBefore = nation.RecruitmentSlots.Count;

        // ---- recruit there through the dialog ----
        screen.MenuBar.PressItemForCheck("strategy.recruit_unit");
        var dialog = screen.ActiveOverlay as RecruitUnitDialog;
        if (!Check(dialog is not null, "Recruit unit opens its dialog")) { return; }
        dialog!.SelectCityForCheck(town!.Id);
        Check(dialog.RecruitEnabledForCheck, "Recruit is enabled at the non-capital town");
        dialog.RecruitForCheck();
        var recruitedSlot = screen.Session.State.NationById(nation.Id)!.RecruitmentSlots.Count - 1;
        if (!Check(recruitedSlot == slotsBefore, $"the order added one slot (index {recruitedSlot}; reply: {dialog.ReplyForCheck})")) { return; }
        dialog.CancelForCheck();

        // ---- save, load ----
        screen.PressSaveForCheck("t162-recruitafterloadcheck");
        _savedPath = screen.LastSavedPath;
        if (!Check(_savedPath is not null && File.Exists(_savedPath), "the Save action wrote a file")) { return; }
        appRoot.ShowLoadGame();
        ((LoadGameScreen)appRoot.CurrentScreen!).ContinueForCheck(_savedPath!);
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Continue returns to the main game screen")) { return; }
        var resumed = (MainGameScreen)appRoot.CurrentScreen!;
        Check(resumed.Session.State.NationById(nation.Id)!.RecruitmentSlots.Count == slotsBefore + 1,
            "the loaded game still holds the recruited slot");

        // ---- open Recruit unit: the unit is in the opening view ----
        resumed.MenuBar.PressItemForCheck("strategy.recruit_unit");
        dialog = resumed.ActiveOverlay as RecruitUnitDialog;
        if (!Check(dialog is not null, "Recruit unit opens after the load")) { return; }
        Check(dialog!.SelectedCityIdForCheck == RecruitUnitDialogModel.AllCitiesId,
            $"the dialog opens on All cities (got '{dialog.SelectedCityIdForCheck}')");
        Check(dialog.RecruitEnabledForCheck, "All cities offers Recruit, which trains at the capital (T166)");
        Check(dialog.TrainingRowsForCheck.Any(r => r.SlotIndex == recruitedSlot && r.TargetCityId == town.Id),
            "the recruited unit is listed, with its city");
        Check(dialog.TrainingRowTextsForCheck.Any(t => t.Contains(resumed.Session.State.CityById(town.Id)!.Name)),
            "the row text names the city");
        dialog.CancelForCheck();

        // ---- wait until ready, then mobilize from All cities ----
        for (var i = 0; i < 40; i++)
        {
            var rows = RecruitmentPanelViewModel.TrainingForNation(
                resumed.Session.State, resumed.Session.Ruleset, nation.Id);
            if (rows.Any(r => r.SlotIndex == recruitedSlot && r.IsReady)) { break; }
            resumed.SubmitForCheck("end");
            DriveBattles(resumed);
        }

        resumed.MenuBar.PressItemForCheck("strategy.recruit_unit");
        dialog = (RecruitUnitDialog)resumed.ActiveOverlay!;
        var rowIndex = dialog.TrainingRowsForCheck.ToList().FindIndex(r => r.SlotIndex == recruitedSlot);
        dialog.SelectTrainingRowForCheck(rowIndex);
        if (!Check(dialog.MobilizeEnabledForCheck, "Mobilize is enabled once the unit is ready")) { return; }
        var armiesBefore = resumed.Session.State.Armies.Count;
        var slotsNow = resumed.Session.State.NationById(nation.Id)!.RecruitmentSlots.Count;
        dialog.MobilizeForCheck();
        Check(resumed.Session.State.NationById(nation.Id)!.RecruitmentSlots.Count == slotsNow - 1,
            $"mobilizing removed the slot (reply: {dialog.ReplyForCheck})");
        Check(resumed.Session.State.Armies.Count >= armiesBefore, "the regiment joined or formed an army");
    }

    /// <summary>
    /// T127: the Godot UI fights a battle with a human side on the board, so an AI seat's attack on Rome during an
    /// <c>end</c> opens the battle screen. This check is about recruiting, not battles: it plays each battle out with
    /// <em>Computer general on</em> and closes the <em>Battle ended</em> window, as a player who lets the general fight would.
    /// </summary>
    private static void DriveBattles(MainGameScreen screen)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            if (screen.ActiveOverlay is TacticalBattleScreen battle)
            {
                battle.PressToolbarButton("computer_general");
            }
            else if (screen.ActiveOverlay is BattleEndedScreen ended)
            {
                ended.Close();
            }
            else
            {
                return;
            }
        }
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
