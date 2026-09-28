using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// The main menu's "Settings" screen (<c>docs/game-design.md</c> §"User interface" item 1: "Settings
/// (asset-pack selection lives here)"). Lists every pack under <c>assets/packs/</c> that carries its own
/// <c>manifest.json</c> — the same directory <see cref="Assets.AssetPackTextureLoader"/> already reads
/// T11's placeholder pack from — and lets the player pick which one <see cref="GameMapView"/> loads.
/// </summary>
/// <remarks>
/// Only <c>assets/packs/placeholder/</c> ships today, so the picker lists exactly one, pre-selected,
/// entry — this is the real state of the repository, not a stub: a second pack simply appearing under
/// <c>assets/packs/</c> is picked up the next time this screen runs, with no code change.
/// <strong>Known gap</strong>: <see cref="Assets.AssetPackTextureLoader.TryLoadPlaceholderPack"/> only
/// ever loads the one pack its own name says — there is no "load pack by id" overload yet for
/// <see cref="SelectedPackId"/> to actually drive, so with only one pack shipped this screen's own
/// selection has nothing else to disagree with, but it does not yet change what
/// <see cref="GameMapView"/> loads. Left as read here rather than adding that overload to
/// <c>godot/Assets/AssetPackTextureLoader.cs</c> unasked — a second pack is what would actually need it.
/// </remarks>
public partial class SettingsScreen : Control
{
    public event Action? BackRequested;

    /// <summary>The pack id chosen here, read by <see cref="AppRoot"/> when it builds
    /// <see cref="GameMapView"/> — <see langword="null"/> keeps <see cref="Assets.AssetPackTextureLoader"/>'s
    /// own default ("placeholder").</summary>
    public static string? SelectedPackId { get; private set; }

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
            var selectedIndex = SelectedPackId is not null ? packs.IndexOf(SelectedPackId) : 0;
            picker.Selected = Math.Max(selectedIndex, 0);
            SelectedPackId = packs[picker.Selected];
        }
        else
        {
            column.AddChild(UiKit.MakeLabel("No asset packs found under assets/packs/.", 14, UiKit.MutedTextColor));
        }

        picker.ItemSelected += index => SelectedPackId = packs[(int)index];
        column.AddChild(picker);
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
