using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class UnitActionIconsCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
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
            WritePreview();
            RemoveChild(main); main.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"UnitActionIconsCheck: {ex}"); _ok = false; }
        GD.Print($"UnitActionIconsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void WritePreview()
    {
        var names = new[] { "split", "recover", "rally", "rally_clear", "travel" };
        var sheet = Image.CreateEmpty(360, 128, false, Image.Format.Rgba8);
        for (var row = 0; row < 2; row++)
        {
            var scheme = ColorSchemes.ById(row == 0 ? "parchment" : "dark");
            sheet.FillRect(new Rect2I(0, row * 64, 360, 64), Color.FromHtml(scheme.Panel));
            for (var column = 0; column < names.Length; column++)
            {
                var button = new Button();
                QolCommandIcons.Apply(button, names[column], names[column]);
                var source = button.Icon.GetImage();
                var image = Image.CreateFromData(source.GetWidth(), source.GetHeight(), source.HasMipmaps(),
                    source.GetFormat(), source.GetData());
                var ink = Color.FromHtml(scheme.Text);
                for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    image.SetPixel(x, y, new Color(ink.R, ink.G, ink.B, pixel.A));
                }
                image.Resize(48, 48, Image.Interpolation.Lanczos);
                sheet.BlendRect(image, new Rect2I(0, 0, 48, 48), new Vector2I(column * 72 + 12, row * 64 + 8));
                button.Free();
            }
        }
        var folder = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "assumptions-audit");
        Directory.CreateDirectory(folder);
        Check(sheet.SavePng(Path.Combine(folder, "qol-icons-preview.png")) == Error.Ok, "original vector icon preview is reproducible");
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
