using IC2.Slice.Diagnostics;
using Godot;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// T127: the tactical battle's screen (<c>docs/game-design.md</c>, "The battle screen" and the User interface's item 3):
/// the 14 × 12 board of 32 px cells drawn from the engine's icon codes (tinted per side's nation), the selection cursor,
/// the caption, the eight-button toolbar, the side panel (the selected unit and the last exchange) and the three unit
/// lists. It takes the human's mouse orders and sends each as T125's matching order; the engine decides whether it is
/// legal and this screen shows what it answers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No rule lives here.</strong> Every cell, number and line is <see cref="TacticalBattleViewModel"/>'s, read
/// from the engine's <see cref="TacticalBattleView"/>; a click the engine refuses shows its message. The screen
/// cannot be left: it has no close button and no Esc, and goes only when the battle ends (the owner then shows
/// <see cref="BattleEndedScreen"/>), by surrender or by quitting the application.
/// </para>
/// <para>Typed producer frames drive the complete displayed snapshot, deliberate lists, participants and controller.
/// Pauses and skip only affect traversal timing; all frames and exchange notifications enter once. The terminal
/// reconciliation is consumed before the result event, even when the session no longer has an open battle.</para>
/// </remarks>
public partial class TacticalBattleScreen : Control
{
    /// <summary>The board's cell side: 32 px, as the original's (T127's entry) and as the 32 x 32 battle assets are drawn.</summary>
    private const int CellPixels = 32;

    // The application's 1500px canvas scales down at practical window sizes. Battle values and role
    // cues need a readable local text size; preserve the approved font family, theme and nation colours.
    private static int BattleBodySize => AppTheme.Body + 6;
    private static int BattleNoteSize => AppTheme.Note + 6;
    private static int BattleHeaderSize => AppTheme.Header + 6;

    // Used only when the pack has no ground tile: the mean colour of assets/packs/authored/battle/ground.bmp (32 x 32),
    // measured over all its pixels (R 0.526, G 0.722, B 0.371), so the fallback matches the real tile's overall tone.
    private static readonly Color GroundFallback = new(0.526f, 0.722f, 0.371f);

    /// <summary>The game whose battle this is.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The battle as it stands when the screen opens.</summary>
    public required TacticalBattleView InitialView { get; init; }

    /// <summary>Opening frames retained across the production map's lines-only callback.</summary>
    public IReadOnlyList<TacticalBattleFrame>? InitialTimeline { get; init; }

    /// <summary>Typed exactly-once exchange seam for T183; fired by the production queue at every delay.</summary>
    public event Action<TacticalBattleFrame>? ExchangeEntered;
    public event Action<TacticalBattleFrame>? FrameEntered;
    public TacticalBattleFrame? DisplayedFrame => _playback.CurrentFrame;
    public TacticalPresentationSnapshot DisplayedSnapshot => _snapshot;
    public TacticalPlaybackStage PlaybackStage => _playback.Stage;
    public double PlaybackProgress => _playback.Progress;
    public TacticalPose? PlaybackPose => _playback.Pose;
    public Label ControllerLabel { get; private set; } = null!;
    public Label ActorLabel { get; private set; } = null!;
    public Label TargetLabel { get; private set; } = null!;
    public IReadOnlyList<string> UnitListTexts => _rosterTexts;
    public bool UnitListOpen => _listKind is not null;
    public string ExchangePlainText => string.Join("\n", Model.CurrentExchangeLines);
    private readonly List<string> _rosterTexts = new();
    private readonly Dictionary<int, Button> _rosterRowButtons = new();

    /// <summary>The repository root, where the asset packs live.</summary>
    public required string RepositoryRoot { get; init; }

    /// <summary>Raised when an order is done and the battle goes on (after any replay).</summary>
    public event Action<SessionOutput>? OrderIssued;

    /// <summary>Raised when an order ended the battle (after any replay); the output carries the report.</summary>
    public event Action<SessionOutput>? BattleEnded;

    /// <summary>Raised when the player presses Save.</summary>
    public event Action? SaveRequested;

    /// <summary>Raised when the player presses Save As.</summary>
    public event Action? SaveAsRequested;

    /// <summary>The model of what is drawn now.</summary>
    public TacticalBattleViewModel Model { get; private set; } = null!;

    /// <summary>The selected slot, or −1.</summary>
    public int SelectedSlot { get; private set; } = -1;

    /// <summary>Whether the screen is replaying the exchanges of the last order.</summary>
    public bool IsPlayingBack => _playback.IsPlaying;

    /// <summary>How many exchanges the replay has shown so far, for <c>godot/Checks/**</c>.</summary>
    public int ExchangesShown { get; private set; }

    /// <summary>Whether the surrender confirmation is open.</summary>
    public bool SurrenderPromptOpen => _dialogKind == DialogKind.Surrender;

    /// <summary>Whether the pauses dialog is open.</summary>
    public bool PausesDialogOpen => _dialogKind == DialogKind.Pauses;

    /// <summary>The caption label, for <c>godot/Checks/**</c>.</summary>
    public Label CaptionLabel { get; private set; } = null!;

    /// <summary>The selected unit's panel, for <c>godot/Checks/**</c>.</summary>
    public Label PanelLabel { get; private set; } = null!;

    /// <summary>The last exchange's panel, for <c>godot/Checks/**</c>.</summary>
    public RichTextLabel ExchangeLabel { get; private set; } = null!;

    /// <summary>The message line under the board: what the engine answered to the last order.</summary>
    public Label MessageLabel { get; private set; } = null!;

    /// <summary>The toolbar's buttons by id, for <c>godot/Checks/**</c>.</summary>
    public IReadOnlyDictionary<string, Button> ToolbarButtons => _buttons;

    private enum DialogKind
    {
        None,
        Surrender,
        Pauses,
    }

    private enum ListKind
    {
        UnitMoves,
        Friendly,
        Enemy,
    }

    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, string Nation), Texture2D?> _icons = new();
    private AssetPackTextureLoader? _loader;
    private TacticalBattleView _view = null!;
    private TacticalPresentationSnapshot _snapshot = null!;
    private int _perspective;
    private int _bottomSide;
    private string? _battleId;
    private BoardView _minimap = null!;
    private BattleDisplay _display = null!;
    private BoardView _board = null!;
    private VBoxContainer _listBox = null!;
    private Label _listTitle = null!;
    private ListKind? _listKind;
    private readonly TacticalPlayback _playback = new();
    private Control? _dialog;
    private DialogKind _dialogKind;

    public override void _Ready()
    {
        _view = InitialView;
        _display = BattleDisplay.For(InitialView, Session.State, Session.Ruleset, Session.World);
        _bottomSide = !InitialView.ComputerControlled[0] ? 0
            : !InitialView.ComputerControlled[1] ? 1
            : InitialView.SideToMove;
        _perspective = InitialView.ComputerControlled[0] && !InitialView.ComputerControlled[1] ? 1 : 0;
        _snapshot = new TacticalPresentationSnapshot(InitialView, _display.BoardWidth, _display.BoardHeight, false,
            ValueList<int>.Empty, ValueList<int>.Empty);
        _playback.PresentationChanged += () =>
        {
            if (_playback.Snapshot is not { } snapshot) return;
            _snapshot = snapshot;
            _view = snapshot.View;
            if (!_view.ComputerControlled[0] && !_view.ComputerControlled[1] && !_playback.ResolvingHalfRound)
                _perspective = _view.SideToMove;
            Refresh();
            ShowMessage(_playback.Stage == TacticalPlaybackStage.Announcement ? "TURN BEGINS · " + Model.ControllerText
                : Model.DepartureLines.Count > 0 ? string.Join("\n", Model.DepartureLines)
                : _playback.CurrentFrame is { } frame ? FrameMessage(frame) : string.Empty);
        };
        _playback.DiagnosticStageEntered += (frame, stage, snapshot) => AppDiagnostics.Frame(frame, Session.State.Calendar.TurnIndex, stage.ToString(), snapshot);
        _playback.FrameEntered += frame => { AppDiagnostics.Frame(frame, Session.State.Calendar.TurnIndex, displayed: _playback.Snapshot); FrameEntered?.Invoke(frame); };
        _playback.ExchangeEntered += frame =>
        {
            ExchangesShown++;
            ExchangeEntered?.Invoke(frame);
        };
        _loader = AssetPackTextureLoader.TryLoadPack(
            RepositoryRoot,
            SettingsScreen.SelectedPackId,
            onFailure: key => GD.PushWarning($"T127 battle screen: asset pack could not resolve or load '{key}'; falling back."));

        MouseFilter = MouseFilterEnum.Stop;
        var backdrop = new ColorRect { Color = UiKit.Background, MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, AppTheme.PanelMarginSide);
        }

        AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(row);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        row.AddChild(left);

        CaptionLabel = UiKit.MakeLabel(string.Empty, BattleHeaderSize, UiKit.AccentColor);
        CaptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        CaptionLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 0);
        left.AddChild(CaptionLabel);
        ControllerLabel = UiKit.MakeLabel(string.Empty, BattleBodySize, UiKit.AccentColor);
        ControllerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ControllerLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 0);
        left.AddChild(ControllerLabel);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        left.AddChild(toolbar);
        for (var i = 0; i < BattleAssetKeys.ToolbarButtons.Count; i++)
        {
            var id = BattleAssetKeys.ToolbarButtons[i];
            var button = new Button
            {
                TooltipText = TacticalBattleViewModel.ToolbarTooltips[i],
                CustomMinimumSize = new Vector2(44, 36),
                Icon = _loader?.TryGetTexture(BattleAssetKeys.ToolbarGlyph(id)),
                Name = id,
            };
            if (button.Icon is null)
            {
                button.Text = TacticalBattleViewModel.ToolbarTooltips[i];
            }

            var captured = id;
            button.Pressed += () => PressToolbarButton(captured);
            _buttons[id] = button;
            toolbar.AddChild(button);
        }

        _board = new BoardView
        {
            Host = this,
            CustomMinimumSize = new Vector2(CellPixels * 14, CellPixels * 12),
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Stop,
        };
        left.AddChild(_board);

        MessageLabel = UiKit.MakeLabel(string.Empty, BattleBodySize, UiKit.MutedTextColor);
        MessageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        MessageLabel.MaxLinesVisible = 3;
        MessageLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 54);
        left.AddChild(MessageLabel);
        var footer = new HBoxContainer();
        left.AddChild(footer);
        _minimap = new BoardView { Host = this, Mini = true, CustomMinimumSize = new Vector2(112, 96), MouseFilter = MouseFilterEnum.Ignore };
        footer.AddChild(_minimap);
        footer.AddChild(UiKit.MakeLabel("○ Cyan: ACTOR\n◇ Orange: TARGET\n□ Gold: SELECTED\nAttacker / Defender: strategic roles", BattleNoteSize, UiKit.TextColor));

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        row.AddChild(right);

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        saveRow.AddChild(UiKit.MakeButton("Save", () => SaveRequested?.Invoke(), AppTheme.Button));
        saveRow.AddChild(UiKit.MakeButton("Save As...", () => SaveAsRequested?.Invoke(), AppTheme.Button));
        right.AddChild(saveRow);

        // A click on the side panel during a computer-played half-round turns Computer general off (the entry).
        var panelBox = UiKit.MakePanel(UiKit.PanelColor);
        panelBox.MouseFilter = MouseFilterEnum.Stop;
        panelBox.GuiInput += OnPanelInput;
        right.AddChild(panelBox);
        var panelColumn = new VBoxContainer();
        panelColumn.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        panelBox.AddChild(panelColumn);
        panelColumn.AddChild(UiKit.MakeLabel("Selected unit", BattleBodySize, UiKit.MutedTextColor));
        PanelLabel = UiKit.MakeLabel(string.Empty, BattleBodySize, UiKit.TextColor);
        PanelLabel.CustomMinimumSize = new Vector2(0, 48);
        PanelLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(PanelLabel);
        ActorLabel = UiKit.MakeLabel(string.Empty, BattleNoteSize, new Color(0.2f, 0.9f, 1f));
        TargetLabel = UiKit.MakeLabel(string.Empty, BattleNoteSize, new Color(1f, 0.58f, 0.18f));
        panelColumn.AddChild(ActorLabel);
        panelColumn.AddChild(TargetLabel);
        panelColumn.AddChild(UiKit.MakeLabel("Current exchange", BattleNoteSize, UiKit.MutedTextColor));
        ExchangeLabel = new RichTextLabel { Name = "CurrentExchange", BbcodeEnabled = true, FitContent = true,
            ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        ExchangeLabel.AddThemeFontOverride("normal_font", AppTheme.BodyFont);
        ExchangeLabel.AddThemeFontSizeOverride("normal_font_size", BattleBodySize);
        ExchangeLabel.AddThemeColorOverride("default_color", UiKit.TextColor);
        ExchangeLabel.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.04f, 0.04f));
        ExchangeLabel.AddThemeConstantOverride("outline_size", 2);
        ExchangeLabel.CustomMinimumSize = new Vector2(0, 40);
        ExchangeLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(ExchangeLabel);
        foreach (var label in new[] { PanelLabel, ActorLabel, TargetLabel })
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.MouseFilter = MouseFilterEnum.Ignore;
        }

        _listTitle = UiKit.MakeLabel(string.Empty, BattleBodySize, UiKit.MutedTextColor);
        right.AddChild(_listTitle);
        _listTitle.Visible = false;
        _listBox = new VBoxContainer { Name = "DeliberateUnitList", Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(_listBox);

        Refresh();
        SetPlaybackPauses();
        var opening = InitialTimeline ?? Session.InitialTacticalTimeline.Concat(Session.LastTacticalTimeline)
            .Where(frame => frame.After.View.AttackerArmyId == InitialView.AttackerArmyId
                && frame.After.View.DefenderArmyId == InitialView.DefenderArmyId).ToArray();
        _battleId = opening.LastOrDefault()?.BattleId;
        _playback.Begin(opening.Where(frame => frame.BattleId == _battleId).OrderBy(frame => frame.FrameId), Refresh);
    }

    /// <summary>
    /// What a click on a board cell does: select, place, move or set a melee target (left), shoot or clear a target
    /// (right). The same path the board's mouse handler takes, exposed so a headless check can click without
    /// simulating the mouse.
    /// </summary>
    /// <param name="x">The cell's column.</param>
    /// <param name="y">The cell's row.</param>
    /// <param name="right">Whether it is the right button.</param>
    public void ClickCell(int x, int y, bool right = false)
    {
        if (IsPlayingBack || _dialogKind != DialogKind.None || Model is null)
        {
            return;
        }

        var click = right ? Model.ForRightClick(x, y) : Model.ForLeftClick(x, y);
        switch (click.Kind)
        {
            case BattleClickKind.Select:
                SelectedSlot = click.Unit;
                Refresh();
                break;
            case BattleClickKind.Place:
                Send(s => s.BattlePlace(click.Unit, click.X, click.Y), $"battle place {click.Unit} {click.X} {click.Y}");
                break;
            case BattleClickKind.Move:
                Send(s => s.BattleMove(click.Unit, click.X, click.Y), $"battle move {click.Unit} {click.X} {click.Y}");
                break;
            case BattleClickKind.Attack:
                Send(s => s.BattleAttack(click.Unit, click.Enemy), $"battle attack {click.Unit} {click.Enemy}");
                break;
            case BattleClickKind.Shoot:
                Send(s => s.BattleShoot(click.Unit, click.Enemy), $"battle shoot {click.Unit} {click.Enemy}");
                break;
            case BattleClickKind.Clear:
                Send(s => s.BattleClear(click.Unit), $"battle clear {click.Unit}");
                break;
        }
    }

    /// <summary>Maps an engine row to the row displayed on the board and minimap.</summary>
    public int BoardYForEngineY(int engineY) => _bottomSide == 0 ? Model.BoardHeight - 1 - engineY : engineY;

    /// <summary>Maps a displayed board row back to its engine row.</summary>
    public int EngineYForBoardY(int boardY) => _bottomSide == 0 ? Model.BoardHeight - 1 - boardY : boardY;

    /// <summary>Routes a board-view row through the production display-to-engine coordinate mapping.</summary>
    public void ClickBoardCell(int x, int boardY, bool right = false) => ClickCell(x, EngineYForBoardY(boardY), right);

    /// <summary>Selects an own unit by slot, as a click on it would.</summary>
    /// <param name="slot">The slot.</param>
    public void SelectUnit(int slot)
    {
        var unit = _view.Slots[slot];
        ClickCell(unit.X, unit.Y);
    }

    /// <summary>Presses a toolbar button by id (one of <see cref="BattleAssetKeys.ToolbarButtons"/>).</summary>
    /// <param name="id">The button.</param>
    public void PressToolbarButton(string id)
    {
        if (_dialogKind != DialogKind.None || Model is null || !Model.Buttons.Single(b => b.Id == id).Enabled)
        {
            return;
        }

        switch (id)
        {
            case "unit_moves":
                _listKind = ListKind.UnitMoves;
                Refresh();
                break;
            case "friendly_units":
                _listKind = ListKind.Friendly;
                Refresh();
                break;
            case "enemy_units":
                _listKind = ListKind.Enemy;
                Refresh();
                break;
            case "cancel_selection":
                SelectedSlot = -1;
                Refresh();
                break;
            case "end_turn":
                Send(s => s.BattleEnd(), "battle end");
                break;
            case "change_pauses":
                OpenPausesDialog();
                break;
            case "computer_general":
                Send(s => s.BattleGeneral(true), "battle general on");
                break;
            case "surrender":
                OpenSurrenderPrompt();
                break;
        }
    }

    /// <summary>Answers the surrender confirmation: Yes surrenders, No and Cancel leave the battle unchanged.</summary>
    /// <param name="yes">Whether the answer is Yes.</param>
    public void AnswerSurrender(bool yes)
    {
        if (_dialogKind != DialogKind.Surrender)
        {
            return;
        }

        CloseDialog();
        if (yes)
        {
            Send(s => s.BattleSurrender(confirmed: true), "battle surrender yes");
        }
    }

    /// <summary>Closes the pauses dialog.</summary>
    public void ClosePausesDialog()
    {
        if (_dialogKind == DialogKind.Pauses)
        {
            CloseDialog();
        }
    }

    /// <summary>Shows a line under the board (the engine's answer to an order or a save).</summary>
    /// <param name="text">The text.</param>
    public void ShowMessage(string text)
    {
        if (MessageLabel is not null)
        {
            MessageLabel.Text = text;
            MessageLabel.TooltipText = text;
        }
    }

    /// <summary>
    /// Moves the replay on by <paramref name="seconds"/>, as the frame loop does. Exposed so a headless check can step
    /// the clock without waiting for real time.
    /// </summary>
    /// <param name="seconds">The time passed.</param>
    public void AdvancePlayback(double seconds) => _playback.Advance(seconds);

    /// <summary>Skips only waits; every frame and typed exchange still enters once.</summary>
    public void SkipPlayback() { RecordPlaybackBoundary(DiagnosticEvent.Skip); _playback.Finish(); }

    public override void _Process(double delta) => AdvancePlayback(delta);

    public override void _ExitTree()
    {
        if (IsPlayingBack) RecordPlaybackBoundary(DiagnosticEvent.Cancel);
    }

    /// <summary>
    /// A click on the side panel: during a replay it ends the replay, and turns <em>Computer general</em> off when
    /// the engine still has it on for a human side. Exposed for the headless check.
    /// </summary>
    public void PanelClicked()
    {
        if (!IsPlayingBack)
        {
            return;
        }

        var open = Session.OpenTacticalBattle;
        var generalOn = open is not null && Enumerable.Range(0, 2).Any(i => open.ComputerGeneral[i] && !open.ComputerControlled[i]);
        SkipPlayback();
        if (generalOn && Session.OpenTacticalBattle is not null)
        {
            Send(s => s.BattleGeneral(false), "battle general off");
        }
    }

    /// <summary>The icon of a unit cell in its nation's colours, or <see langword="null"/> when the pack has none.</summary>
    internal Texture2D? IconFor(BattleCellView cell)
    {
        var nation = Model.TintNationId(cell);
        if (cell.IconKey is null || nation is null)
        {
            return null;
        }

        if (_icons.TryGetValue((cell.IconKey, nation), out var cached))
        {
            return cached;
        }

        Texture2D? result = null;
        if (_loader?.TryGetTexture(cell.IconKey) is { } texture)
        {
            result = texture;
            if (BattleIconTint.ForNation(Session.World, nation) is { } colors && texture.GetImage() is { } image)
            {
                image.Convert(Image.Format.Rgba8);
                var data = image.GetData();
                BattleIconTint.Recolour(colors, data, 4);
                result = ImageTexture.CreateFromImage(Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data));
            }
        }

        _icons[(cell.IconKey, nation)] = result;
        return result;
    }

    internal Texture2D? GroundTexture => _loader?.TryGetTexture(BattleAssetKeys.Ground);

    internal Texture2D? CursorTexture => _loader?.TryGetTexture(BattleAssetKeys.Cursor);

    internal string TypeNameOf(BattleCellView cell) =>
        cell.Slot >= 0 && cell.Slot < _view.Slots.Count && _view.Slots[cell.Slot].Type < _display.UnitTypeNames.Count
            ? _display.UnitTypeNames[_view.Slots[cell.Slot].Type]
            : "?";

    private void SetPlaybackPauses()
    {
        _playback.ShotDelay = SettingsScreen.BattleShotDelaySeconds;
        _playback.AttackDelay = SettingsScreen.BattleAttackDelaySeconds;
    }

    private void Send(Func<GameSession, SessionOutput> order, string diagnosticCommand)
    {
        var output = AppDiagnostics.Operation(DiagnosticEvent.BattleCommand, "tactical", () => order(Session), command: diagnosticCommand, turn: Session.State.Calendar.TurnIndex, success: output => DiagnosticJournal.CommandSucceeded(output.Lines), nation: Session.State.ActiveNationId);
        SetPlaybackPauses();
        _battleId ??= output.TacticalTimeline.FirstOrDefault()?.BattleId;
        _playback.Begin(output.TacticalTimeline.Where(frame => frame.BattleId == _battleId), () => Finish(output));
    }

    private void RecordPlaybackBoundary(DiagnosticEvent kind)
    {
        if (_playback.CurrentFrame is { } frame)
            AppDiagnostics.Frame(frame, Session.State.Calendar.TurnIndex, displayed: _playback.Snapshot, boundary: kind);
        else AppDiagnostics.Mark(kind, "tactical");
    }

    private void Finish(SessionOutput output)
    {
        RecordPlaybackBoundary(DiagnosticEvent.Completion);
        // Every terminal and housekeeping frame has already been displayed, including null OpenBattleView.
        Refresh();
        ShowMessage(output.TacticalTimeline.Count == 0
            ? CommandOutcomeText.OutcomeBlock(output.Lines)
            : "Battlefield updated · " + Model.ControllerText);
        if (output.EndedBattleReport is not null) BattleEnded?.Invoke(output);
        else OrderIssued?.Invoke(output);
    }

    private string FrameMessage(TacticalBattleFrame frame) => frame.Event switch
    {
        TacticalMovedEvent move => $"ACTOR #{move.Slot} moves ({move.FromX},{move.FromY}) → ({move.ToX},{move.ToY})",
        TacticalRoutedEvent rout => $"#{rout.Slot} routed and removed",
        TacticalCascadeRemovedEvent cascade => $"#{cascade.Slot} removed in rout cascade",
        TacticalSurrenderedEvent surrender => $"{(surrender.Side == 0 ? "Attacker" : "Defender")} surrendered",
        TacticalShotEvent or TacticalMeleeEvent => string.Join("\n", TacticalBattleViewModel.PresentExchange(frame, _display).PlainLines),
        _ => frame.Kind switch
        {
            TacticalFrameKind.HalfRound => "Finishing the previous half-round",
            TacticalFrameKind.Setup => "Next half-round ready",
            TacticalFrameKind.Terminal => "Battle over — finishing the last moves",
            TacticalFrameKind.Reconciliation => "Battlefield updated",
            _ => frame.Kind.ToString(),
        },
    };

    private void Refresh()
    {
        Model = TacticalBattleViewModel.FromSnapshot(_snapshot, _display, SelectedSlot, _playback.CurrentFrame,
            _playback.CurrentExchange, _playback.PreviousExchange, _perspective, _playback.ResolvingHalfRound,
            controlsEnabled: !IsPlayingBack, pose: _playback.Pose, stage: _playback.Stage);
        SelectedSlot = Model.SelectedSlot;

        CaptionLabel.Text = _playback.ResolvingHalfRound
            ? $"{_display.AttackerNationName}  v  {_display.DefenderNationName} — resolving combat"
            : Model.Caption;
        ControllerLabel.Text = (_playback.Stage == TacticalPlaybackStage.Announcement ? "TURN BEGINS · " : "") + Model.ControllerText;
        PanelLabel.Text = string.Join("\n", Model.PanelLines);
        ExchangeLabel.Text = string.Join("\n", Model.CurrentExchangePresentation.RichLines);
        ExchangeLabel.TooltipText = ExchangePlainText;
        ExchangeLabel.AddThemeColorOverride("default_color", Model.DepartureLines.Count > 0 ? new Color(1f, 0.7f, 0.25f) : UiKit.TextColor);
        ActorLabel.Text = string.Join("\n", Model.ActorLines);
        TargetLabel.Text = string.Join("\n", Model.TargetLines);
        foreach (var button in Model.Buttons)
        {
            _buttons[button.Id].Disabled = !button.Enabled;
        }

        RebuildList();
        _board.QueueRedraw();
        _minimap.QueueRedraw();
    }

    private string RosterText(BattleListRow row, int side)
    {
        var text = string.Join(" · ", row.Cells);
        if (_listKind == ListKind.UnitMoves && Model.CanSeeDetails(side))
        {
            var unit = _view.Slots[row.Slot];
            text += $" · moves {unit.Moves} · shots {unit.Shots}";
        }
        return text;
    }

    private void RebuildList()
    {
        _listTitle.Visible = _listBox.Visible = UnitListOpen;
        if (_listKind is null) return;
        var side = _listKind == ListKind.Enemy ? 1 - _perspective : _perspective;
        var displayedRows = Model.RosterRows(side);
        _listTitle.Text = (_listKind == ListKind.Enemy ? "Enemy units · " : _listKind == ListKind.Friendly ? "Friendly units · " : "Unit moves · ") + Model.RosterTitle(side);
        if (_rosterRowButtons.Count > 0 && _rosterRowButtons.Keys.Order().SequenceEqual(displayedRows.Select(r => r.Slot).Order()))
        {
            _rosterTexts.Clear();
            _rosterTexts.Add(_listTitle.Text);
            foreach (var row in displayedRows)
            {
                var text = RosterText(row, side);
                _rosterTexts.Add(text);
                _rosterRowButtons[row.Slot].Text = text;
                _rosterRowButtons[row.Slot].TooltipText = text;
            }
            return;
        }
        _rosterRowButtons.Clear();
        foreach (var child in _listBox.GetChildren())
        {
            _listBox.RemoveChild(child);
            child.QueueFree();
        }
        _rosterTexts.Clear();
        _rosterTexts.Add(_listTitle.Text);
        _listBox.AddChild(UiKit.MakeButton("Close unit list", CloseUnitList, BattleNoteSize));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 64) };
        _listBox.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(rows);
        foreach (var row in displayedRows)
        {
            var text = RosterText(row, side);
            _rosterTexts.Add(text);
            var slot = row.Slot;
            var button = new Button { Text = text, Flat = true, Alignment = HorizontalAlignment.Left,
                TooltipText = text, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.AddThemeFontSizeOverride("font_size", BattleNoteSize);
            button.Pressed += () => { if (IsPlayingBack) return; SelectedSlot = slot; Refresh(); };
            _rosterRowButtons[slot] = button;
            rows.AddChild(button);
        }
    }

    public void CloseUnitList()
    {
        _listKind = null;
        _rosterTexts.Clear();
        _rosterRowButtons.Clear();
        foreach (var child in _listBox.GetChildren()) { _listBox.RemoveChild(child); child.QueueFree(); }
        _listTitle.Visible = _listBox.Visible = false;
    }

    private void OnPanelInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true })
        {
            PanelClicked();
        }
    }

    private void OpenSurrenderPrompt()
    {
        ModalOverlay.Build(out var root, out var content, AppTheme.Scrim, 360f);
        content.AddChild(UiKit.MakeLabel("Are you sure you want to surrender ?", BattleBodySize, UiKit.TextColor));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        buttons.AddChild(UiKit.MakeButton("Yes", () => AnswerSurrender(true)));
        buttons.AddChild(UiKit.MakeButton("No", () => AnswerSurrender(false)));
        buttons.AddChild(UiKit.MakeButton("Cancel", () => AnswerSurrender(false)));
        content.AddChild(buttons);
        OpenDialog(root, DialogKind.Surrender);
    }

    private void OpenPausesDialog()
    {
        ModalOverlay.Build(out var root, out var content, AppTheme.Scrim, 420f);
        content.AddChild(UiKit.MakeLabel("Change pauses", BattleHeaderSize, UiKit.AccentColor));
        content.AddChild(UiKit.MakeLabel("Display only: the battle's rules never wait.", BattleNoteSize, UiKit.MutedTextColor));
        content.AddChild(MakePauseRow("After a shot (s)", BattlePauses.ShotStepSeconds, SettingsScreen.BattleShotDelaySeconds,
            value => SettingsScreen.SetBattlePauses(value, SettingsScreen.BattleAttackDelaySeconds)));
        content.AddChild(MakePauseRow("After an attack (s)", BattlePauses.AttackStepSeconds, SettingsScreen.BattleAttackDelaySeconds,
            value => SettingsScreen.SetBattlePauses(SettingsScreen.BattleShotDelaySeconds, value)));
        var ok = UiKit.MakeButton("OK", ClosePausesDialog);
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        content.AddChild(ok);
        OpenDialog(root, DialogKind.Pauses);
    }

    private static HBoxContainer MakePauseRow(string caption, double step, double current, Action<double> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        var name = UiKit.MakeLabel(caption, BattleBodySize, UiKit.TextColor);
        name.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(name);
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = BattlePauses.MaxSeconds,
            Step = step,
            Value = current,
            CustomMinimumSize = new Vector2(160, 24),
        };
        var value = UiKit.MakeLabel(current.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), BattleBodySize, UiKit.MutedTextColor);
        slider.ValueChanged += v =>
        {
            onChanged(v);
            value.Text = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        };
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    private void OpenDialog(Control root, DialogKind kind)
    {
        CloseDialog();
        _dialog = root;
        _dialogKind = kind;
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
    }

    private void CloseDialog()
    {
        if (_dialog is not null)
        {
            RemoveChild(_dialog);
            _dialog.QueueFree();
            _dialog = null;
        }

        _dialogKind = DialogKind.None;
    }

    /// <summary>The board: ground, unit icons in their nation's colours, the cursor; it forwards clicks as cells.</summary>
    private sealed partial class BoardView : Control
    {
        public required TacticalBattleScreen Host { get; init; }
        public bool Mini { get; init; }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true } click
                && (click.ButtonIndex == MouseButton.Left || click.ButtonIndex == MouseButton.Right))
            {
                var x = (int)(click.Position.X / CellPixels);
                var y = (int)(click.Position.Y / CellPixels);
                if (x >= 0 && y >= 0 && x < Host.Model.BoardWidth && y < Host.Model.BoardHeight)
                {
                    Host.ClickBoardCell(x, y, click.ButtonIndex == MouseButton.Right);
                    AcceptEvent();
                }
            }
        }

        // #1292: hovering a unit on the board names it; the theme's TooltipPanel/TooltipLabel style it.
        public override string _GetTooltip(Vector2 atPosition)
        {
            if (Mini || Host.Model is not { } model) return string.Empty;
            var x = (int)(atPosition.X / CellPixels);
            var y = (int)(atPosition.Y / CellPixels);
            return x >= 0 && y >= 0 && x < model.BoardWidth && y < model.BoardHeight
                ? model.HoverText(x, Host.EngineYForBoardY(y)) ?? string.Empty
                : string.Empty;
        }

        public override void _Draw()
        {
            if (Host.Model is not { } model)
            {
                return;
            }

            var pixels = Mini ? 8 : CellPixels;
            var ground = Host.GroundTexture;
            var font = AppTheme.BodyFont;
            foreach (var cell in model.Cells)
            {
                var rect = new Rect2(cell.X * pixels, Host.BoardYForEngineY(cell.Y) * pixels, pixels, pixels);
                if (ground is not null)
                {
                    DrawTextureRect(ground, rect, false);
                }
                else
                {
                    DrawRect(rect, GroundFallback);
                }

                DrawRect(rect, new Color(0, 0, 0, 0.18f), false);

            }
            foreach (var visual in model.VisualUnits)
            {
                var cell = visual.Cell;
                var displayY = Host._bottomSide == 0 ? model.BoardHeight - 1f - (float)visual.Y : (float)visual.Y;
                var rect = new Rect2((float)visual.X * pixels, displayY * pixels, pixels, pixels);
                var alpha = new Color(1, 1, 1, (float)visual.Opacity);
                if (cell.IconKey is not null)
                {
                    if (Host.IconFor(cell) is { } icon)
                    {
                        DrawTextureRect(icon, rect, false, alpha);
                    }
                    else
                    {
                        DrawRect(rect.Grow(-3), cell.TintSide == 0 ? new Color(0.25f, 0.45f, 0.8f, alpha.A) : new Color(0.8f, 0.3f, 0.25f, alpha.A));
                        var letter = Host.TypeNameOf(cell);
                        if (!Mini) DrawString(font, rect.Position + new Vector2(4, 26), letter.Length > 0 ? letter[..1] : "?", HorizontalAlignment.Left, -1, BattleBodySize, alpha);
                    }
                }

                // Role flags require a live occupant in the actual displayed grid. Removed participants
                // remain explicit in their panels/history without a ghost marker at their old coordinates.
                if (cell.Actor)
                    DrawArc(rect.GetCenter(), pixels * 0.42f, 0, Mathf.Tau, 24, new Color(0.2f, 0.9f, 1f, alpha.A), Mini ? 1 : 2);
                if (cell.Target)
                {
                    var c = rect.GetCenter();
                    var radius = pixels * 0.45f;
                    DrawPolyline(new[] { c + new Vector2(0, -radius), c + new Vector2(radius, 0), c + new Vector2(0, radius), c + new Vector2(-radius, 0), c + new Vector2(0, -radius) }, new Color(1f, 0.58f, 0.18f, alpha.A), Mini ? 1 : 2);
                }
                if (cell.Selected)
                {
                    if (!Mini && Host.CursorTexture is { } cursor)
                    {
                        DrawTextureRect(cursor, rect, false);
                    }
                    // The selection square remains independent of nation tint and any pack cursor motif.
                    DrawRect(rect.Grow(-1), new Color(0.95f, 0.78f, 0.35f), false, Mini ? 1 : 3f);
                }
            }
        }
    }
}
