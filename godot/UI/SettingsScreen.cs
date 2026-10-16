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

    /// <summary>The pack id chosen here, read by <see cref="GameMapView.Attach"/> when a game's map
    /// builds its own loader — <see langword="null"/> keeps
    /// <see cref="Assets.AssetPackTextureLoader.TryLoadPack"/>'s own default
    /// (<c>authored</c> when present and valid, otherwise <c>placeholder</c>).</summary>
    public static string? SelectedPackId { get; private set; }

    /// <summary>
    /// T149: whether sound is on — on by default, the original had no such option so this is the
    /// clone's own (the task entry's "the original has no such option, so this is the clone's own").
    /// Read by <see cref="Audio.SoundPlayer"/> every time it queues a cue, so toggling here applies
    /// to the next cue (a cue already on Godot's audio thread finishes naturally — the player
    /// restarts nothing). A future game picks up the current value the same way it picks up
    /// <see cref="SelectedPackId"/>, since both are static state.
    /// </summary>
    public static bool SoundEnabled { get; private set; } = true;

    /// <summary>
    /// T149 (Sol's review of PR #886, R4): the headless check's way to flip the Sound toggle
    /// without driving the CheckBox, so <see cref="MainGameScreen"/>'s production subscription
    /// (which reads <see cref="SoundEnabled"/> on every event) is what mutes the player.
    /// </summary>
    public static void SetSoundEnabledForCheck(bool enabled) => SoundEnabled = enabled;

    /// <summary>
    /// T127: the tactical battle screen's pause after a shot, in seconds (0 to 3.0 in 0.1 s steps; default 0). A
    /// display-only player setting kept beside <see cref="SelectedPackId"/>: it is never in a save or a ruleset, and the
    /// engine never waits (the user's decisions of 2026-10-04 on #496).
    /// </summary>
    public static double BattleShotDelaySeconds { get; private set; }

    /// <summary>T127: the pause after a melee attack, in seconds (0 to 3.0 in 1.0 s steps; default 0). See <see cref="BattleShotDelaySeconds"/>.</summary>
    public static double BattleAttackDelaySeconds { get; private set; }

    /// <summary>
    /// T127: sets both battle pauses, snapped to the original's <c>TBattleDelays</c> grid (<see cref="BattlePauses"/>).
    /// </summary>
    /// <param name="shotSeconds">The pause after a shot.</param>
    /// <param name="attackSeconds">The pause after an attack.</param>
    public static void SetBattlePauses(double shotSeconds, double attackSeconds)
    {
        BattleShotDelaySeconds = BattlePauses.SnapShot(shotSeconds);
        BattleAttackDelaySeconds = BattlePauses.SnapAttack(attackSeconds);
    }

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

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), AppTheme.Button);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);
        header.AddChild(UiKit.MakeLabel("Settings", AppTheme.Header, UiKit.AccentColor));

        column.AddChild(UiKit.MakeLabel("Asset pack:", AppTheme.Body, UiKit.TextColor));

        var packs = DiscoverPacks();
        var picker = new OptionButton { CustomMinimumSize = new Vector2(260, 36) };
        foreach (var pack in packs)
        {
            picker.AddItem(pack);
        }

        if (packs.Count > 0)
        {
            // No selection yet shows the loader's own default (authored when listed), so the picker
            // and the pack GameMapView will load agree from the first frame.
            var selectedIndex = SelectedPackId is not null
                ? packs.IndexOf(SelectedPackId)
                : packs.IndexOf(Assets.AssetPackManifestLoader.DefaultPackId);
            picker.Selected = Math.Max(selectedIndex, 0);
            SelectedPackId = packs[picker.Selected];
        }
        else
        {
            column.AddChild(UiKit.MakeLabel("No asset packs found under assets/packs/.", AppTheme.Body, UiKit.MutedTextColor));
        }

        picker.ItemSelected += index => SelectedPackId = packs[(int)index];
        column.AddChild(picker);

        // T149: the Sound on/off checkbox, kept the same way SelectedPackId's picker is — a static
        // getter, the picker writes it directly, the player reads it every cue. Default true (the
        // original has no such option, so a fresh game starts with the cue list active).
        column.AddChild(UiKit.MakeLabel("Sound:", AppTheme.Body, UiKit.TextColor));
        var soundCheck = new CheckBox
        {
            Text = "Enabled",
            ButtonPressed = SoundEnabled,
        };
        soundCheck.Toggled += pressed => SoundEnabled = pressed;
        column.AddChild(soundCheck);

        // T127: the tactical battle's two display-only pauses, as the original's Change pauses dialog offers them.
        column.AddChild(UiKit.MakeLabel("Battle pauses (seconds):", AppTheme.Body, UiKit.TextColor));
        column.AddChild(MakePauseRow(
            "After a shot", BattlePauses.ShotStepSeconds, BattleShotDelaySeconds,
            value => SetBattlePauses(value, BattleAttackDelaySeconds)));
        column.AddChild(MakePauseRow(
            "After an attack", BattlePauses.AttackStepSeconds, BattleAttackDelaySeconds,
            value => SetBattlePauses(BattleShotDelaySeconds, value)));
    }

    private static HBoxContainer MakePauseRow(string caption, double step, double current, Action<double> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        var name = UiKit.MakeLabel(caption, AppTheme.Body, UiKit.TextColor);
        name.CustomMinimumSize = new Vector2(130, 0);
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
