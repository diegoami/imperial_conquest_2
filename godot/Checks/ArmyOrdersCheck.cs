using Godot;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T111 Done-when 2, 3, 7 and 8: the real <see cref="MainGameScreen"/> on a scripted classical state
/// with adjacent Roman armies, a Roman city and a Roman fleet. Every Army entry is opened from the menu
/// and commits through the engine: Transfer unit moves a unit to the partner and a two-way transfer
/// submits exactly one command; Split army moves two units with supply and money into a new army; Disband in the split dialog removes a unit; Join armies leaves one army; Change units
/// renames a unit; Disband army removes the army only after Yes. With a fleet carrying an army selected,
/// Change units renames a unit of the army aboard, and Split army opens the split dialog on the army
/// aboard as it does for a land army. An army with no free land cell opens no dialog and says why. The check also reads the regiment quality captions off the
/// laid-out army panel and unit list. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/ArmyOrdersCheck.tscn --quit-after 900
/// </code>
/// </summary>
/// <remarks>
/// The scripted world is delivered through the ordinary constructor (the classical world's own starting
/// armies, fleets and cities plus the check's added pair), and the carried army through the
/// resume-a-save seam <c>godot/Checks/MapClickCheck.cs</c> already uses — the engine's own embark is
/// unreachable in play (bug #453). <see cref="MainGameScreen.SelectArmyForCheck"/> and
/// <see cref="MainGameScreen.SelectFleetForCheck"/> set the selection a map click would.
/// </remarks>
public partial class ArmyOrdersCheck : Control
{
    private const int SettleFrames = 6;

    private const string TransferOneWayA = "t111-t1-a";
    private const string TransferOneWayB = "t111-t1-b";
    private const string TransferTwoWayA = "t111-t2-a";
    private const string TransferTwoWayB = "t111-t2-b";
    private const string SplitArmyId = "t111-split";
    private const string SplitDisbandArmyId = "t111-split-disband";
    private const string JoinA = "t111-join-a";
    private const string JoinB = "t111-join-b";
    private const string ChangeArmyId = "t111-change";
    private const string QualityArmyId = "t111-quality";
    private const string DisbandArmyId = "t111-disband";
    private const string DisbandCityId = "t111-city";
    private const string CarriedArmyId = "t111-carried";
    private const string CarryFleetId = "t111-carry-fleet";
    private const string NoFreeLandArmyId = "t111-no-free-land";
    private const string NoFreeLandAboardArmyId = "t111-no-free-aboard";
    private const string NoFreeLandFleetId = "t111-no-free-fleet";
    private const string NoFreeLandOneUnitArmyId = "t111-no-free-one-unit";
    private const string NoFreeLandOneUnitFleetId = "t111-no-free-one-fleet";
    private const string Rome = "rome";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private bool _ok = true;

    private int _commandsSeen;
    private string _splitTileDescription = string.Empty;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _mainGame.CommandIssued += _ => _commandsSeen++;

        _steps.Add(CheckFixture);
        _steps.Add(OpenTransferOneWay);
        _steps.Add(OpenTransferTwoWay);
        _steps.Add(SplitArmy);
        _steps.Add(SplitArmyDisband);
        _steps.Add(TransferEmptySelection);
        _steps.Add(JoinArmies);
        _steps.Add(ChangeUnits);
        _steps.Add(ChangeUnitsEmptySelection);
        _steps.Add(DisbandNo);
        _steps.Add(DisbandYes);
        _steps.Add(ChangeUnitsOfCarriedArmy);
        _steps.Add(SplitCarriedArmyFromFleet);
        _steps.Add(SplitCarriedArmyFromArmy);
        _steps.Add(SplitNoFreeLandArmy);
        _steps.Add(SplitNoFreeLandAboard);
        _steps.Add(SplitNoFreeLandOneUnit);
        _steps.Add(QualityCaptions);
        _steps.Add(NoDisbandButton);
        _steps.Add(Finish);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < SettleFrames || _stepIndex >= _steps.Count)
        {
            return;
        }

        _frame = 0;
        try
        {
            _steps[_stepIndex++]();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ArmyOrdersCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    private void CheckFixture()
    {
        Check(_session.State.NationById(Rome)!.Control == SeatControl.Human, "Rome is the human seat");
        Check(AdjacentArmies(TransferOneWayA, TransferOneWayB), "the one-way transfer pair is adjacent");
        Check(AdjacentArmies(TransferTwoWayA, TransferTwoWayB), "the two-way transfer pair is adjacent");
        Check(AdjacentArmies(JoinA, JoinB), "the join pair is adjacent");
        Check(
            _session.State.ArmyById(SplitArmyId)!.Units.Count >= 3,
            "the split army has three units");
        Check(_splitTileDescription.Length > 0, $"the split army stands on free ground: {_splitTileDescription}");
    }

    private void OpenTransferOneWay()
    {
        _mainGame.SelectArmyForCheck(TransferOneWayA);
        var before = _commandsSeen;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_transfer_unit"), "Transfer unit is wired from the menu");
        Check(_mainGame.ActiveOverlay is ArmyTransferDialog, "Transfer unit opens its dialog");
        Check(_commandsSeen == before, "opening Transfer unit issues no command");

        var dialog = (ArmyTransferDialog)_mainGame.ActiveOverlay!;
        var moved = _session.State.ArmyById(TransferOneWayA)!.Units[0];
        Check(dialog.TransferBySourceForCheck(TransferOneWayA, 0), "the dialog stages a unit to the partner");
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"OK submits exactly one command ({_commandsSeen - before})");
        Check(_mainGame.ActiveOverlay is null, "OK closes the dialog");
        var partner = _session.State.ArmyById(TransferOneWayB)!;
        Check(partner.Units.Any(unit => unit == moved), "the staged unit is in the partner army");
        Check(
            !_session.State.ArmyById(TransferOneWayA)!.Units.Any(unit => unit == moved),
            "the staged unit left the selected army");
    }

    private void OpenTransferTwoWay()
    {
        _mainGame.SelectArmyForCheck(TransferTwoWayA);
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_transfer_unit"), "Transfer unit opens for the two-way pair");
        var dialog = (ArmyTransferDialog)_mainGame.ActiveOverlay!;

        var fromA = _session.State.ArmyById(TransferTwoWayA)!.Units[0];
        var fromB = _session.State.ArmyById(TransferTwoWayB)!.Units[0];
        Check(dialog.TransferBySourceForCheck(TransferTwoWayA, 0), "the dialog stages a unit A -> B");
        Check(dialog.TransferBySourceForCheck(TransferTwoWayB, 0), "the dialog stages a unit B -> A");

        var before = _commandsSeen;
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"the two-way OK submits exactly one command ({_commandsSeen - before})");
        Check(
            _session.State.ArmyById(TransferTwoWayB)!.Units.Any(unit => unit == fromA),
            "A's unit ends in B");
        Check(
            _session.State.ArmyById(TransferTwoWayA)!.Units.Any(unit => unit == fromB),
            "B's unit ends in A");
    }

    /// <summary>
    /// Done-when 2: Split army moving two units, supply and money, as one <c>split-army</c>. The new army
    /// holds exactly those two units and that money, and its supply — read after T141's rebalance — leaves
    /// the two armies' total unchanged.
    /// </summary>
    private void SplitArmy()
    {
        _mainGame.SelectArmyForCheck(SplitArmyId);
        var before = _commandsSeen;
        var parentBefore = _session.State.ArmyById(SplitArmyId)!;
        var movedUnits = new[] { parentBefore.Units[0], parentBefore.Units[2] };
        var supplyTotal = parentBefore.SupplyTons;
        var moneyTotal = parentBefore.Money;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_split"), "Split army is wired from the menu");
        Check(_mainGame.ActiveOverlay is SplitArmyDialog, "Split army opens its dialog");
        Check(_commandsSeen == before, "opening Split army issues no command");

        var dialog = (SplitArmyDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.CanSplit, "the split dialog offers the split");
        dialog.StageUnitForCheck(0);
        dialog.StageUnitForCheck(2);
        dialog.PressSupplyForCheck(ArmyDialogModels.SupplyStepTons);
        dialog.PressSupplyForCheck(ArmyDialogModels.SupplyStepTons);
        dialog.PressMoneyForCheck(ArmyDialogModels.MoneyLargeStepTalents);
        Check(dialog.ModelForCheck.Supply == 20 && dialog.ModelForCheck.Money == 100, "the spinners moved 20 t and 100 talents");
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"Split army submits exactly one command ({_commandsSeen - before})");
        var child = _session.State.ArmyById($"{SplitArmyId}-split");
        var parent = _session.State.ArmyById(SplitArmyId)!;
        Check(child is not null, "Split army adds an army");
        if (child is null)
        {
            return;
        }

        Check(
            child.Units.Count == 2 && movedUnits.All(unit => child.Units.Contains(unit)),
            "the new army holds exactly the two staged units");
        Check(
            parent.Units.Count == 1 && parent.Units[0] == parentBefore.Units[1],
            "the selected army keeps the unit that stayed");
        Check(child.Money == 100 && parent.Money == moneyTotal - 100, "the new army holds the 100 talents moved");
        Check(
            child.SupplyTons + parent.SupplyTons == supplyTotal,
            $"the two armies' supply total is unchanged ({parent.SupplyTons} + {child.SupplyTons} vs {supplyTotal})");
    }

    /// <summary>
    /// Done-when 2: a Disband inside the split dialog, under each list — once on an unstaged unit and once
    /// on a staged one — removes that unit from the selected army and counts one command per press. The
    /// real Disband buttons are pressed (found in the laid-out dialog, fired through their own
    /// <c>pressed</c> signal) on a row selected in the list, so removing a button, or breaking the
    /// selection-to-index mapping, fails this check. A press with nothing selected does nothing.
    /// </summary>
    private void SplitArmyDisband()
    {
        _mainGame.SelectArmyForCheck(SplitDisbandArmyId);
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_split"), "Split army opens for the disband army");
        var dialog = (SplitArmyDialog)_mainGame.ActiveOverlay!;
        var original = _session.State.ArmyById(SplitDisbandArmyId)!.Units.ToList();

        var disbandButtons = ButtonsUnder(dialog).Where(button => button.Text == "Disband").ToList();
        var transferButtons = ButtonsUnder(dialog).Where(button => button.Text == "Transfer").ToList();
        Check(
            disbandButtons.Count == 2 && transferButtons.Count == 2,
            $"the split dialog has a Disband and a Transfer button under each list ({disbandButtons.Count}, {transferButtons.Count})");
        if (disbandButtons.Count != 2 || transferButtons.Count != 2)
        {
            dialog.CancelForCheck();
            return;
        }

        // Nothing selected: Disband and Transfer under either list do nothing.
        var before = _commandsSeen;
        dialog.ArmyListForCheck.DeselectAll();
        dialog.NewArmyListForCheck.DeselectAll();
        foreach (var button in disbandButtons.Concat(transferButtons))
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        Check(_commandsSeen == before, $"Disband and Transfer with no selected row submit nothing ({_commandsSeen - before})");
        Check(
            _session.State.ArmyById(SplitDisbandArmyId)!.Units.Count == original.Count
            && dialog.ModelForCheck.StagedUnits.Count == 0,
            "Disband and Transfer with no selected row change and stage nothing");

        // Under the selected army's own list: select the row of unit 3 (row 3, nothing staged), press Disband.
        dialog.ArmyListForCheck.Select(3);
        before = _commandsSeen;
        disbandButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
        var afterFirst = _session.State.ArmyById(SplitDisbandArmyId)!;
        Check(_commandsSeen == before + 1, $"Disband under the army's list counts one command ({_commandsSeen - before})");
        Check(
            afterFirst.Units.Count == original.Count - 1 && !afterFirst.Units.Contains(original[3]),
            "the unstaged unit is gone from the selected army");

        // Stage units 1 and 2 with the real Transfer button, then Disband the staged row of unit 1.
        dialog.ArmyListForCheck.Select(1);
        transferButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
        dialog.ArmyListForCheck.Select(1); // unit 2 is now row 1 of the unstaged list [0, 2].
        transferButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
        Check(
            dialog.ModelForCheck.StagedUnits.SequenceEqual(new[] { 1, 2 }),
            "Transfer under the army's list stages the selected rows");

        dialog.NewArmyListForCheck.Select(0); // the staged row of unit 1.
        before = _commandsSeen;
        disbandButtons[1].EmitSignal(BaseButton.SignalName.Pressed);
        var afterSecond = _session.State.ArmyById(SplitDisbandArmyId)!;
        Check(_commandsSeen == before + 1, $"Disband under the new army's list counts one command ({_commandsSeen - before})");
        Check(
            afterSecond.Units.Count == original.Count - 2 && !afterSecond.Units.Contains(original[1]),
            "the staged unit is gone from the selected army");
        Check(
            dialog.ModelForCheck.StagedUnits.Count == 1
            && afterSecond.Units[dialog.ModelForCheck.StagedUnits[0]] == original[2],
            "the staging re-reads its indexes from the new state");

        before = _commandsSeen;
        dialog.CancelForCheck();
        Check(_commandsSeen == before, "Cancel submits nothing");
        Check(
            _session.State.ArmyById(SplitDisbandArmyId)!.Units.Count == original.Count - 2
            && _session.State.ArmyById($"{SplitDisbandArmyId}-split") is null,
            "Cancel leaves the disbands in place and adds no army");
    }

    /// <summary>
    /// The Transfer unit dialog's Disband and Transfer buttons with nothing selected do nothing — the
    /// empty selection must not fall back to row 0.
    /// </summary>
    private void TransferEmptySelection()
    {
        _mainGame.SelectArmyForCheck(TransferTwoWayA);
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_transfer_unit"), "Transfer unit opens for the empty-selection press");
        var dialog = (ArmyTransferDialog)_mainGame.ActiveOverlay!;
        var before = _commandsSeen;
        var unitsBefore = _session.State.ArmyById(TransferTwoWayA)!.Units.Count;
        dialog.SelectedListForCheck.DeselectAll();
        dialog.PartnerListForCheck.DeselectAll();
        foreach (var button in ButtonsUnder(dialog).Where(b => b.Text is "Disband" or "Transfer"))
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        Check(_commandsSeen == before, $"Transfer unit's Disband and Transfer with no selection submit nothing ({_commandsSeen - before})");
        Check(
            dialog.ModelForCheck.UnitsToPartner.Count == 0 && dialog.ModelForCheck.UnitsBack.Count == 0,
            "Transfer unit's buttons with no selection stage nothing");
        Check(_session.State.ArmyById(TransferTwoWayA)!.Units.Count == unitsBefore, "no unit was disbanded");
        dialog.CancelForCheck();
    }

    private void JoinArmies()
    {
        var before = _commandsSeen;
        var armiesBefore = _session.State.Armies.Count;
        _mainGame.SelectArmyForCheck(JoinA);
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_join"), "Join armies is wired from the menu");

        Check(_commandsSeen == before + 1, $"Join armies submits exactly one command ({_commandsSeen - before})");
        Check(_mainGame.ActiveOverlay is null, "Join armies opens no dialog");
        Check(_session.State.ArmyById(JoinB) is null, "the absorbed army is gone");
        Check(_session.State.Armies.Count == armiesBefore - 1, "Join armies leaves one army");
    }

    private void ChangeUnits()
    {
        _mainGame.SelectArmyForCheck(ChangeArmyId);
        var before = _commandsSeen;
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_change_units"),
            "Change units is wired from the menu");
        Check(_mainGame.ActiveOverlay is ChangeUnitsDialog, "Change units opens its dialog");

        var dialog = (ChangeUnitsDialog)_mainGame.ActiveOverlay!;
        dialog.RenameUnitForCheck(1, "Legio I");

        Check(_commandsSeen == before + 1, $"Change units renames through one command ({_commandsSeen - before})");
        Check(
            _session.State.ArmyById(ChangeArmyId)!.Units[1].Name == "Legio I",
            "the unit carries the new name");
        dialog.CancelForCheck();
    }

    /// <summary>
    /// Change units' real buttons with nothing selected (Rename unit, Split unit, Join units, Disband, and
    /// the Rename and Split panels' own buttons) submit nothing and change nothing.
    /// </summary>
    private void ChangeUnitsEmptySelection()
    {
        _mainGame.SelectArmyForCheck(ChangeArmyId);
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_change_units"), "Change units opens for the empty-selection press");
        var dialog = (ChangeUnitsDialog)_mainGame.ActiveOverlay!;
        var before = _commandsSeen;
        var armyBefore = _session.State.ArmyById(ChangeArmyId)!;

        var buttons = ButtonsUnder(dialog)
            .Where(button => button.Text is "Rename unit" or "Split unit" or "Join units" or "Disband" or "Rename" or "Split")
            .ToList();
        Check(buttons.Count == 6, $"Change units has its six selection-reading buttons ({buttons.Count})");

        dialog.UnitListForCheck.DeselectAll();
        foreach (var button in buttons)
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        Check(_commandsSeen == before, $"Change units' buttons with no selected unit submit nothing ({_commandsSeen - before})");
        Check(_session.State.ArmyById(ChangeArmyId)! == armyBefore, "Change units' buttons with no selected unit change nothing");
        dialog.CancelForCheck();
    }

    private void DisbandNo()
    {
        _mainGame.SelectArmyForCheck(DisbandArmyId);
        var before = _commandsSeen;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_disband"), "Disband army is wired from the menu");
        Check(_mainGame.ActiveOverlay is ConfirmPrompt, "Disband army asks first");

        PressPromptAnswer("No");

        Check(_commandsSeen == before, $"No submits nothing ({_commandsSeen - before})");
        Check(_session.State.ArmyById(DisbandArmyId) is not null, "the army survives No");
        Check(_mainGame.ActiveOverlay is null, "No closes the prompt");
    }

    private void DisbandYes()
    {
        _mainGame.SelectArmyForCheck(DisbandArmyId);
        var before = _commandsSeen;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_disband"), "Disband army asks again");

        PressPromptAnswer("Yes");

        Check(_commandsSeen == before + 1, $"Yes submits one command ({_commandsSeen - before})");
        Check(_session.State.ArmyById(DisbandArmyId) is null, "the army is gone after Yes");
    }

    /// <summary>Done-when 3: with the carrying fleet selected, Change units renames a unit of the army aboard.</summary>
    private void ChangeUnitsOfCarriedArmy()
    {
        _mainGame.SelectFleetForCheck(CarryFleetId);
        var before = _commandsSeen;
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_change_units"),
            "Change units opens with a fleet selected");
        Check(_mainGame.ActiveOverlay is ChangeUnitsDialog, "Change units opens its dialog for the carried army");

        var dialog = (ChangeUnitsDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ArmyId == CarriedArmyId, $"Change units targets the army aboard ({dialog.ArmyId})");
        dialog.RenameUnitForCheck(0, "Legio Aboard");

        Check(_commandsSeen == before + 1, $"the rename is one command ({_commandsSeen - before})");
        Check(
            _session.State.ArmyById(CarriedArmyId)!.Units[0].Name == "Legio Aboard",
            "the army aboard carries the new unit name");
        dialog.CancelForCheck();
    }

    /// <summary>
    /// Done-when 3: with the carrying fleet selected, Split army opens the split dialog on the army
    /// aboard. Staging one unit and OK submits exactly one <c>split-army</c>; the army aboard keeps one
    /// unit fewer and is still aboard the same fleet, and the new army stands on land one tile away, not
    /// aboard, on the cell the engine's own scan (<c>SplitPlacement.ArmyCellFor</c>) gave before the
    /// order.
    /// </summary>
    private void SplitCarriedArmyFromFleet()
    {
        _mainGame.SelectFleetForCheck(CarryFleetId);
        var before = _commandsSeen;
        var aboardBefore = _session.State.ArmyById(CarriedArmyId)!;
        var fleetBefore = _session.State.FleetById(CarryFleetId)!;
        var expectedCell = IC2.Engine.Armies.SplitPlacement.ArmyCellFor(
            _session.State, _session.World, aboardBefore);
        Check(expectedCell is not null, "the carrying fleet has a free land cell for the new army");
        var idsBefore = _session.State.Armies.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed with the carrying fleet selected");
        Check(_mainGame.ActiveOverlay is SplitArmyDialog, "Split army opens its dialog for the army aboard");
        var dialog = (SplitArmyDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ArmyId == CarriedArmyId, $"Split army targets the army aboard ({dialog.ArmyId})");
        dialog.StageUnitForCheck(0);
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"the aboard split submits exactly one command ({_commandsSeen - before})");
        var parent = _session.State.ArmyById(CarriedArmyId)!;
        Check(parent.Units.Count == aboardBefore.Units.Count - 1, "the army aboard keeps one unit fewer");
        Check(
            parent.IsEmbarked && parent.AboardFleetId == CarryFleetId,
            "the army aboard is still aboard the same fleet");
        Check(
            _session.State.FleetById(CarryFleetId)!.CarriedArmyId == CarriedArmyId,
            "the fleet still carries it");
        var child = _session.State.Armies.FirstOrDefault(a => !idsBefore.Contains(a.Id));
        Check(child is not null, "Split army adds a new army");
        if (child is not null && expectedCell is not null)
        {
            Check(
                child.X == expectedCell.Value.X && child.Y == expectedCell.Value.Y,
                $"the new army stands at the scan's cell ({child.X},{child.Y})");
            Check(!child.IsEmbarked && child.AboardFleetId is null, "the new army is not aboard");
            Check(
                LandingTile.ChebyshevDistance(
                    new GridPoint(child.X, child.Y), new GridPoint(fleetBefore.X, fleetBefore.Y)) == 1,
                "the new army stands one tile from the carrying fleet");
        }
    }

    /// <summary>
    /// Done-when 3, the other route: with the embarked army itself selected (not its fleet), Split army
    /// opens the same dialog on that army; staging one unit and OK submits exactly one <c>split-army</c>
    /// with the same result (Sol's review of PR #765, R1).
    /// </summary>
    private void SplitCarriedArmyFromArmy()
    {
        _mainGame.SelectArmyForCheck(CarriedArmyId);
        var before = _commandsSeen;
        var aboardBefore = _session.State.ArmyById(CarriedArmyId)!;
        var fleetBefore = _session.State.FleetById(CarryFleetId)!;
        var expectedCell = IC2.Engine.Armies.SplitPlacement.ArmyCellFor(
            _session.State, _session.World, aboardBefore);
        Check(expectedCell is not null, "the selected aboard army still has a free land cell");
        var idsBefore = _session.State.Armies.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed with the aboard army selected");
        Check(
            _mainGame.ActiveOverlay is SplitArmyDialog,
            "Split army opens its dialog for the selected aboard army");
        var dialog = (SplitArmyDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ArmyId == CarriedArmyId, $"Split army targets the selected aboard army ({dialog.ArmyId})");
        dialog.StageUnitForCheck(0);
        dialog.OkForCheck();

        Check(
            _commandsSeen == before + 1,
            $"the aboard split from the army route submits exactly one command ({_commandsSeen - before})");
        var parent = _session.State.ArmyById(CarriedArmyId)!;
        Check(parent.Units.Count == aboardBefore.Units.Count - 1, "the selected army keeps one unit fewer");
        Check(
            parent.IsEmbarked && parent.AboardFleetId == CarryFleetId,
            "the selected army is still aboard the same fleet");
        var child = _session.State.Armies.FirstOrDefault(a => !idsBefore.Contains(a.Id));
        Check(child is not null, "Split army adds a new army from the army route");
        if (child is not null && expectedCell is not null)
        {
            Check(
                child.X == expectedCell.Value.X && child.Y == expectedCell.Value.Y,
                $"the new army stands at the scan's cell ({child.X},{child.Y})");
            Check(!child.IsEmbarked && child.AboardFleetId is null, "the new army is not aboard");
            Check(
                LandingTile.ChebyshevDistance(
                    new GridPoint(child.X, child.Y), new GridPoint(fleetBefore.X, fleetBefore.Y)) == 1,
                "the new army stands one tile from the carrying fleet");
        }
    }

    /// <summary>
    /// Done-when 8: an own land army whose eight neighbours are all occupied opens no dialog, shows the
    /// screen's no-free-tile message and submits nothing.
    /// </summary>
    private void SplitNoFreeLandArmy()
    {
        _mainGame.SelectArmyForCheck(NoFreeLandArmyId);
        var before = _commandsSeen;
        var armiesBefore = _session.State.Armies.Count;
        Check(
            IC2.Engine.Armies.SplitPlacement.ArmyCellFor(
                _session.State, _session.World, _session.State.ArmyById(NoFreeLandArmyId)!) is null,
            "the landlocked army has no free land cell");
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed on the landlocked army");
        Check(_mainGame.ActiveOverlay is null, "Split army opens no dialog with no free land cell");
        Check(
            _mainGame.LastMessageForCheck == ArmyDialogModels.SplitNoFreeTileMessage,
            $"Split army says why ('{_mainGame.LastMessageForCheck}')");
        Check(_commandsSeen == before, $"Split army submits nothing ({_commandsSeen - before})");
        Check(_session.State.Armies.Count == armiesBefore, "no army was added");
    }

    /// <summary>
    /// Done-when 8: an army aboard a fleet with no free land cell in the fleet's 3 × 3 opens no dialog,
    /// shows the screen's no-free-tile message and submits nothing — from the fleet and from the army.
    /// </summary>
    private void SplitNoFreeLandAboard()
    {
        _mainGame.SelectFleetForCheck(NoFreeLandFleetId);
        var before = _commandsSeen;
        var armiesBefore = _session.State.Armies.Count;
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed on the open-sea carrying fleet");
        Check(_mainGame.ActiveOverlay is null, "Split army opens no dialog for a fleet with no free land cell");
        Check(
            _mainGame.LastMessageForCheck == ArmyDialogModels.SplitNoFreeTileMessage,
            $"Split army says why for the fleet ('{_mainGame.LastMessageForCheck}')");
        Check(_commandsSeen == before, $"Split army submits nothing for the fleet ({_commandsSeen - before})");
        Check(_session.State.Armies.Count == armiesBefore, "no army was added for the fleet");

        _mainGame.SelectArmyForCheck(NoFreeLandAboardArmyId);
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed on the open-sea aboard army");
        Check(
            _mainGame.ActiveOverlay is null && _commandsSeen == before,
            "Split army on the selected aboard army opens nothing and submits nothing");
        Check(
            _mainGame.LastMessageForCheck == ArmyDialogModels.SplitNoFreeTileMessage,
            $"Split army says why for the aboard army ('{_mainGame.LastMessageForCheck}')");
    }

    /// <summary>
    /// R2 (Opus review of PR #784): a one-unit army with no free land cell gets T111's own one-unit
    /// refusal before the no-free-land pre-check — the original checks the unit count first — so the
    /// dialog opens and says why, and nothing is submitted.
    /// </summary>
    private void SplitNoFreeLandOneUnit()
    {
        _mainGame.SelectArmyForCheck(NoFreeLandOneUnitArmyId);
        var before = _commandsSeen;
        Check(
            IC2.Engine.Armies.SplitPlacement.ArmyCellFor(
                _session.State, _session.World, _session.State.ArmyById(NoFreeLandOneUnitArmyId)!) is null,
            "the one-unit aboard army has no free land cell");
        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.army_split"),
            "Split army is pressed on the one-unit army with no free land cell");
        Check(
            _mainGame.ActiveOverlay is SplitArmyDialog,
            "Split army opens the dialog for a one-unit army even with no free land cell");
        var dialog = (SplitArmyDialog)_mainGame.ActiveOverlay!;
        Check(
            dialog.ModelForCheck.RefusalMessage == ArmyDialogModels.SplitOneUnitRefusal,
            $"the one-unit army gets T111's refusal ('{dialog.ModelForCheck.RefusalMessage}')");
        Check(
            _commandsSeen == before,
            $"Split army submits nothing for the one-unit army ({_commandsSeen - before})");
        dialog.CancelForCheck();
    }

    /// <summary>
    /// Done-when 7: the army panel and the unit list each print every unit's regiment quality caption.
    /// The army's units span tiers 5 (poor), 6 (average) and 8 (very good).
    /// </summary>
    private void QualityCaptions()
    {
        _mainGame.SelectArmyForCheck(QualityArmyId);
        _mainGame.ContextPanel.ShowArmy(QualityArmyId);
        var panelLabels = LabelsUnder(_mainGame.ContextPanel).Select(label => label.Text).ToList();
        foreach (var (quality, caption) in new[] { (5, "poor"), (6, "average"), (8, "very good") })
        {
            Check(
                panelLabels.Any(text => text.Contains($"({caption})", StringComparison.Ordinal)),
                $"the army panel shows quality {quality} as '{caption}'");
        }

        _mainGame.ContextPanel.ShowUnitList(MapEntityKind.Army, QualityArmyId);
        var unitListLabels = LabelsUnder(_mainGame.ContextPanel).Select(label => label.Text).ToList();
        Check(
            unitListLabels.Any(text => text.Contains("(poor)", StringComparison.Ordinal))
            && unitListLabels.Any(text => text.Contains("(average)", StringComparison.Ordinal))
            && unitListLabels.Any(text => text.Contains("(very good)", StringComparison.Ordinal)),
            "the unit list shows each unit's quality caption");
    }

    /// <summary>Done-when 5: the context panel's army Disband button is gone.</summary>
    private void NoDisbandButton()
    {
        _mainGame.ContextPanel.ShowArmy(QualityArmyId);
        var disband = ButtonsUnder(_mainGame.ContextPanel)
            .Select(button => button.Text)
            .Where(text => text.Contains("Disband", StringComparison.Ordinal))
            .ToList();
        Check(
            disband.Count == 0,
            $"no ContextPanel button reads Disband (found {string.Join(", ", disband)})");
    }

    private void Finish()
    {
        GD.Print($"ArmyOrdersCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private void PressPromptAnswer(string answer)
    {
        var button = ButtonsUnder(_mainGame.ActiveOverlay!)
            .FirstOrDefault(candidate => string.Equals(candidate.Text, answer, StringComparison.Ordinal));
        Check(button is not null, $"the confirmation prompt has a '{answer}' button");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private bool AdjacentArmies(string firstId, string secondId)
    {
        var first = _session.State.ArmyById(firstId);
        var second = _session.State.ArmyById(secondId);
        return first is not null
            && second is not null
            && LandingTile.ChebyshevDistance(new GridPoint(first.X, first.Y), new GridPoint(second.X, second.Y)) == 1;
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

    /// <summary>
    /// The shipped classical world with Rome as the human seat and the check's own armies, a Roman city
    /// and a Roman fleet added; the carried army is embarked by resuming a save, the seam
    /// <c>godot/Checks/MapClickCheck.cs</c> uses.
    /// </summary>
    private GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        var occupied = new HashSet<(int X, int Y)>();
        foreach (var army in resolved.World.StartingArmies)
        {
            occupied.Add((army.X, army.Y));
        }

        foreach (var city in resolved.World.Cities)
        {
            occupied.Add((city.X, city.Y));
        }

        foreach (var fleet in resolved.World.StartingFleets)
        {
            occupied.Add((fleet.X, fleet.Y));
        }

        // The two-unit aboard no-free-land fixture claims (2,2); keep the one-unit fixture's own open-sea
        // search off that tile.
        occupied.Add((2, 2));
        var landlessTile = FindLandlessTile(resolved.World, occupied)
            ?? throw new InvalidOperationException("No open-sea tile on the classical map fits a landless army.");

        var splitTile = FindSplitTile(resolved.World, occupied)
            ?? throw new InvalidOperationException("No tile on the classical map fits a split army.");

        var armies = resolved.World.StartingArmies.ToList();
        armies.Add(RomanArmy(TransferOneWayA, 100, 100, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));
        armies.Add(RomanArmy(TransferOneWayB, 100, 99, ("light_cavalry", 4_000, 7)));
        armies.Add(RomanArmy(TransferTwoWayA, 102, 100, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));
        armies.Add(RomanArmy(TransferTwoWayB, 102, 99, ("light_cavalry", 4_000, 7)));
        armies.Add(RomanArmy(
            SplitArmyId, splitTile.X, splitTile.Y,
            ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8), ("light_cavalry", 2_000, 7)));
        armies.Add(RomanArmy(
            SplitDisbandArmyId, 95, 100,
            ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8), ("light_cavalry", 2_000, 7), ("light_infantry", 1_000, 6)));
        armies.Add(RomanArmy(JoinA, 104, 104, ("heavy_infantry", 5_000, 6)));
        armies.Add(RomanArmy(JoinB, 104, 105, ("archers", 3_000, 6)));
        armies.Add(RomanArmy(ChangeArmyId, 95, 110, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));
        armies.Add(RomanArmy(
            QualityArmyId, 95, 120,
            ("light_infantry", 2_000, 5), ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));
        armies.Add(RomanArmy(DisbandArmyId, 90, 90, ("heavy_infantry", 5_000, 6)));
        armies.Add(RomanArmy(
            CarriedArmyId, 100, 120,
            ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8), ("light_cavalry", 2_000, 7)));

        // (56,3) is inland and clear; its eight neighbours are all held by blockers, so the split scan
        // finds no cell. (2,2) is open sea, so an army aboard a fleet there has no land neighbour.
        armies.Add(RomanArmy(NoFreeLandArmyId, 56, 3, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                armies.Add(RomanArmy(
                    $"t111-no-free-block-{dx + 1}{dy + 1}", 56 + dx, 3 + dy,
                    ("light_infantry", 1_000, 6)));
            }
        }

        armies.Add(RomanArmy(
            NoFreeLandAboardArmyId, 2, 2,
            ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8)));

        // R2 (Opus review of PR #784): one unit and no free land cell. The original checks the unit count
        // before the placement, so this army must get T111's one-unit refusal, not the no-free-tile message.
        armies.Add(RomanArmy(
            NoFreeLandOneUnitArmyId, landlessTile.X, landlessTile.Y, ("light_infantry", 1_000, 6)));

        var cities = resolved.World.Cities.ToList();
        cities.Add(new CityDefinition(
            DisbandCityId, "T111 disband city", X: 91, Y: 90, Owner: Rome, Allegiance: Rome,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 50, PopulationThousands: 100,
            MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty));

        var fleets = resolved.World.StartingFleets.ToList();
        fleets.Add(new StartingFleet(
            CarryFleetId, Rome, 100, 120, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: 300, Moves: 8));
        fleets.Add(new StartingFleet(
            NoFreeLandFleetId, Rome, 2, 2, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: 300, Moves: 8));
        fleets.Add(new StartingFleet(
            NoFreeLandOneUnitFleetId, Rome, landlessTile.X, landlessTile.Y,
            Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: 300, Moves: 8));

        var world = resolved.World with
        {
            StartingArmies = ValueList.From(armies),
            Cities = ValueList.From(cities),
            StartingFleets = ValueList.From(fleets),
        };

        _splitTileDescription = $"({splitTile.X}, {splitTile.Y})";

        var initial = new GameSession(
            world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: Rome);

        // Embark the carried armies by resuming a save that carries the links (bug #453's unreachable
        // embark).
        var state = initial.State;
        var embarkedArmies = state.Armies
            .Select(a => a.Id switch
            {
                CarriedArmyId => a with
                {
                    AboardFleetId = CarryFleetId, CoveredTileCode = null, X = 100, Y = 120,
                },
                NoFreeLandAboardArmyId => a with
                {
                    AboardFleetId = NoFreeLandFleetId, CoveredTileCode = null, X = 2, Y = 2,
                },
                NoFreeLandOneUnitArmyId => a with
                {
                    AboardFleetId = NoFreeLandOneUnitFleetId, CoveredTileCode = null,
                    X = landlessTile.X, Y = landlessTile.Y,
                },
                _ => a,
            })
            .ToList();
        var embarkedFleets = state.Fleets
            .Select(f => f.Id switch
            {
                CarryFleetId => f with { CarriedArmyId = CarriedArmyId, X = 100, Y = 120 },
                NoFreeLandFleetId => f with { CarriedArmyId = NoFreeLandAboardArmyId, X = 2, Y = 2 },
                NoFreeLandOneUnitFleetId => f with
                {
                    CarriedArmyId = NoFreeLandOneUnitArmyId, X = landlessTile.X, Y = landlessTile.Y,
                },
                _ => f,
            })
            .ToList();
        var embarked = state with
        {
            Armies = ValueList.From(embarkedArmies),
            Fleets = ValueList.From(embarkedFleets),
        };

        var save = new SaveGame(
            state.SchemaVersion,
            "army-orders-check",
            "army-orders-check",
            state.ScenarioId,
            state.WorldId,
            state.RulesetId,
            embarked);

        return new GameSession(world, resolved.Ruleset, resolved.Scenario, save);
    }

    /// <summary>
    /// The first map tile whose 3×3 block has a passable, unoccupied neighbour for a split army — the
    /// engine's own <see cref="IC2.Engine.Armies.SplitPlacement"/> scan. The scan starts at the top-left,
    /// so the split army lands far from the check's other pairs.
    /// </summary>
    private static (int X, int Y)? FindSplitTile(World world, HashSet<(int X, int Y)> occupied)
    {
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                if (occupied.Contains((x, y)))
                {
                    continue;
                }

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height)
                        {
                            continue;
                        }

                        if (!occupied.Contains((nx, ny))
                            && LandingTile.IsPassableForArmy(new GridPoint(nx, ny), world))
                        {
                            return (x, y);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The first unoccupied map tile whose eight neighbours include no army-passable cell — an open-sea
    /// tile, so an army aboard a fleet there has no free land cell (the R2 one-unit fixture). The centre
    /// itself is not tested: the split scan only offers the neighbours.
    /// </summary>
    private static (int X, int Y)? FindLandlessTile(World world, HashSet<(int X, int Y)> occupied)
    {
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                if (occupied.Contains((x, y)))
                {
                    continue;
                }

                var landless = true;
                for (var dy = -1; dy <= 1 && landless; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height)
                        {
                            continue;
                        }

                        if (LandingTile.IsPassableForArmy(new GridPoint(nx, ny), world))
                        {
                            landless = false;
                            break;
                        }
                    }
                }

                if (landless)
                {
                    return (x, y);
                }
            }
        }

        return null;
    }

    private static StartingArmy RomanArmy(
        string id, int x, int y, params (string Type, int Troops, int Quality)[] units) => new(
        id, Rome, x, y, Morale: 70, Money: 100, SupplyTons: 100, Moves: 8,
        Units: ValueList.From(units.Select((unit, index) =>
            new UnitSlot(0, unit.Type, unit.Troops, unit.Quality, $"T111 {id} unit {index + 1}"))));

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
