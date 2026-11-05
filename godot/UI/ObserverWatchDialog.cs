using Godot;
using IC2.Engine.Model;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

public partial class ObserverWatchDialog : Control
{
    public required ValueList<NationState> Nations { get; init; }
    public ValueList<string>? Selected { get; init; }
    public event Action<ValueList<string>?>? Confirmed;
    public event Action? Cancelled;
    public readonly Dictionary<string, CheckBox> Choices = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, AppTheme.Scrim, 600);
        AddChild(root);
        content.AddChild(UiKit.MakeLabel("Watch civilizations", AppTheme.Header, UiKit.AccentColor));
        var buttons = new HBoxContainer();
        buttons.AddChild(UiKit.MakeButton("All", () => SetAll(true)));
        buttons.AddChild(UiKit.MakeButton("None · fast-forward all", () => SetAll(false)));
        content.AddChild(buttons);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(560, 300) };
        content.AddChild(scroll);
        var grid = new GridContainer { Columns = 2 };
        scroll.AddChild(grid);
        foreach (var nation in Nations.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            var choice = new CheckBox { Text = nation.Name + (nation.Eliminated ? " (conquered)" : ""),
                ButtonPressed = Selected is null || Selected.Contains(nation.Id) };
            Choices[nation.Id] = choice; grid.AddChild(choice);
        }
        var answers = new HBoxContainer(); content.AddChild(answers);
        answers.AddChild(UiKit.MakeButton("OK", Confirm));
        answers.AddChild(UiKit.MakeButton("Cancel", () => Cancelled?.Invoke()));
    }

    public void SetAll(bool value) { foreach (var choice in Choices.Values) choice.SetPressedNoSignal(value); }
    public void Confirm()
    {
        var chosen = Choices.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray();
        Confirmed?.Invoke(chosen.Length == Choices.Count ? null : ValueList.From(chosen));
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Cancelled?.Invoke(); }
    }
}
