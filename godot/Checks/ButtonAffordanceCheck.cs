using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>#1721: production button affordance and palette-switching checks; optional authored UI captures.</summary>
public partial class ButtonAffordanceCheck : Control
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            foreach (var scheme in ColorSchemes.All.Concat(new[] { ColorSchemes.ById("parchment") }))
            {
                Appearance.ApplyColors(scheme, GetTree().Root);
                var panel = UiKit.MakePanel(UiKit.PanelColor);
                panel.Position = new Vector2(24, 24);
                panel.CustomMinimumSize = new Vector2(600, 300);
                AddChild(panel);
                var column = new VBoxContainer(); panel.AddChild(column);
                column.AddChild(UiKit.MakeLabel(scheme.Name + " — action buttons", AppTheme.Header));
                column.AddChild(UiKit.MakeLabel("Available actions remain outlined before hovering."));
                var clicks = 0;
                var button = UiKit.MakeButton("Accept trade offer", () => clicks++);
                column.AddChild(button);
                var disabled = UiKit.MakeButton("Unavailable action", () => { });
                disabled.Disabled = true; column.AddChild(disabled);
                var pressed = UiKit.MakeButton("Selected action", () => { });
                pressed.ToggleMode = true; pressed.ButtonPressed = true; column.AddChild(pressed);
                var focus = UiKit.MakeButton("Keyboard focus", () => { }); column.AddChild(focus);
                focus.GrabFocus();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                var normal = (StyleBoxFlat)button.GetThemeStylebox("normal");
                var down = (StyleBoxFlat)button.GetThemeStylebox("pressed");
                var off = (StyleBoxFlat)disabled.GetThemeStylebox("disabled");
                Check(normal.BorderWidthLeft >= 1 && Contrast(normal.BorderColor, normal.BgColor) >= 3
                      && Contrast(normal.BorderColor, UiKit.PanelColor) >= 3,
                    scheme.Id + ": idle action has a contrasting boundary against both fill and panel");
                Check(normal.BorderWidthBottom > normal.BorderWidthTop && down.BorderWidthTop > down.BorderWidthBottom
                      && normal.ShadowSize > down.ShadowSize,
                    scheme.Id + ": raised idle and recessed pressed states have different geometry");
                Check(off.BorderWidthLeft == 0 && off.ShadowSize == 0 && !off.BgColor.IsEqualApprox(normal.BgColor),
                    scheme.Id + ": disabled action is flat and distinct from the available action");
                foreach (var (state, font) in new[] { ("normal", "font_color"), ("hover", "font_hover_color"),
                             ("pressed", "font_pressed_color"), ("hover_pressed", "font_hover_pressed_color"),
                             ("normal", "font_focus_color"), ("disabled", "font_disabled_color") })
                    Check(Contrast(button.GetThemeColor(font), ((StyleBoxFlat)button.GetThemeStylebox(state)).BgColor) >= 4.5,
                        scheme.Id + ": readable " + font);
                Check(focus.HasFocus() && ((StyleBoxFlat)focus.GetThemeStylebox("focus")).BorderWidthLeft >= 2,
                    scheme.Id + ": keyboard focus is visible without changing the label colour");
                var before = button.GetCombinedMinimumSize();
                button.EmitSignal(BaseButton.SignalName.Pressed);
                Check(clicks == 1 && button.GetCombinedMinimumSize() == before && !button.Flat,
                    scheme.Id + ": real button dispatches once with stable geometry");

                var output = System.Environment.GetEnvironmentVariable("IC2_BUTTON_REVIEW_DIR");
                if (!string.IsNullOrWhiteSpace(output) && DisplayServer.GetName() != "headless")
                {
                    System.IO.Directory.CreateDirectory(output);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, scheme.Id + ".png")) == Error.Ok,
                        scheme.Id + ": authored review image saved");
                }
                RemoveChild(panel); panel.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"ButtonAffordanceCheck: {ex}"); _ok = false; }
        GD.Print($"ButtonAffordanceCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        static double L(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
