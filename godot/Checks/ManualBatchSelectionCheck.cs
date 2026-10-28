using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ManualBatchSelectionCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            Cities(); Mobilize(); MobilizeFirstRefusal();
            foreach (var all in new[] { false, true }) await RecruitmentDisband(all);
            foreach (var commit in new[] { false, true }) { await Split(commit); await Transfer(commit); }
            await MixedDisband(); await StaleRecruitment();
        }
        catch (Exception ex) { GD.PrintErr($"ManualBatchSelectionCheck: {ex}"); _ok = false; }
        GD.Print($"ManualBatchSelectionCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build(bool away = false, bool capitalOnly = false, bool otherCapital = false, bool highSlotUnready = false)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var a = initial.State.ArmyById("north-army-1")! with { Id = "a", X = away ? 10 : 3, Y = away ? 10 : 2,
            Money = 900, SupplyTons = 200, Units = ValueList.From(Enumerable.Range(0, 6)
                .Select(i => new UnitSlot(away && i == 1 ? 1 : 0, "light_infantry", 100, 6, $"U{i}"))) };
        var b = a with { Id = "b", X = a.X + 1, Units = ValueList.Of(new UnitSlot(0, "archers", 100, 6, "V0"),
            new UnitSlot(0, "archers", 100, 6, "V1")) };
        var slots = new[] { new RecruitmentSlot("arx", "light_infantry", 1100, 16),
            new RecruitmentSlot("portus", "heavy_infantry", 1200, 16), new RecruitmentSlot("arx", "archers", 1300, 0),
            new RecruitmentSlot("portus", "light_cavalry", 1400, highSlotUnready ? 0 : 24) };
        var state = initial.State with { Armies = ValueList.Of(a, b), Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList.From(initial.State.Cities.Select(c => c with { Name = c.Id == "arx" ? "Zulu" : c.Id == "portus" ? "Alpha" : c.Name, FortificationCode = 99 })),
            Nations = ValueList.From(initial.State.Nations.Select(n => n.Id != "north" ? n : n with {
                CapitalCityId = otherCapital ? "portus" : "arx",
                RecruitmentSlots = ValueList.From(slots.Where(s => !capitalOnly || s.TargetCityId == "arx")) })) };
        return new GameSession(data.World, data.Ruleset, data.Scenario,
            new SaveGame(state.SchemaVersion, "manual-batch", "manual-batch", state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private RecruitUnitDialog Recruit(GameSession s) { var d = new RecruitUnitDialog { Session = s, Submit = line => s.Submit(line).Lines }; AddChild(d); return d; }
    private void Remove(Control d) { RemoveChild(d); d.QueueFree(); }
    private async Task Settle() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private static void Press(Control d, string text, int index = 0) => d.FindChildren("*", "Button", true, false)
        .OfType<Button>().Where(b => b.Text == text).ElementAt(index).EmitSignal(BaseButton.SignalName.Pressed);
    private static ConfirmPrompt Prompt(Control d) => d.GetChildren().OfType<ConfirmPrompt>().Single(p => !p.IsQueuedForDeletion());

    private void Cities()
    {
        var session = Build(); var before = session.State; var dialog = Recruit(session);
        Check(dialog.CityIdsForCheck.SequenceEqual(new[] { "portus", "arx" }) && dialog.CityCaptionsForCheck[1] == "ZULU",
            "custom/import snapshot city names sort alphabetically and the capital is marked");
        Check(dialog.SelectedCityIdForCheck == RecruitUnitDialogModel.AllCitiesId
            && dialog.TrainingHeadingForCheck == "Units at all cities" && dialog.OrderCaptionForCheck.Contains("Recruit at Zulu:"),
            "All cities shows the whole queue and names its actual capital order destination");
        dialog.SelectCityForCheck("portus");
        Check(dialog.TrainingHeadingForCheck == "Units at Alpha" && dialog.OrderCaptionForCheck.Contains("Recruit at Alpha:"), "per-town queue and order context agree");
        dialog.SelectTrainingRowForCheck(0);
        Check(dialog.MobilizeTargetForCheck.Contains("Alpha"), "mobilization destination names the selected regiment's town");
        var find = new FindCityDialog { Session = session, InitialNationId = "north" }; AddChild(find);
        Check(find.CityIdsForCheck.SequenceEqual(new[] { "portus", "arx" }) && ReferenceEquals(session.State, before), "Find City sorts its own view without reordering engine cities or RNG");
        Remove(find); Remove(dialog);
        dialog = Recruit(Build(capitalOnly: true));
        Check(dialog.SelectedCityIdForCheck == "arx", "alphabetical sorting does not hide the capital queue on opening"); Remove(dialog);
        dialog = Recruit(Build(otherCapital: true));
        Check(dialog.CityCaptionsForCheck[0] == "ALPHA" && dialog.OrderCaptionForCheck.Contains("Recruit at Alpha:"), "capital marker and All cities destination follow the current campaign capital"); Remove(dialog);
    }

    private void Mobilize()
    {
        var s = Build(); var before = s.State; var d = Recruit(s); var events = 0; s.EventsPublished += _ => events++;
        Check(d.TrainingListForCheck.SelectMode == ItemList.SelectModeEnum.Multi, "recruitment queue supports multiple selected regiments");
        // A real Ctrl-selection signal retains stable slots across the in-place refresh.
        d.TrainingListForCheck.EmitSignal(ItemList.SignalName.MultiSelected, 2L, true);
        d.TrainingListForCheck.EmitSignal(ItemList.SignalName.MultiSelected, 3L, true);
        d.MobilizeForCheck();
        Check(ReferenceEquals(s.State, before) && events == 0 && d.ReplyForCheck.Length > 0
            && d.TrainingListForCheck.GetSelectedItems().Length == 2, "mixed readiness refuses the complete batch with feedback and selection intact");
        d.SelectTrainingRowsForCheck(0, 3); d.MobilizeForCheck();
        Check(s.State.NationById("north")!.RecruitmentSlots.Select(x => x.Troops).SequenceEqual(new[] { 1200, 1300 })
            && s.State.Armies.SelectMany(a => a.Units).Count(u => u.Troops is 1100 or 1400) == 2 && events == 1,
            "descending original slot identities mobilize exactly the two selected units across towns in one commit");
        var after = s.State; d.MobilizeForCheck();
        Check(ReferenceEquals(after, s.State) && events == 1 && d.TrainingListForCheck.GetSelectedItems().Length == 0,
            "post-action refresh clears selection instead of retargeting shifted slots"); Remove(d);
    }

    private void MobilizeFirstRefusal()
    {
        var s = Build(highSlotUnready: true); var before = s.State; var d = Recruit(s);
        var events = 0; s.EventsPublished += _ => events++;
        d.SelectTrainingRowsForCheck(0, 3); d.MobilizeForCheck();
        Check(ReferenceEquals(before, s.State) && events == 0 && d.ReplyForCheck.Length > 0
            && d.TrainingListForCheck.GetSelectedItems().Length == 2,
            "a higher-index unready slot refuses before the ready slot: no accepted remainder may commit");
        d.SelectTrainingRowsForCheck(0, 1); d.MobilizeForCheck();
        Check(s.State.NationById("north")!.RecruitmentSlots.Select(x => x.Troops).SequenceEqual(new[] { 1300, 1400 })
            && s.State.Armies.SelectMany(a => a.Units).Count(u => u.Troops is 1100 or 1200) == 2 && events == 1,
            "a later valid selection mobilizes exactly its own units, without leaking the failed batch");
        Remove(d);
    }

    private async Task RecruitmentDisband(bool all)
    {
        var s = Build(); var before = s.State; var d = Recruit(s);
        if (!all) d.SelectCityForCheck("arx");
        d.SelectTrainingRowsForCheck(0, all ? 3 : 1); d.DisbandForCheck();
        Check(Prompt(d).Question.Contains("2") && ReferenceEquals(s.State, before), "queued Disband confirms the exact selected count");
        Prompt(d).AnswerForCheck(false); await Settle();
        Check(ReferenceEquals(s.State, before), "queued Disband No changes nothing");
        d.DisbandForCheck(); Prompt(d).AnswerForCheck(true); await Settle();
        var left = s.State.NationById("north")!.RecruitmentSlots.Select(x => x.Troops).ToArray();
        Check(left.SequenceEqual(all ? new[] { 1200, 1300 } : new[] { 1200, 1400 }), "Yes disbands stable selected slots in per-town and All cities views"); Remove(d);
    }

    private async Task Split(bool commit)
    {
        var s = Build(); var before = s.State; var d = new SplitArmyDialog { Session = s, ArmyId = "a", NewArmyId = "new" }; AddChild(d);
        d.ArmyListForCheck.Select(0); d.ArmyListForCheck.Select(1, false); Press(d, "Transfer");
        Check(d.ModelForCheck.StagedUnits.Count == 2, "Split moves both selected whole units");
        d.NewArmyListForCheck.Select(0); d.NewArmyListForCheck.Select(1, false); Press(d, "Transfer", 1);
        Check(d.ModelForCheck.StagedUnits.Count == 0, "Split moves a multiple selection back from the new army");
        for (var i = 0; i < 6; i++) d.ArmyListForCheck.Select(i, false);
        Press(d, "Transfer"); Check(d.ModelForCheck.StagedUnits.Count == 0, "a batch emptying the parent is wholly refused");
        d.ArmyListForCheck.DeselectAll(); d.ArmyListForCheck.Select(1); d.ArmyListForCheck.Select(4, false); Press(d, "Transfer");
        d.PressMoneyForCheck(50);
        d.ArmyListForCheck.Select(0); d.ArmyListForCheck.Select(1, false); Press(d, "Disband");
        Check(Prompt(d).Question.Contains("2"), "Split Disband confirms both selected units");
        Prompt(d).AnswerForCheck(true); await Settle();
        Check(ReferenceEquals(before, s.State) && d.ModelForCheck.StagedUnits.Select(i => d.ModelForCheck.Units[i].Name).SequenceEqual(new[] { "U1", "U4" }),
            "batch disband reindexes surviving split identities without committing");
        if (commit) d.OkForCheck(); else d.CancelForCheck();
        Check(commit ? s.State.ArmyById("new")!.Units.Select(u => u.Name).SequenceEqual(new[] { "U1", "U4" })
            && s.State.ArmyById("new")!.Money == 50 : ReferenceEquals(before, s.State), "Split OK/Cancel preserves intended units and allocations"); Remove(d);
    }

    private async Task Transfer(bool commit)
    {
        var s = Build(); var before = s.State; var d = new ArmyTransferDialog { Session = s, ArmyId = "a", PartnerId = "b" }; AddChild(d);
        d.SelectedListForCheck.Select(5); d.TransferSelectedForCheck(true); d.PressMoneyForCheck(50);
        d.SelectedListForCheck.Select(0); d.SelectedListForCheck.Select(2, false); d.DisbandSelectedForCheck(true);
        Check(Prompt(d).Question.Contains("2"), "Transfer Disband confirms the batch count"); Prompt(d).AnswerForCheck(true); await Settle();
        Check(ReferenceEquals(s.State, before) && d.ModelForCheck.PartnerWorkingUnits.Last().Unit.Name == "U5"
            && d.ModelForCheck.SelectedWorkingUnits.Select(u => u.Unit.Name).SequenceEqual(new[] { "U1", "U3", "U4" }),
            "deleting multiple origin slots preserves a previously transferred unit and surviving roster");
        if (commit) d.OkForCheck(); else d.CancelForCheck();
        Check(commit ? s.State.ArmyById("b")!.Units.Last().Name == "U5" && s.State.ArmyById("b")!.Money == 950
            : ReferenceEquals(before, s.State), "Transfer batch commits once or cancels as a whole"); Remove(d);
    }

    private async Task MixedDisband()
    {
        var s = Build(away: true); var before = s.State;
        var d = new ArmyTransferDialog { Session = s, ArmyId = "a", PartnerId = "b" }; AddChild(d);
        for (var i = 0; i < 6; i++) d.SelectedListForCheck.Select(i, false);
        d.DisbandSelectedForCheck(true);
        Prompt(d).AnswerForCheck(true); await Settle();
        Check(ReferenceEquals(before, s.State) && d.ModelForCheck.SelectedWorkingUnits.Count == 6 && d.ReplyForCheck.Length > 0,
            "the existing last-unit safeguard cannot delete an accepted prefix of a batch"); Remove(d);
    }

    private async Task StaleRecruitment()
    {
        var s = Build(); var d = Recruit(s); d.SelectTrainingRowsForCheck(0, 1); d.DisbandForCheck();
        s.Submit("rename-unit a 0 External"); var beforeYes = s.State;
        Prompt(d).AnswerForCheck(true); await Settle();
        Check(ReferenceEquals(s.State, beforeYes) && d.ReplyForCheck.Contains("Reopen", StringComparison.OrdinalIgnoreCase), "stale queued Disband confirmation cannot retarget or overwrite a changed campaign");
        d.SelectTrainingRowsForCheck(0, 1); s.Submit("disband-slot 0"); var shifted = s.State;
        d.MobilizeForCheck();
        Check(ReferenceEquals(s.State, shifted) && d.TrainingListForCheck.GetSelectedItems().Length == 0
            && d.ReplyForCheck.Contains("Select the units again"), "external queue compaction clears stale selection rather than retargeting new slot occupants");
        Remove(d);
    }

    private void Check(bool condition, string message) { GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition; }
}

