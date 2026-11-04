using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    private sealed record StrategicView(object Map, object Details, string? Army, string? Fleet, string? City,
        BattleReportRouter.Checkpoint Reports, BattleResult[] Battles, SeatFall[] Falls, string[] Notices,
        (string Attacker, string Defender)[] BoardBattles);
    private readonly Dictionary<long, StrategicView> _strategicViews = new();
    public Button StrategicUndoButton { get; private set; } = null!;

    private void BuildStrategicUndoControl()
    {
        StrategicUndoButton = new Button { Name = "StrategicUndo", Disabled = true };
        QolCommandIcons.Apply(StrategicUndoButton, "undo", "Undo strategic action (experimental, Ctrl+Z)");
        StrategicUndoButton.Pressed += () => PerformStrategicUndo();
        Toolbar.ExtraActions.AddChild(StrategicUndoButton);
        CommandTable.Bind("game.undo", () => PerformStrategicUndo());
    }

    private void InitializeStrategicUndo()
    {
        Session.StrategicCheckpointCaptured += CaptureStrategicView;
        Session.StrategicCheckpointDiscarded += DiscardStrategicView;
        Session.StrategicCheckpointRestored += RestoreStrategicView;
        Session.StrategicUndoCleared += ClearStrategicViews;
        Session.StrategicUndoEnabled = true;
        RefreshStrategicUndoControl();
    }

    private void CaptureStrategicView(long id) => _strategicViews[id] = new(
        _mapView.CaptureUndoSelection(), _contextPanel.CaptureUndoView(), _selectedArmyId, _selectedFleetId,
        _selectedCityId, _battleReportRouter.Capture(), _pendingBattleOverlays.ToArray(),
        _pendingGameEndOverlays.ToArray(), _pendingOfferNotices.ToArray(), _boardBattleArmies.ToArray());
    private void DiscardStrategicView(long id) => _strategicViews.Remove(id);
    private void ClearStrategicViews() => _strategicViews.Clear();

    private void RestoreStrategicView(long id)
    {
        StrategicTravel.ResetAfterUndo();
        _soundPlayer.ResetAfterUndo();
        _pendingBattleOverlays.Clear(); _pendingGameEndOverlays.Clear(); _pendingOfferNotices.Clear();
        _boardBattleArmies.Clear();
        _nextTacticalOpening = Array.Empty<TacticalBattleFrame>();
        _tacticalCompletionRouting = null;
        _rallyPointPickNationId = null;
        _orderNotice.Close();
        _saveConfirmationLabel.Text = string.Empty;
        if (_strategicViews.Remove(id, out var view))
        {
            _selectedArmyId = view.Army; _selectedFleetId = view.Fleet; _selectedCityId = view.City;
            _mapView.RestoreUndoSelection(view.Map);
            _contextPanel.RestoreUndoView(view.Details);
            _battleReportRouter.Restore(view.Reports);
            foreach (var battle in view.Battles) _pendingBattleOverlays.Enqueue(battle);
            foreach (var fall in view.Falls) _pendingGameEndOverlays.Enqueue(fall);
            foreach (var notice in view.Notices) _pendingOfferNotices.Enqueue(notice);
            foreach (var battle in view.BoardBattles) _boardBattleArmies.Add(battle);
        }
        else _mapView.ClearSelection();
        RefreshTravelDisplay();
        if (_newsLog.Visible) _newsLog.Refresh();
    }

    private void RefreshStrategicUndoControl()
    {
        if (StrategicUndoButton is null) return;
        var allowed = !_closing && Session.CanUndoStrategic
            && (ActiveOverlay is null || ActiveOverlay is PeaceOfferScreen);
        StrategicUndoButton.Visible = !Session.IsObserverMode;
        StrategicUndoButton.Disabled = !allowed;
        MenuBar.SetCommandEnabled("game.undo", allowed);
    }

    public bool PerformStrategicUndo()
    {
        if (_closing || !Session.CanUndoStrategic || ActiveOverlay is not null and not PeaceOfferScreen) return false;
        if (ActiveOverlay is PeaceOfferScreen peace) CloseOverlay(peace);
        var output = Session.StrategicUndo();
        OnCommandIssued(output.Lines);
        RefreshStrategicUndoControl();
        return true;
    }

    private bool HandleStrategicUndoKey(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, Keycode: Key.Z }) return false;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return false;
        if (!PerformStrategicUndo()) return false;
        GetViewport().SetInputAsHandled();
        return true;
    }
}
