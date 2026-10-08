using Godot;

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

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), 14);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);
        header.AddChild(UiKit.MakeLabel("Settings", 26, UiKit.AccentColor));

        column.AddChild(UiKit.MakeLabel("Asset pack:", 16, UiKit.TextColor));

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
            column.AddChild(UiKit.MakeLabel("No asset packs found under assets/packs/.", 14, UiKit.MutedTextColor));
        }

        picker.ItemSelected += index => SelectedPackId = packs[(int)index];
        column.AddChild(picker);

        // T149: the Sound on/off checkbox, kept the same way SelectedPackId's picker is — a static
        // getter, the picker writes it directly, the player reads it every cue. Default true (the
        // original has no such option, so a fresh game starts with the cue list active).
        column.AddChild(UiKit.MakeLabel("Sound:", 16, UiKit.TextColor));
        var soundCheck = new CheckBox
        {
            Text = "Enabled",
            ButtonPressed = SoundEnabled,
        };
        soundCheck.Toggled += pressed => SoundEnabled = pressed;
        column.AddChild(soundCheck);
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
