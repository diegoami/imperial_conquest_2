using Godot;
using IC2.Engine.Model;
using IC2.Slice.Assets;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's shortcut toolbar — the original's main toolbar (audit §3.1): Open, Save and
/// End turn; the six Strategy entries; and the 16 nation buttons plus All nations. Every pictorial
/// button is one <see cref="GameCommandRow"/> and runs the same table handler its menu entry runs
/// (<c>docs/game-design.md</c> "User interface" item 2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Order across groups is [open]</strong> (audit §3.1): the form stream that holds the real
/// order and its separators has never been decoded, so this uses the audit's own group order —
/// <strong>File and Game, Strategy, then Nations</strong> — with a separator between groups, and says
/// so here rather than inventing a geometry claim.
/// </para>
/// <para>
/// The nation buttons are colour swatches, not new art: each one fills with its nation's T97 background
/// (<see cref="NationDefinition.ColorHex"/>) and outlines with its foreground
/// (<see cref="NationDefinition.GlyphColorHex"/>) through the same <see cref="MarkerTint.ForOwner"/>
/// the map's markers use, so the toolbar and the map cannot drift apart. All nations gets the neutral
/// accent treatment because it has no single nation colour. They are <strong>disabled until T110</strong>
/// (T100's Scope).
/// </para>
/// <para>
/// Each command button draws its command's <c>ui.command.&lt;id&gt;.icon</c> key through the pack
/// loader (<see cref="AssetPackTextureLoader.TryGetTexture"/>) and is <strong>icon-only</strong>: the
/// caption lives in the tooltip (while <strong>Show hints</strong> is on), not on the button, so the
/// row fits the 1500&#160;px design viewport and cannot push the context panel off-screen. When the pack
/// has no texture for the key, the button falls back to the caption as its own <see cref="Button.Text"/>
/// — so a missing texture can never leave an empty button (T100's Scope).
/// </para>
/// </remarks>
public partial class CommandToolbar : PanelContainer
{
    /// <summary>Toolbar buttons in display order. File/Game first, the six Strategy entries, then the
    /// 17 nation buttons (filled in from the table's own Nations rows, in the world's order). The File
    /// commands New/Save As/Close are menu-only, exactly as the audit's §3.1 list says.</summary>
    private static readonly string[] PictorialOrder =
    {
        "file.open",
        "file.save",
        "game.end_turn",
        "strategy.news",
        "strategy.relations",
        "strategy.taxation",
        "strategy.balance_sheet",
        "strategy.recruit_unit",
        "strategy.build_fleet",
    };

    public required GameCommandTable Table { get; init; }

    public required World World { get; init; }

    public required AssetPackTextureLoader? AssetLoader { get; init; }

    /// <summary>Every toolbar button, in display order — exposed for
    /// <c>godot/Checks/MenuBarCheck.cs</c> and <c>godot/Checks/MapClipCheck.cs</c>.</summary>
    public IReadOnlyList<Button> Buttons { get; private set; } = Array.Empty<Button>();

    private readonly Dictionary<string, Button> _buttonByCommand = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _captionByCommand = new(StringComparer.Ordinal);
    private bool _hintsEnabled = true;

    public override void _Ready()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        AddChild(row);

        foreach (var commandId in PictorialOrder)
        {
            row.AddChild(BuildCommandButton(GameCommandTable.RowById(commandId)!));
        }

        row.AddChild(new VSeparator());

        foreach (var nationRow in GameCommandTable.Rows.Where(r => string.Equals(r.Menu, "Nations", StringComparison.Ordinal)))
        {
            row.AddChild(nationRow.Id == "nations.all" ? BuildAllNationsButton(nationRow) : BuildNationSwatch(nationRow));
        }

        Buttons = _buttonByCommand.Values.ToList();
        ApplyHints();
    }

    /// <summary>The button for <paramref name="commandId"/>, or <see langword="null"/> when the toolbar
    /// does not offer it (the menu-only File commands).</summary>
    public Button? ButtonFor(string commandId) =>
        _buttonByCommand.TryGetValue(commandId, out var button) ? button : null;

    /// <summary>
    /// Presses <paramref name="commandId"/>'s button the way a click does — it emits the real
    /// <see cref="Button.Pressed"/> signal — and returns <see langword="false"/> without emitting
    /// anything when the button is disabled, so a check can prove a disabled control issues no command
    /// (T100 Done-when 7).
    /// </summary>
    public bool PressForCheck(string commandId)
    {
        if (!_buttonByCommand.TryGetValue(commandId, out var button) || button.Disabled)
        {
            return false;
        }

        button.EmitSignal(Button.SignalName.Pressed);
        return true;
    }

    /// <summary>
    /// Turns every button's tooltip on or off (T100 Done-when 6). The table owns no hint state; the
    /// screen flips this and the menu bar together from the one **Show hints** command.
    /// </summary>
    public void SetHintsEnabled(bool enabled)
    {
        _hintsEnabled = enabled;
        ApplyHints();
    }

    private Button BuildCommandButton(GameCommandRow row)
    {
        var button = new Button
        {
            Name = NameFor(row.Id),
            Disabled = !row.Wired,
            CustomMinimumSize = new Vector2(0, 36),
        };

        if (row.IconKey is { } iconKey && AssetLoader?.TryGetTexture(iconKey) is { } texture)
        {
            // Icon-only: the caption is the tooltip, so the row's minimum width stays small enough for
            // the 1500 px design viewport (rework B5).
            button.Icon = texture;
        }
        else
        {
            // A missing texture falls back to the caption as text, never an empty button (T100's Scope).
            button.Text = row.Caption;
        }

        button.Pressed += () => Table.TryInvoke(row.Id);
        Register(row.Id, row.Caption, button);
        return button;
    }

    private Button BuildNationSwatch(GameCommandRow row)
    {
        var nationId = row.Id["nations.".Length..];
        var colors = MarkerTint.ForOwner(World, nationId);
        var background = colors is { } pair ? ToColor(pair.Background) : new Color(0.4f, 0.4f, 0.4f);
        var foreground = colors is { } pair2 ? ToColor(pair2.Foreground) : new Color(0.9f, 0.9f, 0.9f);

        var button = new Button
        {
            Name = NameFor(row.Id),
            Disabled = true,
            CustomMinimumSize = new Vector2(30, 30),
            TooltipText = _hintsEnabled ? row.Caption : string.Empty,
        };
        button.AddThemeStyleboxOverride("normal", Swatch(background, foreground));
        button.AddThemeStyleboxOverride("hover", Swatch(background, foreground));
        button.AddThemeStyleboxOverride("pressed", Swatch(background, foreground));
        button.AddThemeStyleboxOverride("disabled", Swatch(background, foreground));

        Register(row.Id, row.Caption, button);
        return button;
    }

    private Button BuildAllNationsButton(GameCommandRow row)
    {
        var button = new Button
        {
            Name = NameFor(row.Id),
            Text = "All",
            Disabled = true,
            CustomMinimumSize = new Vector2(38, 30),
            TooltipText = _hintsEnabled ? row.Caption : string.Empty,
        };
        button.AddThemeStyleboxOverride("disabled", Swatch(UiKit.PanelColorRaised, UiKit.AccentColor));

        Register(row.Id, row.Caption, button);
        return button;
    }

    private static StyleBoxFlat Swatch(Color background, Color foreground) => new()
    {
        BgColor = background,
        BorderColor = foreground,
        BorderWidthTop = 2,
        BorderWidthBottom = 2,
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
    };

    private void Register(string commandId, string caption, Button button)
    {
        _buttonByCommand[commandId] = button;
        _captionByCommand[commandId] = caption;
    }

    private void ApplyHints()
    {
        foreach (var (commandId, button) in _buttonByCommand)
        {
            button.TooltipText = _hintsEnabled ? _captionByCommand[commandId] : string.Empty;
        }
    }

    private static Color ToColor(MarkerTint tint) => new(tint.Red, tint.Green, tint.Blue, tint.Alpha);

    private static string NameFor(string commandId) => "Command_" + commandId.Replace('.', '_');
}
