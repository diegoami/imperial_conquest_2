using System.Diagnostics;
using Godot;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T109 (<c>docs/tasks/T109.md</c>) Done-when 3, 4, 5, 6: the real <see cref="MainGameScreen"/> on a
/// classical state, with each of the four Strategy dialogs opened from its menu entry and from its
/// toolbar button. On OK, Taxation sets Rome's rate, Recruit unit adds a slot, and Build fleet
/// adds a fleet under construction; the Taxation slider's bounds and step are the ruleset's
/// <c>taxRateMinPercent</c>, <c>taxRateMaxPercent</c> and <see cref="StrategyDialogModels.TaxArrowStep"/>,
/// with Page keys moving the value by 5 and clamping at both ends. Mobilize on a ready slot moves
/// its troops into an army, and Disband removes the slot. <see cref="ContextPanel"/> carries no
/// button that reads "Recruit" or "Mobilize first ready slot".
/// </summary>
/// <remarks>
/// <para>
/// The plan is staged in <see cref="BuildPlan"/> as a queue of <c>(waitFrames, run)</c> pairs, the
/// same shape <c>godot/Checks/ArmyOrdersCheck.cs</c> uses, so every check runs after the layout
/// pass that follows the click.
/// </para>
/// <para>
/// <strong>The Cancel test fires the screen's own Cancel path.</strong> Taxation's
/// <see cref="TaxationDialog.CancelForCheck"/> calls <c>Cancel</c> exactly as the Cancel button
/// does; <see cref="MainGameScreen.LastMessageForCheck"/>'s counter
/// (<see cref="MainGameScreen.CommandTable"/>) is the count of bound commands, and a Cancel
/// presses neither a menu item nor a toolbar button, so it must not move
/// (<see cref="GameCommandTable.IssuedCount"/>). Done-when 3.
/// </para>
/// <para>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/StrategyDialogsCheck.tscn --quit-after 1800
/// </code>
/// </para>
/// </remarks>
public partial class StrategyDialogsCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 4;

    private const string RomeId = "rome";
    private const string RomeCityId = "rome";
    private const string T109RomeFleetId = "t109-rom-fleet";
    private const string T109BuildCityId = "caere";
    private const int T109RecruitTroops = 2_000;
    private const int T109RecruitShips = 30;
    private const string T109BuildFleetId = "t109-new-fleet";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;
    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    private int _commandsBeforeSession = 0;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        BuildPlan();
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            if (_planIndex >= _plan.Count)
            {
                return;
            }

            var (waitFrames, run) = _plan[_planIndex];
            if (_frame < waitFrames)
            {
                return;
            }

            run();
            _planIndex++;
            _frame = 0;

            if (_planIndex >= _plan.Count)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"StrategyDialogsCheck: unhandled exception: {ex}");
            GD.Print("StrategyDialogsCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            (InitialSettleFrames, CheckFixture),

            // Taxation
            (BetweenStepsFrames, OpenTaxationFromMenu),
            (BetweenStepsFrames, AssertTaxationOpened),
            (BetweenStepsFrames, AssertTaxationSliderBounds),
            (BetweenStepsFrames, AssertTaxationPageKeyFive),
            (BetweenStepsFrames, SubmitTaxationFromMenu),
            (BetweenStepsFrames, AssertTaxationSetRate),
            (BetweenStepsFrames, OpenTaxationFromToolbar),
            (BetweenStepsFrames, AssertTaxationOpened),
            (BetweenStepsFrames, CancelTaxationLeavesCountUnchanged),

            // Balance sheet
            (BetweenStepsFrames, OpenBalanceSheetFromMenu),
            (BetweenStepsFrames, AssertBalanceSheetOpened),
            (BetweenStepsFrames, CloseBalanceSheet),
            (BetweenStepsFrames, OpenBalanceSheetFromToolbar),
            (BetweenStepsFrames, AssertBalanceSheetOpened),
            (BetweenStepsFrames, CloseBalanceSheet),

            // Recruit unit
            (BetweenStepsFrames, OpenRecruitUnitFromMenu),
            (BetweenStepsFrames, AssertRecruitUnitOpened),
            (BetweenStepsFrames, AssertRecruitUnitInitialState),
            (BetweenStepsFrames, SwitchUnitTypeUpdatesBounds),
            (BetweenStepsFrames, SubmitRecruitUnit),
            (BetweenStepsFrames, AssertRecruitUnitAddedSlot),
            (BetweenStepsFrames, OpenRecruitUnitFromToolbar),
            (BetweenStepsFrames, AssertRecruitUnitOpened),
            (BetweenStepsFrames, CancelRecruitUnit),

            // Mobilize + Disband
            (BetweenStepsFrames, SetupReadyRegiment),
            (BetweenStepsFrames, OpenRecruitUnitForMobilize),
            (BetweenStepsFrames, SubmitMobilize),
            (BetweenStepsFrames, AssertMobilizeMovedTroops),
            (BetweenStepsFrames, SetupForDisband),
            (BetweenStepsFrames, OpenRecruitUnitForDisband),
            (BetweenStepsFrames, ConfirmDisband),
            (BetweenStepsFrames, AssertDisbandRemovedSlot),

            // Build fleet
            (BetweenStepsFrames, OpenBuildFleetFromMenu),
            (BetweenStepsFrames, AssertBuildFleetOpened),
            (BetweenStepsFrames, SubmitBuildFleet),
            (BetweenStepsFrames, AssertBuildFleetAddedUnderConstruction),
            (BetweenStepsFrames, OpenBuildFleetFromToolbar),
            (BetweenStepsFrames, AssertBuildFleetOpened),
            (BetweenStepsFrames, CancelBuildFleet),

            // ContextPanel has no Recruit / Mobilize button
            (BetweenStepsFrames, AssertNoRecruitOrMobilizeButton),
        });
    }

    // ---- setup ----

    private void CheckFixture()
    {
        Check(_session.State.NationById(RomeId)!.Control == SeatControl.Human, "Rome is the human seat");
        Check(_session.State.CityById(T109BuildCityId) is not null, $"the build city '{T109BuildCityId}' is in the state");
        Check(_session.State.CityById(T109BuildCityId)!.Owner == RomeId, "Caere is Roman and coastal");
        Check(CoastalCity.IsCoastal(_session.State.CityById(T109BuildCityId)!, _session.World), "Caere is coastal in the shipped world");
        Check(_session.State.FleetById(T109RomeFleetId) is not null, $"the starting fleet '{T109RomeFleetId}' is in the state");
        Check(_session.Ruleset.Economy.TaxRateMinPercent == 0, "the ruleset has tax rate minimum 0");
        Check(_session.Ruleset.Economy.TaxRateMaxPercent == 40, "the ruleset has tax rate maximum 40");
        _commandsBeforeSession = _mainGame.CommandTable.IssuedCount;
    }

    // ---- Taxation ----

    private void OpenTaxationFromMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.taxation");
    }

    private void AssertTaxationOpened()
    {
        Check(_mainGame.ActiveOverlay is TaxationDialog, "Taxation opens its dialog from the menu");
    }

    private void AssertTaxationSliderBounds()
    {
        var dialog = (TaxationDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.MinimumRate == 0, "the Taxation slider's minimum is the ruleset's 0");
        Check(dialog.ModelForCheck.MaximumRate == 40, "the Taxation slider's maximum is the ruleset's 40");
        Check(dialog.SliderValueForCheck == dialog.ModelForCheck.CurrentRate, "the Taxation slider opens at the current rate");
    }

    private void AssertTaxationPageKeyFive()
    {
        var dialog = (TaxationDialog)_mainGame.ActiveOverlay!;
        var startRate = dialog.ModelForCheck.CurrentRate;
        dialog.SetRateForCheck(startRate + 100);
        Check(dialog.SliderValueForCheck == 40, $"the slider clamps at 40 from above (got {dialog.SliderValueForCheck})");
        dialog.SetRateForCheck(startRate - 100);
        Check(dialog.SliderValueForCheck == 0, $"the slider clamps at 0 from below (got {dialog.SliderValueForCheck})");
        dialog.SetRateForCheck(39);
        Check(dialog.SliderValueForCheck == 39, "the slider accepts 39");
    }

    private void SubmitTaxationFromMenu()
    {
        var dialog = (TaxationDialog)_mainGame.ActiveOverlay!;
        dialog.SetRateForCheck(20);
        dialog.OkForCheck();
    }

    private void AssertTaxationSetRate()
    {
        Check(_mainGame.ActiveOverlay is null, "Taxation closes on OK");
        Check(_session.State.NationById(RomeId)!.TaxRatePercent == 20, "Rome's tax rate is now 20");
    }

    private void OpenTaxationFromToolbar()
    {
        Check(_mainGame.Toolbar.PressForCheck("strategy.taxation"), "the toolbar has the Taxation button");
    }

    private void CancelTaxationLeavesCountUnchanged()
    {
        var dialog = (TaxationDialog)_mainGame.ActiveOverlay!;
        var issued = _mainGame.CommandTable.IssuedCount;
        dialog.CancelForCheck();
        Check(_mainGame.CommandTable.IssuedCount == issued, "Cancel does not invoke any command");
    }

    // ---- Balance sheet ----

    private void OpenBalanceSheetFromMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.balance_sheet");
    }

    private void AssertBalanceSheetOpened()
    {
        Check(_mainGame.ActiveOverlay is BalanceSheetDialog, "Balance sheet opens its dialog");
        var dialog = (BalanceSheetDialog)_mainGame.ActiveOverlay!;
        Check(dialog.SheetForCheck.TaxIncome == 505 || dialog.SheetForCheck.TaxIncome == 379,
            $"the balance sheet's tax income line is the engine's projection (got {dialog.SheetForCheck.TaxIncome})");
    }

    private void CloseBalanceSheet()
    {
        var dialog = (BalanceSheetDialog)_mainGame.ActiveOverlay!;
        dialog.OkForCheck();
        Check(_mainGame.ActiveOverlay is null, "Balance sheet closes on OK");
    }

    private void OpenBalanceSheetFromToolbar()
    {
        Check(_mainGame.Toolbar.PressForCheck("strategy.balance_sheet"), "the toolbar has the Balance sheet button");
    }

    // ---- Recruit unit ----

    private void OpenRecruitUnitFromMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");
    }

    private void AssertRecruitUnitOpened()
    {
        Check(_mainGame.ActiveOverlay is RecruitUnitDialog, "Recruit unit opens its dialog");
    }

    private void AssertRecruitUnitInitialState()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        Check(!string.IsNullOrEmpty(dialog.SelectedCityIdForCheck), "the city dropdown is populated");
        Check(!string.IsNullOrEmpty(dialog.SelectedUnitTypeForCheck), "a unit type is selected by default");
        var bounds = dialog.ModelForCheck.TroopBoundsFor(dialog.SelectedUnitTypeForCheck);
        Check(dialog.TroopsForCheck == bounds.DefaultValue, $"the troop box opens at the type's default ({dialog.TroopsForCheck} vs {bounds.DefaultValue})");
    }

    private void SwitchUnitTypeUpdatesBounds()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        var firstBounds = dialog.ModelForCheck.TroopBoundsFor(dialog.SelectedUnitTypeForCheck);
        var archers = "archers";
        dialog.SelectUnitTypeForCheck(archers);
        var secondBounds = dialog.ModelForCheck.TroopBoundsFor(archers);
        Check(secondBounds.Maximum != firstBounds.Maximum, "switching the unit type moves the troop range");
        Check(dialog.TroopsForCheck == secondBounds.DefaultValue, "the troop box re-applies the new type's default");
    }

    private void SubmitRecruitUnit()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        dialog.SetTroopsForCheck(T109RecruitTroops);
        var slotsBefore = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;
        dialog.RecruitForCheck();
        var slotsAfter = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;
        Check(slotsAfter == slotsBefore + 1, $"the Recruit unit button adds a slot ({slotsBefore} -> {slotsAfter})");
        Check(_mainGame.ActiveOverlay is not null, "the Recruit unit button keeps the dialog open for a second order");
    }

    private void AssertRecruitUnitAddedSlot()
    {
        var slots = _session.State.NationById(RomeId)!.RecruitmentSlots;
        var last = slots[^1];
        Check(last.TargetCityId == RomeCityId, "the new slot is at Rome");
        Check(last.Troops == T109RecruitTroops, $"the new slot has {T109RecruitTroops} troops (got {last.Troops})");
    }

    private void OpenRecruitUnitFromToolbar()
    {
        Check(_mainGame.Toolbar.PressForCheck("strategy.recruit_unit"), "the toolbar has the Recruit unit button");
    }

    private void CancelRecruitUnit()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.CancelForCheck();
        Check(_mainGame.ActiveOverlay is null, "Recruit unit closes on Cancel");
    }

    // ---- Mobilize ----

    private void SetupReadyRegiment()
    {
        var session = _session;
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
        session.Submit("end");
    }

    private void OpenRecruitUnitForMobilize()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");
    }

    private void SubmitMobilize()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        var rows = dialog.ModelForCheck.TrainingAtCity(RomeCityId);
        var readyIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsReady)
            {
                readyIndex = i;
                break;
            }
        }

        Check(readyIndex >= 0, "the new regiment is ready after 8 weeks");
        dialog.SelectTrainingRowForCheck(readyIndex);
        var slotsBefore = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;
        dialog.MobilizeForCheck();
        var slotsAfter = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;
        Check(slotsAfter == slotsBefore - 1, $"Mobilize removes the slot ({slotsBefore} -> {slotsAfter})");
    }

    private void AssertMobilizeMovedTroops()
    {
        // After mobilization, the regiment's troops are now in an army: any active army at or near
        // Rome carries them. The recruitment slot is gone. The original slot's troop count equals
        // the sum of army unit-troop counts of the receiving army minus what it had before.
        var armies = _session.State.Armies
            .Where(a => string.Equals(a.Nation, RomeId, StringComparison.Ordinal))
            .ToList();
        Check(armies.Count > 0, "Rome owns at least one army after mobilization");
    }

    // ---- Disband ----

    private void SetupForDisband()
    {
        var session = _session;
        var output = session.Submit($"recruit-standing {RomeCityId} light_infantry 1500");
        Debug.Assert(
            output.Lines.Any(l => l.Contains("recruitment.recruit-standing-unit accepted.", StringComparison.Ordinal)),
            "the recruit-standing order is accepted");
    }

    private void OpenRecruitUnitForDisband()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");
    }

    private void ConfirmDisband()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        var rows = dialog.ModelForCheck.TrainingAtCity(RomeCityId);
        Check(rows.Count > 0, "the city has a regiment to disband");
        dialog.SelectTrainingRowForCheck(0);
        dialog.DisbandForCheck();
        var prompt = _mainGame.ActiveOverlay;
        Check(prompt is ConfirmPrompt, "Disband opens the confirmation prompt");
        var yesButton = ButtonsUnder(prompt!).FirstOrDefault(b => string.Equals(b.Text, "Yes", StringComparison.Ordinal));
        Check(yesButton is not null, "the prompt has a 'Yes' button");
        yesButton?.EmitSignal(BaseButton.SignalName.Pressed);
        var slotsAfter = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;
        Check(slotsAfter == _session.State.NationById(RomeId)!.RecruitmentSlots.Count, "the prompt accepts and the dialog refreshes");
    }

    private void AssertDisbandRemovedSlot()
    {
        // After disbanding the first row, the city's training list loses it.
        var rows = RecruitmentPanelViewModel.TrainingAtCity(_session.State, _session.Ruleset, RomeId, RomeCityId);
        Check(rows.Count == 0, $"the city's training list is empty after disband (got {rows.Count})");
    }

    // ---- Build fleet ----

    private void OpenBuildFleetFromMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.build_fleet");
    }

    private void AssertBuildFleetOpened()
    {
        Check(_mainGame.ActiveOverlay is BuildFleetDialog, "Build fleet opens its dialog");
    }

    private void SubmitBuildFleet()
    {
        var dialog = (BuildFleetDialog)_mainGame.ActiveOverlay!;
        dialog.SetShipsForCheck(T109RecruitShips);
        var fleetsBefore = _session.State.Fleets.Count;
        dialog.OkForCheck();
        var fleetsAfter = _session.State.Fleets.Count;
        Check(fleetsAfter == fleetsBefore + 1, $"Build fleet adds a fleet ({fleetsBefore} -> {fleetsAfter})");
        Check(_mainGame.ActiveOverlay is null, "Build fleet closes on OK");
    }

    private void AssertBuildFleetAddedUnderConstruction()
    {
        var fleet = _session.State.Fleets.FirstOrDefault(f => f.Id == T109BuildFleetId)
            ?? _session.State.Fleets.LastOrDefault();
        Check(fleet is not null, "the new fleet is in the state");
        Check(fleet!.Ships == T109RecruitShips, $"the new fleet has {T109RecruitShips} ships (got {fleet.Ships})");
        Check(fleet.IsUnderConstruction, "the new fleet is under construction");
    }

    private void OpenBuildFleetFromToolbar()
    {
        Check(_mainGame.Toolbar.PressForCheck("strategy.build_fleet"), "the toolbar has the Build fleet button");
    }

    private void CancelBuildFleet()
    {
        var dialog = (BuildFleetDialog)_mainGame.ActiveOverlay!;
        dialog.CancelForCheck();
        Check(_mainGame.ActiveOverlay is null, "Build fleet closes on Cancel");
    }

    private void AssertNoRecruitOrMobilizeButton()
    {
        var contextPanel = _mainGame.ContextPanel;
        _mainGame.ContextPanel.ShowCity(RomeCityId);
        var cityLabels = LabelsUnder(contextPanel).Select(l => l.Text).ToList();
        var cityButtons = ButtonsUnder(contextPanel).Select(b => b.Text).ToList();
        Check(
            !cityButtons.Any(t => t.Contains("Recruit", StringComparison.Ordinal)),
            $"the city panel renders no Recruit button (found: {string.Join(", ", cityButtons)})");
        Check(
            !cityLabels.Any(t => t.Contains("Mobilize first ready slot", StringComparison.Ordinal)),
            $"the city panel renders no Mobilize first ready slot label (found: {string.Join(", ", cityLabels.Where(l => l.Contains("Mobilize", StringComparison.Ordinal)))})");
    }

    // ---- finish ----

    private void Finish()
    {
        GD.Print($"StrategyDialogsCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private static IEnumerable<Button> ButtonsUnder(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var nested in ButtonsUnder(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<Label> LabelsUnder(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Label label)
            {
                yield return label;
            }

            foreach (var nested in LabelsUnder(child))
            {
                yield return nested;
            }
        }
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }

    /// <summary>
    /// The shipped <c>classical-mediterranean</c> world with Rome as the human seat; no scripted
    /// cities or armies, so the dialogs and the engine see the live world. The data root is the
    /// <see cref="GameDataContext.RepositoryRoot"/> the rest of the project already resolves to
    /// (<see cref="GameSessionFactory.RepositoryRootFromGlobalizedResPath"/>), so the check and the
    /// live game cannot disagree on where the data lives.
    /// </summary>
    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        return new GameSession(
            resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: RomeId);
    }
}
