using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class UnitActionIconsCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        var originalColors = Appearance.AppliedColors;
        try
        {
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var session = new GameSession(data.World, data.Ruleset, data.Scenario);
            var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
                Size = GetViewport().GetVisibleRect().Size };
            AddChild(main);
            var before = session.State;
            var button = main.RetreatSupplyButtonForCheck;
            foreach (var fleet in new[] { false, true })
            {
                if (fleet) main.SelectFleetForCheck(session.State.Fleets.First(f => f.Nation == session.State.ActiveNationId).Id);
                else main.SelectArmyForCheck(session.State.Armies.First(a => a.Nation == session.State.ActiveNationId).Id);
                Check(main.CommandStrip.IsAncestorOf(button) && !main.Toolbar.ExtraActions.IsAncestorOf(button),
                    "existing recovery icon is grouped with selected army/fleet actions");
                Check(button.Icon is not null && button.Text.Length == 0 && button.TooltipText.Length > 0,
                    "recovery remains a compact icon with a clear hint");
                Check(button == main.RetreatSupplyButtonForCheck, "selection changes preserve the action instance and handlers");
            }
            Check(session.State == before, "selection/layout changes no campaign state or RNG");
            foreach (var name in QolCommandIcons.IllustratedNames)
            {
                var probe = new Button();
                QolCommandIcons.Apply(probe, name, "Action " + name);
                Check(QolCommandIcons.IsIllustrated(probe.Icon) && probe.Text.Length == 0
                    && probe.AccessibilityName.Length > 0, name + " loads its illustrated asset and accessible name");
                if (probe.Icon is { } texture)
                {
                    var pixels = texture.GetImage(); pixels.Convert(Image.Format.Rgba8);
                    var bytes = pixels.GetData(); var colors = new HashSet<uint>();
                    for (var i = 0; i < bytes.Length; i += 4)
                        if (bytes[i + 3] > 127) colors.Add((uint)((bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2]));
                    Check(pixels.GetWidth() == 24 && pixels.GetHeight() == 24 && pixels.GetPixel(0, 0).A == 0
                        && colors.Count >= 4, name + " decodes natively with transparency and illustration colors");
                }
                probe.Free();
            }
            foreach (var scheme in ColorSchemes.All)
            {
                Appearance.ApplyColors(scheme, GetTree().Root);
                Check(button.GetThemeColor("icon_normal_color") == Colors.White
                    && button.GetThemeColor("icon_disabled_color").A < 0.5f,
                    scheme.Id + " keeps live illustrated colors and a distinct disabled state");
            }
            WritePreview();
            RemoveChild(main); main.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"UnitActionIconsCheck: {ex}"); _ok = false; }
        finally { Appearance.ApplyColors(originalColors, GetTree().Root); }
        GD.Print($"UnitActionIconsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void WritePreview()
    {
        var names = QolCommandIcons.IllustratedNames;
        var sheet = Image.CreateEmpty(names.Count * 48, ColorSchemes.All.Count * 5 * 48, false, Image.Format.Rgba8);
        for (var row = 0; row < ColorSchemes.All.Count; row++)
        {
            var scheme = ColorSchemes.All[row];
            Appearance.ApplyColors(scheme, GetTree().Root);
            for (var column = 0; column < names.Count; column++)
            {
                var button = UiKit.MakeButton("", () => { });
                QolCommandIcons.Apply(button, names[column], names[column]);
                var states = new[] { ("normal", "icon_normal_color"), ("hover", "icon_hover_color"),
                    ("pressed", "icon_pressed_color"), ("focus", "icon_focus_color"), ("disabled", "icon_disabled_color") };
                for (var state = 0; state < states.Length; state++)
                {
                    var tile = new Vector2I(column * 48, (row * 5 + state) * 48);
                    sheet.FillRect(new Rect2I(tile.X, tile.Y, 48, 48), Color.FromHtml(scheme.Panel));
                    var box = button.GetThemeStylebox(state == 3 ? "normal" : states[state].Item1) as StyleBoxFlat;
                    if (box is not null) sheet.FillRect(new Rect2I(tile.X + 4, tile.Y + 4, 40, 40), box.BgColor);
                    if (state == 3 && button.GetThemeStylebox("focus") is StyleBoxFlat focus)
                    {
                        sheet.FillRect(new Rect2I(tile.X + 3, tile.Y + 3, 42, 42), focus.BorderColor);
                        sheet.FillRect(new Rect2I(tile.X + 5, tile.Y + 5, 38, 38), box?.BgColor ?? Color.FromHtml(scheme.Panel));
                    }
                    if (button.Icon is null) continue;
                    var original = button.Icon.GetImage();
                    var image = Image.CreateFromData(original.GetWidth(), original.GetHeight(), original.HasMipmaps(),
                        original.GetFormat(), original.GetData());
                    var tint = button.GetThemeColor(states[state].Item2);
                    for (var y = 0; y < image.GetHeight(); y++)
                    for (var x = 0; x < image.GetWidth(); x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        image.SetPixel(x, y, new Color(pixel.R * tint.R, pixel.G * tint.G, pixel.B * tint.B, pixel.A * tint.A));
                    }
                    sheet.BlendRect(image, new Rect2I(0, 0, 24, 24), new Vector2I(tile.X + 12, tile.Y + 12));
                }
                button.Free();
            }
        }
        var folder = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "assumptions-audit");
        Directory.CreateDirectory(folder);
        Check(sheet.SavePng(Path.Combine(folder, "qol-icons-preview.png")) == Error.Ok, "loaded texture/theme-state preview is reproducible");
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
