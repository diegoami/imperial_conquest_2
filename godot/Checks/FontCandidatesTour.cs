using Godot;
using GodotDictionary = Godot.Collections.Dictionary;

namespace IC2.Slice.Checks;

/// <summary>
/// T168 (#1072): a windowed tour that renders the same sample of every kind of surface (a menu title, a
/// menu bar, a dialog header with a table, an army panel of figures, a news log) once per candidate font
/// pairing and palette, and writes one PNG per candidate. The first PNG is the baseline: the font and
/// colours the game ships today at today's sizes, so the user compares like with like. Nothing in the
/// game is touched: the sample is built here from plain controls, and the candidate fonts are read from
/// <c>res://Assets/Fonts/candidates/</c> as raw bytes (no import step). It cannot run headless (#156)
/// and its name does not end in <c>Check</c>, so CI's loop skips it:
/// <c>godot --path godot res://Checks/FontCandidatesTour.tscn --quit-after 800</c> with
/// <c>IC2_SCREENSHOT_DIR</c> set to a directory (use the git-ignored <c>rendered/</c>). It also prints
/// the WCAG contrast ratio of every palette's text colours on every panel colour.
/// </summary>
public partial class FontCandidatesTour : Control
{
    private const int SettleFrames = 8;

    private sealed record Palette(string Name, string Slug, Color Bg, Color Panel, Color Raised, Color Text, Color Muted, Color Accent);

    private sealed record Pairing(string Name, string Slug, Font? Display, Font? Body, bool Compact);

    private static readonly Palette Current = new("Current (UiKit)", "current",
        C("121C26"), C("1C2938"), C("263647"), C("EDF0EB"), C("ADB5BD"), C("DBA845"));

    private static readonly Palette[] Palettes =
    {
        new("Bronze Night", "bronze", C("14110D"), C("1E1912"), C("2C251B"), C("EDE3CC"), C("B8AB8E"), C("D9A55B")),
        new("Parchment", "parchment", C("CDBF9D"), C("E6D9B8"), C("F1E7CC"), C("2A2015"), C("52432A"), C("7A1F52")),
        new("Stone and Tyrian", "stone", C("1B1C1F"), C("26282C"), C("33363B"), C("ECEAE4"), C("B0B2B3"), C("DA94C2")),
    };

    private readonly List<(Pairing Pairing, Palette Palette)> _plan = new();
    private string _outputDirectory = string.Empty;
    private Control? _board;
    private int _frame;
    private int _index;

    private static Color C(string hex) => new(hex);

    public override void _Ready()
    {
        _outputDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            GD.PrintErr("FontCandidatesTour: IC2_SCREENSHOT_DIR is not set. Nothing will be captured.");
        }
        else
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        Size = GetViewport().GetVisibleRect().Size;

        _plan.Add((new Pairing("Baseline: Godot default font, today's sizes", "00-baseline", null, null, false), Current));
        var pairings = BuildPairings();
        foreach (var palette in Palettes)
        {
            foreach (var pairing in pairings)
            {
                _plan.Add((pairing, palette));
            }
        }

        PrintContrast();
    }

    private static Pairing[] BuildPairings()
    {
        var cinzel = Variable("Cinzel/Cinzel-wght.ttf", 500);
        var marcellus = Plain("Marcellus/Marcellus-Regular.ttf");
        var cormorant = Plain("CormorantSC/CormorantSC-SemiBold.ttf");
        var sourceSans = Variable("SourceSans3/SourceSans3-wght.ttf", 400);
        var plex = Plain("IBMPlexSansCondensed/IBMPlexSansCondensed-Regular.ttf");
        var alegreya = Tabular(Plain("AlegreyaSans/AlegreyaSans-Regular.ttf"));
        var fira = Tabular(Plain("FiraSansCondensed/FiraSansCondensed-Regular.ttf"));
        return new[]
        {
            new Pairing("A: Cinzel + Source Sans 3", "A-cinzel-sourcesans3", cinzel, sourceSans, true),
            new Pairing("B: Marcellus + IBM Plex Sans Condensed", "B-marcellus-plexcondensed", marcellus, plex, true),
            new Pairing("C: Cormorant SC + Alegreya Sans", "C-cormorantsc-alegreyasans", cormorant, alegreya, true),
            new Pairing("D: Cinzel + Fira Sans Condensed", "D-cinzel-firacondensed", cinzel, fira, true),
        };
    }

    private static FontFile Load(string relative)
    {
        var path = ProjectSettings.GlobalizePath("res://Assets/Fonts/candidates/" + relative);
        return new FontFile { Data = File.ReadAllBytes(path) };
    }

    private static Font Plain(string relative) => Load(relative);

    private static Font Variable(string relative, int weight) => new FontVariation
    {
        BaseFont = Load(relative),
        VariationOpentype = new GodotDictionary { { "wght", weight } },
    };

    /// <summary>Turns on the OpenType <c>tnum</c> feature (tabular figures) for faces whose default digits are proportional.</summary>
    private static Font Tabular(Font font) => new FontVariation
    {
        BaseFont = font,
        OpentypeFeatures = new GodotDictionary { { "tnum", 1 } },
    };

    public override void _Process(double delta)
    {
        if (_index >= _plan.Count)
        {
            GD.Print("FontCandidatesTour: done.");
            GetTree().Quit(0);
            return;
        }

        if (_board is null)
        {
            _board = BuildBoard(_plan[_index].Pairing, _plan[_index].Palette);
            AddChild(_board);
            _frame = 0;
            return;
        }

        _frame++;
        if (_frame < SettleFrames)
        {
            return;
        }

        var (pairing, palette) = _plan[_index];
        var name = _index == 0 ? "00-baseline.png" : $"{_index:00}-{pairing.Slug}-{palette.Slug}.png";
        Capture(name);
        _board.QueueFree();
        _board = null;
        _index++;
    }

    private void Capture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            return;
        }

        var path = Path.Combine(_outputDirectory, fileName);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok
            ? $"FontCandidatesTour: saved {path}"
            : $"FontCandidatesTour: could not save {path}: {error}");
    }

    // ---- the sample board -------------------------------------------------------------------

    private Control BuildBoard(Pairing pairing, Palette p)
    {
        var compact = pairing.Compact;
        var board = new Control { Size = Size };
        var bg = new ColorRect { Color = p.Bg };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        board.AddChild(bg);

        // The proposed type scale (base pixels at the 1500 x 850 design viewport) against today's.
        var body = compact ? 13 : 14;
        var small = 12;
        var header = compact ? 17 : 18;
        var title = compact ? 34 : 32;
        var menu = compact ? 14 : 16;

        var theme = new Theme();
        if (pairing.Body is not null)
        {
            theme.DefaultFont = pairing.Body;
        }

        theme.DefaultFontSize = body;
        board.Theme = theme;

        var root = new VBoxContainer { Size = Size };
        root.AddThemeConstantOverride("separation", compact ? 6 : 8);
        board.AddChild(root);

        // Menu bar (display face).
        var bar = Panel(p.Raised, 4, 2);
        var barRow = new HBoxContainer();
        barRow.AddThemeConstantOverride("separation", 18);
        bar.AddChild(barRow);
        foreach (var item in new[] { "File", "Game", "Strategy", "Nations", "Area map", "Unit map", "Help" })
        {
            barRow.AddChild(Lbl(item, menu, p.Text, pairing.Display));
        }

        barRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        barRow.AddChild(Lbl("Week 1, 270 BC", menu, p.Accent, pairing.Display));
        root.AddChild(bar);

        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 12);
        root.AddChild(middle);

        middle.AddChild(MenuBlock(p, pairing, title, body));
        middle.AddChild(TransferDialog(p, pairing, header, body, small, compact));
        middle.AddChild(ArmyPanel(p, pairing, header, body, small, compact));

        root.AddChild(NewsLog(p, pairing, header, body, compact));

        var caption = Lbl($"{pairing.Name}   |   palette: {p.Name}   |   body {body}px, header {header}px, title {title}px",
            small, p.Muted, null);
        caption.Position = new Vector2(12, Size.Y - 22);
        board.AddChild(caption);
        return board;
    }

    private static PanelContainer Panel(Color color, int marginX = 12, int marginY = 10)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = marginX + 6,
            ContentMarginRight = marginX + 6,
            ContentMarginTop = marginY,
            ContentMarginBottom = marginY,
        });
        return panel;
    }

    private static Label Lbl(string text, int size, Color color, Font? font, int lineSpacing = 0)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (font is not null)
        {
            label.AddThemeFontOverride("font", font);
        }

        if (lineSpacing != 0)
        {
            label.AddThemeConstantOverride("line_spacing", lineSpacing);
        }

        return label;
    }

    private static Button Btn(string text, Palette p, Font? font, int size)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, size + 14) };
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", p.Text);
        if (font is not null)
        {
            button.AddThemeFontOverride("font", font);
        }

        var style = new StyleBoxFlat
        {
            BgColor = p.Raised,
            BorderColor = p.Accent,
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
        };
        button.AddThemeStyleboxOverride("normal", style);
        return button;
    }

    private static Control MenuBlock(Palette p, Pairing pairing, int title, int body)
    {
        var panel = Panel(p.Panel, 16, 16);
        panel.CustomMinimumSize = new Vector2(380, 0);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        var t = Lbl("Imperial Conquest 2", title, p.Accent, pairing.Display);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(t);
        var s = Lbl("A reimplementation of the 1999 strategy game", body, p.Muted, null);
        s.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(s);
        foreach (var item in new[] { "New Game", "Load", "Settings", "Quit" })
        {
            column.AddChild(Btn(item, p, pairing.Display, body + 2));
        }

        return panel;
    }

    private static Control TransferDialog(Palette p, Pairing pairing, int header, int body, int small, bool compact)
    {
        var panel = Panel(p.Panel);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", compact ? 4 : 8);
        panel.AddChild(column);
        column.AddChild(Lbl("Army to army transfer", header, p.Accent, pairing.Display));
        column.AddChild(Lbl("Army A12 (Rome): 5,000 troops    Army A7 (Rome): 3,200 troops    Combined: 8,200", body, p.Text, null));

        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", compact ? 1 : 4);
        column.AddChild(grid);
        foreach (var head in new[] { "Unit", "Troops", "Quality", "Unit", "Troops", "Quality" })
        {
            grid.AddChild(Lbl(head, small, p.Muted, null));
        }

        var rows = new[]
        {
            new[] { "Heavy infantry", "2,000", "veteran", "Light infantry", "1,200", "regular" },
            new[] { "Archers", "1,500", "regular", "Cavalry", "800", "elite" },
            new[] { "Light cavalry", "900", "regular", "Archers", "600", "regular" },
            new[] { "Slingers", "600", "poor", "Heavy infantry", "600", "regular" },
        };
        foreach (var row in rows)
        {
            foreach (var cell in row)
            {
                var l = Lbl(cell, body, p.Text, null);
                if (char.IsDigit(cell[0]))
                {
                    l.HorizontalAlignment = HorizontalAlignment.Right;
                }

                grid.AddChild(l);
            }
        }

        column.AddChild(Lbl("Supply: 480 t here, 120 t there. Net 0 t.    Money: 1,200 / 300 talents.", small, p.Muted, null));
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(Btn("Transfer", p, pairing.Display, body));
        buttons.AddChild(Btn("Disband", p, pairing.Display, body));
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(Btn("Close", p, pairing.Display, body));
        column.AddChild(buttons);
        return panel;
    }

    private static Control ArmyPanel(Palette p, Pairing pairing, int header, int body, int small, bool compact)
    {
        var panel = Panel(p.Panel);
        panel.CustomMinimumSize = new Vector2(340, 0);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", compact ? 3 : 4);
        panel.AddChild(column);
        column.AddChild(Lbl("Army — A12", header, p.Accent, pairing.Display));
        var lines = new[]
        {
            "Army of Rome", "Moves: 6", "Supply: 480 tons  (80%)", "Morale: high", "Money: 1200 talents",
            "Terrain: plains", "", "Total troops: 5000", "No. of units: 6",
            "Regulars cost: 420 talents per quarter", "Mercenary pay: 90 talents per quarter", "",
            "2000x heavy_infantry (veteran)", "1500x archers (regular)", "900x light_cavalry (regular)",
        };
        if (compact)
        {
            // One multi-line label with a tightened line spacing: the proposed army panel.
            column.AddChild(Lbl(string.Join('\n', lines), body, p.Text, null, lineSpacing: -1));
        }
        else
        {
            // Today's panel: one label per line (Heading/Fact/Blank), the container's default 4px apart.
            foreach (var line in lines)
            {
                column.AddChild(Lbl(line, line.Length == 0 ? 8 : body, p.Text, null));
            }
        }

        column.AddChild(Lbl("Orders for this army are in the Unit map menu.", small, p.Muted, null));
        return panel;
    }

    private static Control NewsLog(Palette p, Pairing pairing, int header, int body, bool compact)
    {
        var panel = Panel(p.Panel);
        panel.CustomMinimumSize = new Vector2(0, 150);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", compact ? 3 : 6);
        panel.AddChild(column);
        column.AddChild(Lbl("News", header, p.Accent, pairing.Display));
        var text = Lbl(
            "Dacia takes its turn: 4 orders issued.\nPtolemaic takes its turn: 15 orders issued.\nNumidia takes its turn: 6 orders issued.\nArmy A12 (Rome) arrives at Capua; supply 480 t, morale high.\nCarthage offers peace to Rome: 300 talents a quarter.",
            body, p.Text, null, lineSpacing: compact ? -1 : 0);
        column.AddChild(text);
        return panel;
    }

    // ---- contrast -----------------------------------------------------------------------------

    private static double Luminance(Color c)
    {
        static double Lin(double v) => v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static double Ratio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (System.Math.Max(la, lb) + 0.05) / (System.Math.Min(la, lb) + 0.05);
    }

    private static void PrintContrast()
    {
        foreach (var p in new[] { Current }.Concat(Palettes))
        {
            foreach (var (label, fg) in new[] { ("text", p.Text), ("muted", p.Muted), ("accent", p.Accent) })
            {
                GD.Print(FormattableString.Invariant(
                    $"contrast {p.Name}: {label} on bg {Ratio(fg, p.Bg):0.00}, panel {Ratio(fg, p.Panel):0.00}, raised {Ratio(fg, p.Raised):0.00}"));
            }
        }
    }
}
