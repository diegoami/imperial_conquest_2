using Godot;
using IC2.Slice.Assets;

namespace IC2.Slice.UI;

/// <summary>
/// The Unit map's <strong>command strip</strong> — the original's context toolbar, which shows only the
/// selected unit's group: 7 army buttons, 6 fleet buttons or 1 city button, each the
/// <c>ui.command.*</c> icon of its <see cref="GameCommandTable"/> row, plus Cancel selection
/// <c>[derived: code, TUnitMap_AllButtonsOff / ArmyButtonsOn / FleetButtonsOn / CityButtonsOn;
/// docs/investigations/original-ui-command-audit.md §3.3]</c>. A fleet carrying an army shows the 6
/// fleet buttons then the 7 army buttons; a foreign unit, or no selection, shows nothing.
/// </summary>
/// <remarks>
/// <para>
/// Every button runs its table row through <see cref="GameCommandTable.TryInvoke"/>, so the strip and
/// the menu share one handler (<c>docs/game-design.md</c> "User interface" item 2). Which ids appear, in
/// which order, is the Godot-free <see cref="UnitCommandStripLayout"/>.
/// </para>
/// <para>
/// Like <see cref="CommandToolbar"/>, a button draws the row's own <c>ui.command.&lt;id&gt;.icon</c> key
/// through the pack loader and falls back to the caption as text when the pack has no texture for it, so
/// a missing texture never leaves an empty button.
/// </para>
/// </remarks>
public partial class UnitCommandStrip : PanelContainer
{
    /// <summary>The one table the strip's buttons look their rows up in.</summary>
    public required GameCommandTable Table { get; init; }

    /// <summary>The pack loader the icons come from, or <see langword="null"/> to fall back to captions.</summary>
    public required AssetPackTextureLoader? AssetLoader { get; init; }

    /// <summary>The selection the strip currently reflects.</summary>
    public UnitStripSelection Selection { get; private set; } = UnitStripSelection.None;

    /// <summary>Every button currently shown, in strip order — exposed for the headless checks.</summary>
    public IReadOnlyList<Button> Buttons { get; private set; } = Array.Empty<Button>();

    private HBoxContainer _row = null!;
    private readonly Dictionary<string, Button> _buttonByCommand = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 4);
        AddChild(_row);
        Rebuild();
    }

    /// <summary>
    /// Sets the selection and rebuilds the strip: the buttons change and the strip hides itself when
    /// there are none (a foreign unit or no selection).
    /// </summary>
    public void SetSelection(UnitStripSelection selection)
    {
        if (selection == Selection)
        {
            return;
        }

        Selection = selection;
        if (_row is not null)
        {
            Rebuild();
        }
    }

    /// <summary>The button for <paramref name="commandId"/>, or <see langword="null"/> when the strip does
    /// not show it.</summary>
    public Button? ButtonFor(string commandId) =>
        _buttonByCommand.TryGetValue(commandId, out var button) ? button : null;

    /// <summary>
    /// Presses <paramref name="commandId"/>'s strip button the way a click does — emitting its real
    /// <see cref="Button.Pressed"/> signal — and returns <see langword="false"/> when the strip does not
    /// show it.
    /// </summary>
    public bool PressForCheck(string commandId)
    {
        if (!_buttonByCommand.TryGetValue(commandId, out var button))
        {
            return false;
        }

        button.EmitSignal(Button.SignalName.Pressed);
        return true;
    }

    private void Rebuild()
    {
        foreach (var child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }

        _buttonByCommand.Clear();
        var buttons = new List<Button>();
        foreach (var commandId in UnitCommandStripLayout.CommandsFor(Selection))
        {
            var row = GameCommandTable.RowById(commandId);
            if (row is null)
            {
                continue;
            }

            var button = BuildButton(row);
            _row.AddChild(button);
            _buttonByCommand[row.Id] = button;
            buttons.Add(button);
        }

        Buttons = buttons;
        Visible = buttons.Count > 0;
    }

    private Button BuildButton(GameCommandRow row)
    {
        var button = new Button
        {
            Name = "Strip_" + row.Id.Replace('.', '_'),
            Disabled = !row.Wired,
            CustomMinimumSize = new Vector2(30, 30),
            TooltipText = row.Caption,
        };

        if (row.IconKey is { } iconKey && AssetLoader?.TryGetTexture(iconKey) is { } texture)
        {
            button.Icon = texture;
        }
        else
        {
            button.Text = row.Caption;
        }

        var commandId = row.Id;
        button.Pressed += () => Table.TryInvoke(commandId);
        return button;
    }
}
