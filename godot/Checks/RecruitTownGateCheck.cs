using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Recruitment;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T155 (<c>docs/tasks/T155.md</c>) Done-when 4 and 5, headless: the real
/// <see cref="MainGameScreen"/> on the shipped classical world, Rome seated. Strategy → Recruit unit
/// lists exactly the towns the original's <c>FUN_004544E0</c> lists (the capital, a town whose
/// current fortification level is at the ruleset's
/// <see cref="RecruitmentRules.RecruitTownMinFortificationPercent"/>, or a town with units in
/// training), offers Recruit only where the order accepts, and keeps Mobilize and Disband wherever
/// units are in training; the city panel carries no Recruit control (absent, T112's information-only
/// decision) and shows the rule's own reason in a town that may not take a new order; and after each
/// of Recruit, Mobilize and Disband the dialog's training list, cost line and mobilisation label
/// match <see cref="GameSession.State"/> without reopening (bug #903).
/// </summary>
/// <remarks>
/// <para>
/// Every fixture state is reached through the engine's own orders — never by editing a fortification
/// word by hand. The training-only town is Capua: <c>order-city capua fortify 1</c> writes the
/// pending-order encoding (57 + 1×100 = 157, which <c>RecruitUnit</c>'s raw comparison accepts), a
/// recruit lands there, and one <c>end</c> completes the order to 58 — below the threshold, with a
/// unit in training, which is exactly <c>FUN_004544E0</c>'s third listing case and
/// <c>RecruitUnit</c>'s refusal case at once.
/// </para>
/// <para>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/RecruitTownGateCheck.tscn --quit-after 3600
/// </code>
/// </para>
/// </remarks>
public partial class RecruitTownGateCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 4;

    private const string RomeId = "rome";
    private const string RomeCityId = "rome";
    private const string LuceriaCityId = "luceria";
    private const string CapuaCityId = "capua";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;
    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = new GameSession(
            GameDataContext.Repository.Resolve("classical-mediterranean").World,
            GameDataContext.Repository.Resolve("classical-mediterranean").Ruleset,
            GameDataContext.Repository.Resolve("classical-mediterranean").Scenario,
            seedOverride: 1,
            humanSeatNationId: RomeId);
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
            GD.PrintErr($"RecruitTownGateCheck: unhandled exception: {ex}");
            GD.Print("RecruitTownGateCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            (InitialSettleFrames, CheckFixture),

            // Done-when 4: the dialog's town list is FUN_004544E0's.
            (BetweenStepsFrames, OpenRecruitUnitFromMenu),
            (BetweenStepsFrames, AssertRecruitUnitOpened),
            (BetweenStepsFrames, AssertTownListMatchesFun004544E0),
            (BetweenStepsFrames, AssertRecruitOfferedAtTheCapital),

            // The CLI order path refuses the same town with the rule's reason.
            (BetweenStepsFrames, AssertEngineRefusesAnIneligibleTown),

            // Reach the training-only town through real orders.
            (BetweenStepsFrames, SetupTrainingOnlyTown),
            (BetweenStepsFrames, CompleteTheFortifyOrder),

            // Done-when 4: listed for training, Recruit not offered, Disband is; the city panel
            // carries the rule's reason and no Recruit control.
            (BetweenStepsFrames, ReopenRecruitUnit),
            (BetweenStepsFrames, AssertTrainingOnlyTownListedWithRecruitNotOffered),
            (BetweenStepsFrames, AssertCityPanelCarriesTheRuleReasonInAnIneligibleTown),

            // Done-when 5 (bug #903): each submit refreshes the dialog in place.
            (BetweenStepsFrames, AssertRecruitRefreshesInPlace),
            (BetweenStepsFrames, AssertDisbandRefreshesInPlace),

            // A ready regiment for the Mobilize half: the human seat's threshold is 16 (state code),
            // +2 a week — StrategyDialogsCheck's own 9-turn readiness setup.
            (BetweenStepsFrames, EndNineTurns),
            (BetweenStepsFrames, AssertMobilizeRefreshesInPlace),
        });
    }

    // ---- setup ----

    private void CheckFixture()
    {
        var ruleset = _session.Ruleset;
        Check(ruleset.Recruitment.RecruitTownMinFortificationPercent == 75,
            "classical-faithful carries recruitTownMinFortificationPercent 75");
        Check(_session.State.NationById(RomeId)!.Control == SeatControl.Human, "Rome is the human seat");
        Check(_session.State.CityById(CapuaCityId)!.FortificationCode == 57,
            "Capua ships at a 57% fortification word (an ineligible non-capital)");
        Check(_session.State.CityById(LuceriaCityId)!.FortificationCode == 77,
            "Luceria ships at a 77% fortification word (an eligible non-capital)");
    }

    // ---- Done-when 4: the dialog's town list ----

    private void OpenRecruitUnitFromMenu() => _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");

    private void AssertRecruitUnitOpened() =>
        Check(_mainGame.ActiveOverlay is RecruitUnitDialog, "Recruit unit opens its dialog");

    private void AssertTownListMatchesFun004544E0()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        var nation = _session.State.NationById(RomeId)!;
        var expected = new List<string>();
        foreach (var city in _session.State.Cities)
        {
            if (string.Equals(city.Owner, RomeId, StringComparison.Ordinal)
                && RecruitmentEligibility.IsListedInDialog(city, nation, _session.Ruleset))
            {
                expected.Add(city.Id);
            }
        }

        // The shipped start: the capital plus the one Roman non-capital at the threshold (Luceria,
        // 77%) — and Capua (57%, no training) is not listed.
        Check(expected.SequenceEqual(new[] { RomeCityId, LuceriaCityId }),
            $"the shipped FUN_004544E0 list is rome + luceria (got {string.Join(", ", expected)})");
        Check(dialog.CityIdsForCheck.SequenceEqual(expected),
            $"the dialog lists exactly FUN_004544E0's towns (got {string.Join(", ", dialog.CityIdsForCheck)})");
    }

    private void AssertRecruitOfferedAtTheCapital()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        Check(dialog.RecruitEnabledForCheck, "Recruit is offered at the capital");
        Check(dialog.RecruitReasonForCheck.Length == 0, "no refusal reason on an offered Recruit button");
    }

    private void AssertEngineRefusesAnIneligibleTown()
    {
        var output = _session.Submit($"recruit-standing {CapuaCityId} archers 700");
        var joined = string.Join("\n", output.Lines);
        Check(
            joined.Contains("may not take a new recruitment order", StringComparison.Ordinal),
            "the CLI path refuses recruit-standing in an ineligible town with the rule's reason");
        Check(
            joined.Contains("Capua", StringComparison.Ordinal) && joined.Contains("at least 75", StringComparison.Ordinal),
            "the refusal names the town and the rule");
        Check(_session.State.NationById(RomeId)!.RecruitmentSlots.Count == 0, "the refused order queued nothing");
    }

    // ---- the training-only town, through real orders ----

    private void SetupTrainingOnlyTown()
    {
        var fortified = _session.Submit($"order-city {CapuaCityId} fortify 1");
        Check(
            fortified.Lines.Any(l => l.Contains("accepted", StringComparison.Ordinal)),
            "a 1-point fortify order at Capua is accepted (word 57 -> 157)");
        Check(_session.State.CityById(CapuaCityId)!.FortificationCode == 157,
            "Capua's raw word is 157 — a pending order over 57%");

        // RecruitUnit's raw comparison accepts the pending-order word whatever the current level.
        var recruited = _session.Submit($"recruit-standing {CapuaCityId} archers 700");
        Check(
            recruited.Lines.Any(l => l.Contains("accepted", StringComparison.Ordinal)),
            "recruit-standing at Capua is accepted while its word carries a pending order");
    }

    private void CompleteTheFortifyOrder()
    {
        _session.Submit("end");
        Check(_session.State.CityById(CapuaCityId)!.FortificationCode == 58,
            "one turn completes the 1-point order: Capua is at 58%, below the threshold");
        var slots = _session.State.NationById(RomeId)!.RecruitmentSlots;
        Check(slots.Count == 1 && string.Equals(slots[0].TargetCityId, CapuaCityId, StringComparison.Ordinal),
            "Capua keeps its unit in training after the fall");
    }

    // ---- Done-when 4: listed for training, Recruit not offered; the city panel ----

    private void ReopenRecruitUnit()
    {
        ((RecruitUnitDialog?)_mainGame.ActiveOverlay)?.CancelForCheck();
        _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");
    }

    private void AssertTrainingOnlyTownListedWithRecruitNotOffered()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        Check(dialog.CityIdsForCheck.Contains(CapuaCityId),
            "a town with units in training is listed even below the threshold (FUN_004544E0)");

        dialog.SelectCityForCheck(CapuaCityId);
        Check(!dialog.RecruitEnabledForCheck, "Recruit is not offered in the training-only town");
        Check(
            dialog.RecruitReasonForCheck.Contains("may not take a new recruitment order", StringComparison.Ordinal)
            && dialog.RecruitReasonForCheck.Contains("at least 75", StringComparison.Ordinal),
            "the disabled Recruit control carries the rule's reason");

        // Mobilize and Disband are where units are in training (Disband now; Mobilize when ready).
        Check(dialog.ModelForCheck.TrainingAtCity(CapuaCityId).Count == 1,
            "the dialog's training list shows Capua's regiment");
    }

    private void AssertCityPanelCarriesTheRuleReasonInAnIneligibleTown()
    {
        var panel = _mainGame.ContextPanel;

        panel.ShowCity(CapuaCityId);
        var capuaButtons = ButtonsUnder(panel).Select(b => b.Text).ToList();
        var capuaLabels = LabelsUnder(panel).Select(l => l.Text).ToList();
        Check(
            !capuaButtons.Any(t => t.Contains("Recruit", StringComparison.Ordinal)),
            "the city panel renders no Recruit button in an ineligible town (absent, T112)");
        Check(
            capuaLabels.Any(t => t.Contains("may not take a new recruitment order", StringComparison.Ordinal)
                && t.Contains("at least 75", StringComparison.Ordinal)),
            "the city panel shows the rule's reason in an ineligible town");

        panel.ShowCity(RomeCityId);
        var romeLabels = LabelsUnder(panel).Select(l => l.Text).ToList();
        Check(
            !romeLabels.Any(t => t.Contains("may not take a new recruitment order", StringComparison.Ordinal)),
            "the city panel shows no refusal reason at the capital");
    }

    // ---- Done-when 5: bug #903's in-place refresh ----

    private void AssertRecruitRefreshesInPlace()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        var stateBefore = _session.State.NationById(RomeId)!.RecruitmentSlots.Count;

        dialog.RecruitForCheck();

        // The dialog's own model, read without reopening: it must carry the state the submit produced.
        var slotsAfter = _session.State.NationById(RomeId)!.RecruitmentSlots;
        Check(slotsAfter.Count == stateBefore + 1, "the recruit order added a slot");
        Check(
            dialog.ModelForCheck.TrainingAtCity(RomeCityId).Count == slotsAfter.Count(s => s.TargetCityId == RomeCityId),
            "the training list reflects the new regiment without reopening (#903)");

        // The town dropdown is re-listed from the fresh model and keeps its selection (N2).
        Check(dialog.SelectedCityIdForCheck == RomeCityId, "the selected town survives the dropdown rebuild");
    }

    private void AssertDisbandRefreshesInPlace()
    {
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(CapuaCityId);
        Check(dialog.ModelForCheck.TrainingAtCity(CapuaCityId).Count == 1, "Capua's regiment is still listed");
        dialog.SelectTrainingRowForCheck(0);
        dialog.DisbandForCheck();

        var prompt = dialog.GetChildren().OfType<ConfirmPrompt>().FirstOrDefault();
        Check(prompt is ConfirmPrompt, "Disband opens the confirmation prompt");
        var yesButton = ButtonsUnder(prompt ?? (Node)dialog).FirstOrDefault(b => string.Equals(b.Text, "Yes", StringComparison.Ordinal));
        yesButton?.EmitSignal(BaseButton.SignalName.Pressed);

        Check(
            dialog.ModelForCheck.TrainingAtCity(CapuaCityId).Count == 0,
            "the training list reflects the disband without reopening (#903)");
        Check(
            dialog.ModelForCheck.TrainingAtCity(CapuaCityId).Count
                == _session.State.NationById(RomeId)!.RecruitmentSlots.Count(s => s.TargetCityId == CapuaCityId),
            "and it matches Session.State exactly (#903)");
        Check(
            !dialog.CityIdsForCheck.Contains(CapuaCityId),
            "Capua, listed only for its training, leaves the town list once its last regiment is gone (N2)");
    }

    private void EndNineTurns()
    {
        ((RecruitUnitDialog?)_mainGame.ActiveOverlay)?.CancelForCheck();

        for (var i = 0; i < 9; i++)
        {
            _session.Submit("end");
        }

        var romeSlots = _session.State.NationById(RomeId)!.RecruitmentSlots
            .Where(s => string.Equals(s.TargetCityId, RomeCityId, StringComparison.Ordinal)).ToList();
        Check(romeSlots.Count > 0 && romeSlots.All(s => s.StateCode >= 16),
            "Rome's regiment is ready after nine turns (state code >= 16)");
    }

    private void AssertMobilizeRefreshesInPlace()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.recruit_unit");
        var dialog = (RecruitUnitDialog)_mainGame.ActiveOverlay!;
        dialog.SelectCityForCheck(RomeCityId);
        var rows = dialog.ModelForCheck.TrainingAtCity(RomeCityId);
        Check(rows.Count > 0 && rows[0].IsReady, "the dialog's row is ready");
        dialog.SelectTrainingRowForCheck(0);
        Check(dialog.MobilizeTargetForCheck.Length > 0, "a selected ready row shows its mobilisation label");

        dialog.MobilizeForCheck();

        Check(
            dialog.ModelForCheck.TrainingAtCity(RomeCityId).Count == rows.Count - 1,
            "the training list reflects the mobilization without reopening (#903)");
        Check(
            dialog.ModelForCheck.TrainingAtCity(RomeCityId).Count
                == _session.State.NationById(RomeId)!.RecruitmentSlots.Count(s => s.TargetCityId == RomeCityId),
            "and it matches Session.State exactly (#903)");

        // With the row gone the selection is cleared, and the mobilisation label matches the fresh
        // model's answer for "nothing selected" — empty, not the pre-submit text (#903).
        Check(dialog.MobilizeTargetForCheck.Length == 0, "the mobilisation label is cleared after the submit");
    }

    // ---- finish ----

    private void Finish()
    {
        GD.Print($"RecruitTownGateCheck: exiting with code {(_ok ? 0 : 1)}.");
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
}
