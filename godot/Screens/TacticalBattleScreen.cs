using Godot;
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
/// <para>
/// <strong>The pauses are display only.</strong> The engine plays a whole call at once (a half-round, or a whole battle
/// under <em>Computer general</em>), so by the time the screen hears of it the state is final. With a pause above 0 s the
/// screen replays the call's exchanges one by one, waiting the shot or attack pause after each, and only then shows the
/// final board and raises its event. A click on the side panel meanwhile ends the replay, and turns <em>Computer
/// general</em> off when the engine still has it on for a human side.
/// </para>
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
    public bool IsPlayingBack => _playback is not null;

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

    private sealed class Playback
    {
        public Playback(Queue<BattleExchange> queue, SessionOutput output)
        {
            Queue = queue;
            Output = output;
        }

        public Queue<BattleExchange> Queue { get; }

        public SessionOutput Output { get; }

        public double Remaining { get; set; }
    }

    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, string Nation), Texture2D?> _icons = new();
    private AssetPackTextureLoader? _loader;
    private TacticalBattleView _view = null!;
    private BattleExchange? _lastExchange;
    private BattleDisplay _display = null!;
    private BoardView _board = null!;
    private VBoxContainer _listBox = null!;
    private Label _listTitle = null!;
    private ListKind _listKind = ListKind.Friendly;
    private Playback? _playback;
    private Control? _dialog;
    private DialogKind _dialogKind;

    public override void _Ready()
    {
        _view = InitialView;
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
        left.AddChild(CaptionLabel);

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
        MessageLabel.CustomMinimumSize = new Vector2(CellPixels * 14, 54);
        left.AddChild(MessageLabel);

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
        PanelLabel.CustomMinimumSize = new Vector2(0, 150);
        PanelLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(PanelLabel);
        panelColumn.AddChild(UiKit.MakeLabel("Last exchange", AppTheme.Body, UiKit.MutedTextColor));
        ExchangeLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        ExchangeLabel.CustomMinimumSize = new Vector2(0, 90);
        ExchangeLabel.MouseFilter = MouseFilterEnum.Ignore;
        panelColumn.AddChild(ExchangeLabel);

        _listTitle = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        right.AddChild(_listTitle);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        right.AddChild(scroll);
        _listBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_listBox);

        Refresh();
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
    public void AdvancePlayback(double seconds)
    {
        if (_playback is not { } playback)
        {
            return;
        }

        playback.Remaining -= seconds;
        while (_playback is not null && _playback.Remaining <= 0)
        {
            ShowNextExchange();
        }
    }

    /// <summary>Ends the replay at once and shows the final state.</summary>
    public void SkipPlayback()
    {
        while (_playback is not null)
        {
            ShowNextExchange();
        }
    }

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

    private void Send(Func<GameSession, SessionOutput> order)
    {
        var before = _view;
        var output = order(Session);
        ShowMessage(CommandOutcomeText.OutcomeBlock(output.Lines));

        var exchanges = TacticalBattleViewModel.ParseExchanges(output.Lines, before);
        var wait = exchanges.Any(e => e.Kind == BattleExchangeKind.Shot && SettingsScreen.BattleShotDelaySeconds > 0)
                   || exchanges.Any(e => e.Kind == BattleExchangeKind.Melee && SettingsScreen.BattleAttackDelaySeconds > 0);
        if (!wait)
        {
            if (exchanges.Count > 0)
            {
                _lastExchange = exchanges[^1];
            }

            Finish(output);
            return;
        }

        _playback = new Playback(new Queue<BattleExchange>(exchanges), output);
        ShowNextExchange();
    }

    private void ShowNextExchange()
    {
        if (_playback is not { } playback)
        {
            return;
        }

        if (playback.Queue.Count == 0)
        {
            _playback = null;
            Finish(playback.Output);
            return;
        }

        var exchange = playback.Queue.Dequeue();
        _lastExchange = exchange;
        ExchangesShown++;
        playback.Remaining = exchange.Kind == BattleExchangeKind.Shot
            ? SettingsScreen.BattleShotDelaySeconds
            : SettingsScreen.BattleAttackDelaySeconds;
        Refresh();
    }

    private void Finish(SessionOutput output)
    {
        if (output.OpenBattleView is { } view)
        {
            _view = view;
        }

        if (output.EndedBattleReport is not null)
        {
            BattleEnded?.Invoke(output);
            return;
        }

        Refresh();
        OrderIssued?.Invoke(output);
    }

    private void Refresh()
    {
        _display = BattleDisplay.For(_view, Session.State, Session.Ruleset);
        Model = TacticalBattleViewModel.From(_view, _display, SelectedSlot, _lastExchange, controlsEnabled: !IsPlayingBack);
        SelectedSlot = Model.SelectedSlot;

        CaptionLabel.Text = Model.Caption;
        PanelLabel.Text = string.Join("\n", Model.PanelLines);
        ExchangeLabel.Text = _lastExchange is null ? string.Empty : string.Join("\n", _lastExchange.Lines);
        foreach (var button in Model.Buttons)
        {
            _buttons[button.Id].Disabled = !button.Enabled;
        }

        RebuildList();
        _board.QueueRedraw();
    }

    private void RebuildList()
    {
        foreach (var child in _listBox.GetChildren())
        {
            _listBox.RemoveChild(child);
            child.QueueFree();
        }

        (string Title, IReadOnlyList<string> Headers, IReadOnlyList<BattleListRow> Rows) list = _listKind switch
        {
            ListKind.UnitMoves => ("Unit moves", TacticalBattleViewModel.UnitMovesHeaders, Model.UnitMovesRows),
            ListKind.Enemy => ("Enemy units", TacticalBattleViewModel.EnemyHeaders, Model.EnemyRows),
            _ => ("Friendly units", TacticalBattleViewModel.FriendlyHeaders, Model.FriendlyRows),
        };
        _listTitle.Text = list.Title;

        var grid = new GridContainer { Columns = list.Headers.Count };
        grid.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        _listBox.AddChild(grid);
        foreach (var header in list.Headers)
        {
            grid.AddChild(UiKit.MakeLabel(header, AppTheme.Note, UiKit.MutedTextColor));
        }

        foreach (var row in list.Rows)
        {
            foreach (var cell in row.Cells)
            {
                grid.AddChild(UiKit.MakeLabel(cell, AppTheme.Note, row.Slot == SelectedSlot ? UiKit.AccentColor : UiKit.TextColor));
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

            var ground = Host.GroundTexture;
            var font = AppTheme.BodyFont;
            foreach (var cell in model.Cells)
            {
                var rect = new Rect2(cell.X * CellPixels, cell.Y * CellPixels, CellPixels, CellPixels);
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
                        DrawString(font, rect.Position + new Vector2(4, 26), letter.Length > 0 ? letter[..1] : "?", HorizontalAlignment.Left, -1, AppTheme.Body, Colors.White);
                    }
                }

                if (cell.Selected)
                {
                    if (Host.CursorTexture is { } cursor)
                    {
                        DrawTextureRect(cursor, rect, false);
                    }
                    else
                    {
                        // Game marker tint: palette changes must not recolour the selected cell.
                        DrawRect(rect.Grow(-1), new Color(0.95f, 0.78f, 0.35f), false, 3f);
                    }
                }
            }
        }
    }
}
