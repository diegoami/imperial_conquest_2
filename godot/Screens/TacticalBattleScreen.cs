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
/// <para>Typed producer frames drive the complete displayed snapshot, both rosters, participants and controller.
/// Pauses and skip only affect traversal timing; all frames and exchange notifications enter once. The terminal
/// reconciliation is consumed before the result event, even when the session no longer has an open battle.</para>
/// </remarks>
public partial class TacticalBattleScreen : Control
{
    /// <summary>The board's cell side: 32 px, as the original's (T127's entry) and as the 32 x 32 battle assets are drawn.</summary>
    private const int CellPixels = 32;

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
    public Label ControllerLabel { get; private set; } = null!;
    public Label PreviousExchangeLabel { get; private set; } = null!;
    public Label ActorLabel { get; private set; } = null!;
    public Label TargetLabel { get; private set; } = null!;
    public IReadOnlyList<string> RosterTexts => _rosterTexts;
    private readonly List<string> _rosterTexts = new();

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
    public Label ExchangeLabel { get; private set; } = null!;

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
    private string? _battleId;
    private BoardView _minimap = null!;
    private BattleDisplay _display = null!;
    private BoardView _board = null!;
    private VBoxContainer _listBox = null!;
    private Label _listTitle = null!;
    private ListKind _listKind = ListKind.Friendly;
    private readonly TacticalPlayback _playback = new();
    private Control? _dialog;
    private DialogKind _dialogKind;

    public override void _Ready()
    {
        _view = InitialView;
        _display = BattleDisplay.For(InitialView, Session.State, Session.Ruleset);
        _perspective = InitialView.ComputerControlled[0] && !InitialView.ComputerControlled[1] ? 1 : 0;
        _snapshot = new TacticalPresentationSnapshot(InitialView, _display.BoardWidth, _display.BoardHeight, false,
            ValueList<int>.Empty, ValueList<int>.Empty);
        _playback.FrameEntered += frame =>
        {
            _snapshot = frame.After;
            _view = _snapshot.View;
            if (!_view.ComputerControlled[0] && !_view.ComputerControlled[1] && !_playback.ResolvingHalfRound)
                _perspective = _view.SideToMove;
            Refresh();
            ShowMessage(FrameMessage(frame));
            FrameEntered?.Invoke(frame);
        };
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

        CaptionLabel = UiKit.MakeLabel(string.Empty, AppTheme.Header, UiKit.AccentColor);
        CaptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        CaptionLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 0);
        left.AddChild(CaptionLabel);
        ControllerLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.AccentColor);
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

        MessageLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        MessageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        MessageLabel.MaxLinesVisible = 3;
        MessageLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 54);
        left.AddChild(MessageLabel);
        var footer = new HBoxContainer();
        left.AddChild(footer);
        _minimap = new BoardView { Host = this, Mini = true, CustomMinimumSize = new Vector2(112, 96), MouseFilter = MouseFilterEnum.Ignore };
        footer.AddChild(_minimap);
        footer.AddChild(UiKit.MakeLabel("○ Cyan: ACTOR\n◇ Orange: TARGET\n□ Gold: SELECTED\nAttacker / Defender: strategic roles", AppTheme.Note, UiKit.TextColor));

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
        panelColumn.AddChild(UiKit.MakeLabel("Selected unit", AppTheme.Body, UiKit.MutedTextColor));
        PanelLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        PanelLabel.CustomMinimumSize = new Vector2(0, 48);
        PanelLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(PanelLabel);
        ActorLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, new Color(0.2f, 0.9f, 1f));
        TargetLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, new Color(1f, 0.58f, 0.18f));
        panelColumn.AddChild(ActorLabel);
        panelColumn.AddChild(TargetLabel);
        panelColumn.AddChild(UiKit.MakeLabel("Current exchange", AppTheme.Note, UiKit.MutedTextColor));
        ExchangeLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        ExchangeLabel.CustomMinimumSize = new Vector2(0, 40);
        ExchangeLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(ExchangeLabel);
        panelColumn.AddChild(UiKit.MakeLabel("Previous exchange", AppTheme.Note, UiKit.MutedTextColor));
        PreviousExchangeLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, UiKit.MutedTextColor);
        panelColumn.AddChild(PreviousExchangeLabel);
        foreach (var label in new[] { PanelLabel, ActorLabel, TargetLabel, ExchangeLabel, PreviousExchangeLabel })
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.MouseFilter = MouseFilterEnum.Ignore;
        }

        _listTitle = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        right.AddChild(_listTitle);
        _listBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
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
                Send(s => s.BattlePlace(click.Unit, click.X, click.Y));
                break;
            case BattleClickKind.Move:
                Send(s => s.BattleMove(click.Unit, click.X, click.Y));
                break;
            case BattleClickKind.Attack:
                Send(s => s.BattleAttack(click.Unit, click.Enemy));
                break;
            case BattleClickKind.Shoot:
                Send(s => s.BattleShoot(click.Unit, click.Enemy));
                break;
            case BattleClickKind.Clear:
                Send(s => s.BattleClear(click.Unit));
                break;
        }
    }

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
                Send(s => s.BattleEnd());
                break;
            case "change_pauses":
                OpenPausesDialog();
                break;
            case "computer_general":
                Send(s => s.BattleGeneral(true));
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
            Send(s => s.BattleSurrender(confirmed: true));
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
    public void SkipPlayback() => _playback.Finish();

    public override void _Process(double delta) => AdvancePlayback(delta);

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
            Send(s => s.BattleGeneral(false));
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

    private void Send(Func<GameSession, SessionOutput> order)
    {
        var output = order(Session);
        SetPlaybackPauses();
        _battleId ??= output.TacticalTimeline.FirstOrDefault()?.BattleId;
        _playback.Begin(output.TacticalTimeline.Where(frame => frame.BattleId == _battleId), () => Finish(output));
    }

    private void Finish(SessionOutput output)
    {
        // Every terminal and housekeeping frame has already been displayed, including null OpenBattleView.
        Refresh();
        ShowMessage(output.TacticalTimeline.Count == 0
            ? CommandOutcomeText.OutcomeBlock(output.Lines)
            : "Battlefield updated · " + Model.ControllerText);
        if (output.EndedBattleReport is not null) BattleEnded?.Invoke(output);
        else OrderIssued?.Invoke(output);
    }

    private static string FrameMessage(TacticalBattleFrame frame) => frame.Event switch
    {
        TacticalMovedEvent move => $"ACTOR #{move.Slot} moves ({move.FromX},{move.FromY}) → ({move.ToX},{move.ToY})",
        TacticalRoutedEvent rout => $"#{rout.Slot} routed and removed",
        TacticalCascadeRemovedEvent cascade => $"#{cascade.Slot} removed in rout cascade",
        TacticalShotEvent or TacticalMeleeEvent => string.Join("\n", TacticalBattleViewModel.ExchangeLines(frame)),
        _ => frame.Kind switch
        {
            TacticalFrameKind.HalfRound => "Resolving the previous half-round before controller handoff",
            TacticalFrameKind.Setup => "Round state updated — controller ready",
            TacticalFrameKind.Terminal => "Battle ended — completing battlefield updates",
            TacticalFrameKind.Reconciliation => "Battlefield reconciled",
            _ => frame.Kind.ToString(),
        },
    };

    private void Refresh()
    {
        Model = TacticalBattleViewModel.FromSnapshot(_snapshot, _display, SelectedSlot, _playback.CurrentFrame,
            _playback.CurrentExchange, _playback.PreviousExchange, _perspective, _playback.ResolvingHalfRound,
            controlsEnabled: !IsPlayingBack);
        SelectedSlot = Model.SelectedSlot;

        CaptionLabel.Text = _playback.ResolvingHalfRound
            ? $"{_display.AttackerNationName}  v  {_display.DefenderNationName} — resolving combat"
            : Model.Caption;
        ControllerLabel.Text = Model.ControllerText;
        PanelLabel.Text = string.Join("\n", Model.PanelLines);
        ExchangeLabel.Text = string.Join("\n", Model.CurrentExchangeLines);
        PreviousExchangeLabel.Text = string.Join("\n", Model.PreviousExchangeLines);
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

    private void RebuildList()
    {
        foreach (var child in _listBox.GetChildren())
        {
            _listBox.RemoveChild(child);
            child.QueueFree();
        }

        _rosterTexts.Clear();
        _listTitle.Text = "Both armies · " + (_listKind == ListKind.UnitMoves ? "moves and combat" : "battle roster");
        for (var side = 0; side < 2; side++)
        {
            var title = Model.RosterTitle(side);
            _rosterTexts.Add(title);
            var section = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            _listBox.AddChild(section);
            section.AddChild(UiKit.MakeLabel(title, AppTheme.Note, UiKit.AccentColor));
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 64) };
            section.AddChild(scroll);
            var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddChild(rows);
            foreach (var row in Model.RosterRows(side))
            {
                var text = string.Join(" · ", row.Cells);
                if (_listKind == ListKind.UnitMoves && Model.CanSeeDetails(side))
                {
                    var unit = _view.Slots[row.Slot];
                    text += $" · moves {unit.Moves} · shots {unit.Shots}";
                }
                _rosterTexts.Add(text);
                var slot = row.Slot;
                var button = new Button { Text = text, Flat = true, Alignment = HorizontalAlignment.Left,
                    TooltipText = text, ClipText = true, MouseFilter = MouseFilterEnum.Stop,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill };
                button.AddThemeFontSizeOverride("font_size", AppTheme.Note);
                button.Pressed += () =>
                {
                    if (IsPlayingBack) return;
                    SelectedSlot = slot;
                    Refresh();
                };
                rows.AddChild(button);
            }
        }
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
        content.AddChild(UiKit.MakeLabel("Are you sure you want to surrender ?", AppTheme.Body, UiKit.TextColor));
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
        content.AddChild(UiKit.MakeLabel("Change pauses", AppTheme.Header, UiKit.AccentColor));
        content.AddChild(UiKit.MakeLabel("Display only: the battle's rules never wait.", AppTheme.Note, UiKit.MutedTextColor));
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
        var name = UiKit.MakeLabel(caption, AppTheme.Body, UiKit.TextColor);
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
        var value = UiKit.MakeLabel(current.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), AppTheme.Body, UiKit.MutedTextColor);
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
                    Host.ClickCell(x, y, click.ButtonIndex == MouseButton.Right);
                    AcceptEvent();
                }
            }
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
                var rect = new Rect2(cell.X * pixels, cell.Y * pixels, pixels, pixels);
                if (ground is not null)
                {
                    DrawTextureRect(ground, rect, false);
                }
                else
                {
                    DrawRect(rect, GroundFallback);
                }

                DrawRect(rect, new Color(0, 0, 0, 0.18f), false);

                if (cell.IconKey is not null)
                {
                    if (Host.IconFor(cell) is { } icon)
                    {
                        DrawTextureRect(icon, rect, false);
                    }
                    else
                    {
                        DrawRect(rect.Grow(-3), cell.TintSide == 0 ? new Color(0.25f, 0.45f, 0.8f) : new Color(0.8f, 0.3f, 0.25f));
                        var letter = Host.TypeNameOf(cell);
                        if (!Mini) DrawString(font, rect.Position + new Vector2(4, 26), letter.Length > 0 ? letter[..1] : "?", HorizontalAlignment.Left, -1, AppTheme.Body, Colors.White);
                    }
                }

                var actor = model.ActorSlot;
                var target = model.TargetSlot;
                var actorHere = actor >= 0 && model.View.Slots[actor].X == cell.X && model.View.Slots[actor].Y == cell.Y;
                var targetHere = target >= 0 && model.View.Slots[target].X == cell.X && model.View.Slots[target].Y == cell.Y;
                if (actorHere)
                    DrawArc(rect.GetCenter(), pixels * 0.42f, 0, Mathf.Tau, 24, new Color(0.2f, 0.9f, 1f), Mini ? 1 : 2);
                if (targetHere)
                {
                    var c = rect.GetCenter();
                    var radius = pixels * 0.45f;
                    DrawPolyline(new[] { c + new Vector2(0, -radius), c + new Vector2(radius, 0), c + new Vector2(0, radius), c + new Vector2(-radius, 0), c + new Vector2(0, -radius) }, new Color(1f, 0.58f, 0.18f), Mini ? 1 : 2);
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
