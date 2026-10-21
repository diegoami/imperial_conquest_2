using IC2.Slice.Diagnostics;
using Godot;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// The main menu's "Settings" screen (<c>docs/game-design.md</c> §"User interface" item 1: "Settings
/// (asset-pack selection lives here)"). Lists every pack under <c>assets/packs/</c> that carries its own
/// <c>manifest.json</c> — the same directory <see cref="Assets.AssetPackTextureLoader"/> already reads
/// T11's placeholder pack from — and lets the player pick which one <see cref="GameMapView"/> loads.
/// </summary>
/// <remarks>
/// The picker lists every pack under <c>assets/packs/</c> that has a <c>manifest.json</c> — T11's
/// <c>placeholder</c> and T51's generated <c>authored</c>, as the repository ships both today; a
/// further pack simply appearing there is picked up the next time this screen runs, with no code
/// change. Nothing selected yet defaults to <see cref="Assets.AssetPackManifestLoader.DefaultPackId"/>
/// (<c>authored</c>) when it is listed, the same pack <see cref="Assets.AssetPackTextureLoader.TryLoadPack"/>
/// loads by default, so the picker shows what the map will actually use.
/// <para>
/// <strong>What the choice drives (bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>).</strong>
/// <see cref="SelectedPackId"/> is read by <see cref="GameMapView.Attach"/> when a game's map builds
/// its one asset-pack loader (T94 folded #454 item 6), so a change here applies to the <em>next</em>
/// game started or loaded; the game already on screen keeps the pack it began with. A selected pack
/// that is missing or malformed falls back to the <c>placeholder</c> pack, reported and never thrown.
/// </para>
/// </remarks>
public partial class SettingsScreen : Control
{
    public event Action? BackRequested;

    /// <summary>#1293: true when opened over a game (the Back button reads "Close").</summary>
    public bool InGame { get; init; }

    /// <summary>#1293: the settings in force, normalised.</summary>
    public static UserSettings Current { get; private set; } = new();

    /// <summary>
    /// #1293: where <see cref="Current"/> is saved on every change, or <see langword="null"/> to keep it in
    /// memory only (every headless check). <see cref="AppRoot"/> sets it when it is the running main scene.
    /// </summary>
    public static string? SettingsPath { get; set; }

    /// <summary>The pack id chosen here, read by <see cref="GameMapView.Attach"/> when a game's map
    /// builds its own loader — <see langword="null"/> keeps
    /// <see cref="Assets.AssetPackTextureLoader.TryLoadPack"/>'s own default
    /// (<c>authored</c> when present and valid, otherwise <c>placeholder</c>).</summary>
    public static string? SelectedPackId => Current.PackId;

    /// <summary>
    /// T149: whether sound is on — on by default, the original had no such option so this is the
    /// clone's own. Read by <see cref="Audio.SoundPlayer"/> every time it queues a cue, so toggling here
    /// applies to the next cue.
    /// </summary>
    public static bool SoundEnabled => Current.SoundEnabled;

    /// <summary>#1293: the headless checks' way to pick a pack without the picker (never saved).</summary>
    public static void SetPackForCheck(string? packId) => Current = Current with { PackId = packId };

    /// <summary>
    /// T149 (Sol's review of PR #886, R4): the headless check's way to flip the Sound toggle
    /// without driving the CheckBox, so <see cref="MainGameScreen"/>'s production subscription
    /// (which reads <see cref="SoundEnabled"/> on every event) is what mutes the player.
    /// </summary>
    public static void SetSoundEnabledForCheck(bool enabled)
    {
        Current = Current with { SoundEnabled = enabled };
        AppDiagnostics.Detail(DiagnosticEvent.Settings, nameof(SettingsScreen));
    }

    /// <summary>
    /// T127: the tactical battle screen's pause after a shot, in seconds (0 to 3.0 in 0.1 s steps; designed fresh-process default 1). A
    /// display-only player setting: it is never in a save or a ruleset, and the engine never waits (the user's decisions of 2026-10-04 on #496).
    /// </summary>
    public static double BattleShotDelaySeconds => Current.BattleShotDelaySeconds;

    /// <summary>T127: the pause after a melee attack, in seconds (0 to 3.0 in 1.0 s steps; designed fresh-process default 2). See <see cref="BattleShotDelaySeconds"/>.</summary>
    public static double BattleAttackDelaySeconds => Current.BattleAttackDelaySeconds;

    /// <summary>
    /// T127: sets both battle pauses, snapped to the original's <c>TBattleDelays</c> grid (<see cref="BattlePauses"/>).
    /// </summary>
    /// <param name="shotSeconds">The pause after a shot.</param>
    /// <param name="attackSeconds">The pause after an attack.</param>
    public static void SetBattlePauses(double shotSeconds, double attackSeconds) =>
        Commit(Current with { BattleShotDelaySeconds = shotSeconds, BattleAttackDelaySeconds = attackSeconds });

    /// <summary>#1293: loads <paramref name="path"/> into <see cref="Current"/>, remembers it as
    /// <see cref="SettingsPath"/> and applies it to <paramref name="root"/>'s tree.</summary>
    public static void LoadAndApply(string path, Node root)
    {
        SettingsPath = path;
        Current = UserSettings.Load(path);
        Appearance.Apply(Current, root);
    }

    /// <summary>#1293: replaces <see cref="Current"/>, saves it when <see cref="SettingsPath"/> is set, and
    /// applies the display options to <paramref name="applyTo"/>'s tree when one is given.</summary>
    public static void Commit(UserSettings next, Node? applyTo = null)
    {
        Current = next.Normalized();
        if (SettingsPath is { } path && !Current.TrySave(path))
        {
            GD.PushWarning($"Settings could not be saved to {path}.");
        }

        if (applyTo is not null)
        {
            Appearance.Apply(Current, applyTo);
        }

        AppDiagnostics.Detail(DiagnosticEvent.Settings, nameof(SettingsScreen));
        Committed?.Invoke();
    }

    /// <summary>Raised after every <see cref="Commit"/>, so an open map can redraw in a new style.</summary>
    public static event Action? Committed;

    /// <summary>The asset-pack picker's node name, for the headless checks.</summary>
    public const string PackPickerName = "PackPicker";

    /// <summary>The name of the map-style picker, for the checks.</summary>
    public const string MapStylePickerName = "MapStylePicker";

    /// <summary>The map's terrain style, one of <see cref="MapStyles"/>' ids.</summary>
    public static string MapStyleId => MapStyles.ById(Current.MapStyle).Id;

    /// <summary>Sets the map style without saving, for the checks.</summary>
    public static void SetMapStyleForCheck(string style) => Current = Current with { MapStyle = style };

    /// <summary>The Sound checkbox's node name, for the headless checks.</summary>
    public const string SoundCheckName = "SoundCheck";

    /// <summary>The volume slider's node name, for the headless checks (the other two sliders are the pauses).</summary>
    public const string VolumeSliderName = "VolumeSlider";

    private readonly List<(HSlider Slider, Label Value)> _pauseRows = new();
    private OptionButton _speedPicker = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_right", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_top", AppTheme.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", AppTheme.ScreenMarginVertical);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(outer);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        outer.AddChild(header);

        var backButton = UiKit.MakeButton(InGame ? "Close" : "< Back", () => BackRequested?.Invoke(), AppTheme.Button);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);
        header.AddChild(UiKit.MakeLabel("Settings", AppTheme.Header, UiKit.AccentColor));
        header.AddChild(UiKit.MakeLabel("Changes apply at once and are remembered.", AppTheme.Note, UiKit.MutedTextColor));

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        outer.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        scroll.AddChild(column);

        BuildAppearance(column);
        BuildMap(column);
        BuildBattle(column);
        BuildSound(column);
        BuildGame(column);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (InGame && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            BackRequested?.Invoke();
        }
    }

    private void Section(VBoxContainer column, string title)
    {
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, AppTheme.DialogGap * 2) });
        column.AddChild(UiKit.MakeLabel(title, AppTheme.Header, UiKit.AccentColor));
    }

    private static HBoxContainer Row(VBoxContainer column, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        var name = UiKit.MakeLabel(caption, AppTheme.Body, UiKit.TextColor);
        name.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(name);
        column.AddChild(row);
        return row;
    }

    private static OptionButton Picker<T>(HBoxContainer row, IReadOnlyList<T> items, Func<T, string> caption, int selected, Action<T> onPicked)
    {
        var picker = new OptionButton { CustomMinimumSize = new Vector2(280, 32) };
        foreach (var item in items)
        {
            picker.AddItem(caption(item));
        }

        picker.Selected = Math.Max(selected, 0);
        picker.ItemSelected += index => onPicked(items[(int)index]);
        row.AddChild(picker);
        return picker;
    }

    private void BuildAppearance(VBoxContainer column)
    {
        Section(column, "Appearance");

        var schemes = ColorSchemes.All;
        Picker(Row(column, "Colours"), schemes, s => s.Name, IndexOf(schemes, s => s.Id == Current.ColorScheme),
            scheme => Commit(Current with { ColorScheme = scheme.Id, BackgroundHex = null, TextHex = null }, GetTree().Root));

        var custom = Row(column, "Custom colours");
        custom.AddChild(ColorButton("Background", () => Current.ResolvedColors().Background, hex => Current with { BackgroundHex = hex }));
        custom.AddChild(ColorButton("Text", () => Current.ResolvedColors().Text, hex => Current with { TextHex = hex }));
        custom.AddChild(UiKit.MakeButton("Reset", () => Commit(Current with { BackgroundHex = null, TextHex = null }, GetTree().Root), AppTheme.Button));

        var fonts = UiFonts.All;
        Picker(Row(column, "Font"), fonts, f => f.Name, IndexOf(fonts, f => f.Id == Current.Font),
            font => Commit(Current with { Font = font.Id }, GetTree().Root));

        var sizes = new[] { 90, 100, 115, 130, 150 };
        Picker(Row(column, "Interface size"), sizes, p => $"{p}%", Array.IndexOf(sizes, Current.TextScalePercent),
            percent => Commit(Current with { TextScalePercent = percent }, GetTree().Root));

        var fullscreen = new CheckBox { Text = "Full screen", ButtonPressed = Current.Fullscreen };
        fullscreen.Toggled += on => Commit(Current with { Fullscreen = on }, GetTree().Root);
        Row(column, "Window").AddChild(fullscreen);
    }

    private HBoxContainer ColorButton(string caption, Func<string> current, Func<string, UserSettings> with)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", AppTheme.MenuGap);
        box.AddChild(UiKit.MakeLabel(caption, AppTheme.Body, UiKit.MutedTextColor));
        var button = new ColorPickerButton { Color = Color.FromHtml(current()), EditAlpha = false, CustomMinimumSize = new Vector2(48, 28) };
        button.PopupClosed += () => Commit(with("#" + button.Color.ToHtml(false)), GetTree().Root);
        box.AddChild(button);
        return box;
    }

    private void BuildBattle(VBoxContainer column)
    {
        Section(column, "Battle speed");
        var speeds = BattleSpeeds.All.Append(new BattleSpeed("custom", "Custom", 0, 0)).ToList();
        _speedPicker = Picker(Row(column, "Speed"), speeds, s => s.Name,
            Current.BattleSpeedPreset is { } preset ? speeds.IndexOf(preset) : speeds.Count - 1,
            speed =>
            {
                if (speed.Id == "custom") return;
                Commit(Current.WithBattleSpeed(speed));
                _pauseRows[0].Slider.SetValueNoSignal(BattleShotDelaySeconds);
                _pauseRows[1].Slider.SetValueNoSignal(BattleAttackDelaySeconds);
                _pauseRows[0].Value.Text = Seconds(BattleShotDelaySeconds);
                _pauseRows[1].Value.Text = Seconds(BattleAttackDelaySeconds);
            });

        // T127: the tactical battle's two display-only pauses, as the original's Change pauses dialog offers them.
        column.AddChild(UiKit.MakeLabel("Pauses (seconds; display only, the rules never wait):", AppTheme.Body, UiKit.MutedTextColor));
        column.AddChild(MakePauseRow(
            "After a shot", BattlePauses.ShotStepSeconds, BattleShotDelaySeconds,
            value => { SetBattlePauses(value, BattleAttackDelaySeconds); SyncSpeedPicker(); }, _pauseRows));
        column.AddChild(MakePauseRow(
            "After an attack", BattlePauses.AttackStepSeconds, BattleAttackDelaySeconds,
            value => { SetBattlePauses(BattleShotDelaySeconds, value); SyncSpeedPicker(); }, _pauseRows));

        // #1280 F7: battles between two computer nations need not interrupt the player's turn.
        var others = new CheckBox { Text = "Pop up reports of battles between other nations", ButtonPressed = Current.ShowOtherBattleReports };
        others.Toggled += on => Commit(Current with { ShowOtherBattleReports = on });
        Row(column, "Other battles").AddChild(others);
        column.AddChild(UiKit.MakeLabel("Off: their decisive results (armies destroyed, fleets sunk) still appear in News.", AppTheme.Note, UiKit.MutedTextColor));
    }

    private void SyncSpeedPicker() =>
        _speedPicker.Selected = Current.BattleSpeedPreset is { } preset ? BattleSpeeds.All.ToList().IndexOf(preset) : BattleSpeeds.All.Count;

    private void BuildMap(VBoxContainer column)
    {
        Section(column, "Map");

        // The owner's request of 2026-10-21: the original's tiled look by default, T148's painting on request.
        var styles = MapStyles.All;
        var style = Row(column, "Map style");
        Picker(style, styles, s => s.Name, IndexOf(styles, s => s.Id == MapStyles.ById(Current.MapStyle).Id),
            s => Commit(Current with { MapStyle = s.Id })).Name = MapStylePickerName;
        column.AddChild(UiKit.MakeLabel("Classic tiles: one patterned tile per map square, as the original. Painted: blended terrain.", AppTheme.Note, UiKit.MutedTextColor));

        // #1280 F2: a near-miss on a marker must not march an army away unasked.
        var confirm = new CheckBox { Text = "Confirm moves with a second click", ButtonPressed = Current.ConfirmMoves };
        confirm.Toggled += on => Commit(Current with { ConfirmMoves = on });
        Row(column, "Moving").AddChild(confirm);
        column.AddChild(UiKit.MakeLabel("On: the first click marks the destination, a second click on it moves. Off: the original's one-click move.", AppTheme.Note, UiKit.MutedTextColor));
    }

    private void BuildSound(VBoxContainer column)
    {
        Section(column, "Sound");

        // T149: the Sound on/off checkbox. Default true (the original has no such option, so a fresh game
        // starts with the cue list active).
        var soundCheck = new CheckBox { Text = "Enabled", ButtonPressed = SoundEnabled, Name = SoundCheckName };
        soundCheck.Toggled += pressed => Commit(Current with { SoundEnabled = pressed }, GetTree().Root);
        Row(column, "Sound").AddChild(soundCheck);

        var volume = Row(column, "Volume");
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = Current.VolumePercent, CustomMinimumSize = new Vector2(220, 24), Name = VolumeSliderName };
        var value = UiKit.MakeLabel($"{Current.VolumePercent}%", AppTheme.Body, UiKit.MutedTextColor);
        slider.ValueChanged += v =>
        {
            value.Text = $"{(int)v}%";
            Commit(Current with { VolumePercent = (int)v }, GetTree().Root);
        };
        volume.AddChild(slider);
        volume.AddChild(value);
    }

    private void BuildGame(VBoxContainer column)
    {
        Section(column, "Asset pack");
        var packs = DiscoverPacks();
        if (packs.Count == 0)
        {
            column.AddChild(UiKit.MakeLabel("No asset packs found under assets/packs/.", AppTheme.Body, UiKit.MutedTextColor));
            return;
        }

        // No selection yet shows the loader's own default (authored when listed), so the picker
        // and the pack GameMapView will load agree from the first frame.
        var selectedIndex = SelectedPackId is not null
            ? packs.IndexOf(SelectedPackId)
            : packs.IndexOf(Assets.AssetPackManifestLoader.DefaultPackId);
        selectedIndex = Math.Max(selectedIndex, 0);
        if (SelectedPackId != packs[selectedIndex])
        {
            Current = Current with { PackId = packs[selectedIndex] };
        }

        AppDiagnostics.Detail(DiagnosticEvent.Settings, nameof(SettingsScreen));
        var row = Row(column, "Pack");
        Picker(row, packs, p => p, selectedIndex, pack => Commit(Current with { PackId = pack })).Name = PackPickerName;
        if (InGame)
        {
            row.AddChild(UiKit.MakeLabel("(applies to the next game started or loaded)", AppTheme.Note, UiKit.MutedTextColor));
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (match(items[i])) return i;
        }

        return 0;
    }

    private static string Seconds(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private static HBoxContainer MakePauseRow(string caption, double step, double current, Action<double> onChanged,
        List<(HSlider Slider, Label Value)>? rows = null)
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
            CustomMinimumSize = new Vector2(220, 24),
        };
        var value = UiKit.MakeLabel(current.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), AppTheme.Body, UiKit.MutedTextColor);
        slider.ValueChanged += v =>
        {
            onChanged(v);
            value.Text = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        };
        row.AddChild(slider);
        row.AddChild(value);
        rows?.Add((slider, value));
        return row;
    }

    private static List<string> DiscoverPacks()
    {
        var packsRoot = Path.Combine(GameDataContext.RepositoryRoot, "assets", "packs");
        var found = new List<string>();
        if (!Directory.Exists(packsRoot))
        {
            return found;
        }

        foreach (var directory in Directory.GetDirectories(packsRoot))
        {
            if (File.Exists(Path.Combine(directory, "manifest.json")))
            {
                found.Add(Path.GetFileName(directory));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
