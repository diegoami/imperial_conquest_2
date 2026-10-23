using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// #1293: the in-game Settings. On the real <see cref="MainGameScreen"/>, Game → Settings opens the
/// Settings screen over the game; a colour scheme applies at once to the theme and to labels already on
/// screen, and switching back restores the shipped colours exactly; the font, interface size and battle
/// speed apply; every change is written to the settings file and reads back equal; Esc closes the screen.
/// </summary>
/// <remarks>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/SettingsCheck.tscn --quit-after 900
/// </code>
/// </remarks>
public partial class SettingsCheck : Control
{
    private const int SettleFrames = 4;

    private MainGameScreen _game = null!;
    private readonly Queue<Action> _steps = new();
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ic2-settings-check-{Guid.NewGuid():N}.json");
    private bool _ok = true;
    private int _frame;
    private Color _darkText;
    private Color _darkBackground;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        SettingsScreen.SettingsPath = _file;
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        _game = new MainGameScreen { Session = new GameSession(data.World, data.Ruleset, data.Scenario), RepositoryRoot = GameDataContext.RepositoryRoot };
        _game.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_game);

        _steps.Enqueue(() =>
        {
            _darkText = AppTheme.Color("text");
            _darkBackground = AppTheme.Color("background");
            Check(GameCommandTable.Rows.Any(r => r.Id == "game.settings" && r.Menu == "Game"), "the Game menu has a Settings row");
            _game.MenuBar.PressItemForCheck("game.settings");
            Check(_game.ActiveOverlay is SettingsScreen { InGame: true }, "Game → Settings opens the Settings screen over the game");
        });
        _steps.Enqueue(() =>
        {
            var settings = (SettingsScreen)_game.ActiveOverlay!;
            Check(settings.Tabs.GetTabCount() == 2
                  && settings.Tabs.GetTabTitle(0) == "Display & controls"
                  && settings.Tabs.GetTabTitle(1) == "Automation",
                "Settings has display and global Automation tabs");
            settings.Tabs.CurrentTab = 1;
            var supply = settings.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>()
                .Single(box => box.Text == "Auto supply armies");
            supply.ButtonPressed = true;
            supply.EmitSignal(BaseButton.SignalName.Toggled, true);
            Check(SettingsScreen.Current.AutoSupplyArmies && UserSettings.Load(_file).AutoSupplyArmies,
                "the Automation tab switch commits to the global settings file");
            Check(settings.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().Count() == 5,
                "Automation exposes army/fleet targets and treasury reserve");
            var fleetRepair = settings.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>()
                .Single(box => box.Text == "Auto repair fleets");
            fleetRepair.EmitSignal(BaseButton.SignalName.Toggled, true);
            Check(SettingsScreen.Current.AutoRepairFleets && UserSettings.Load(_file).AutoRepairFleets,
                "the fleet repair switch commits to the global settings file");
            var mobilize = settings.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>()
                .Single(box => box.Text == "Auto mobilize ready units");
            mobilize.EmitSignal(BaseButton.SignalName.Toggled, true);
            Check(SettingsScreen.Current.AutoMobilizeReady && UserSettings.Load(_file).AutoMobilizeReady,
                "the auto mobilize switch commits to the global settings file");
            SettingsScreen.Commit(SettingsScreen.Current with { AutoSupplyArmies = false });
            SettingsScreen.Commit(SettingsScreen.Current with { AutoRepairFleets = false });
            SettingsScreen.Commit(SettingsScreen.Current with { AutoMobilizeReady = false });
            settings.Tabs.CurrentTab = 0;
        });
        _steps.Enqueue(() =>
        {
            var before = LabelsWithTextColour(_darkText);
            SettingsScreen.Commit(SettingsScreen.Current with { ColorScheme = "parchment" }, GetTree().Root);
            var parchment = ColorSchemes.ById("parchment");
            Check(Same(AppTheme.Color("background"), Color.FromHtml(parchment.Background)), "the theme's background is Parchment's");
            Check(Same(AppTheme.Color("text"), Color.FromHtml(parchment.Text)), "the theme's text colour is Parchment's");
            Check(before > 0 && LabelsWithTextColour(Color.FromHtml(parchment.Text)) >= before && LabelsWithTextColour(_darkText) == 0,
                $"every label already on screen in the old text colour is retinted ({before} before)");
            Check(UserSettings.Load(_file).ColorScheme == "parchment", "the scheme is saved to the settings file");
        });
        _steps.Enqueue(() =>
        {
            SettingsScreen.Commit(SettingsScreen.Current with { ColorScheme = "dark" }, GetTree().Root);
            Check(Same(AppTheme.Color("text"), _darkText), "back on Dark, the text colour is the shipped one");
            Check(Same(AppTheme.Color("background"), _darkBackground), "back on Dark, the background is the shipped one");
        });
        _steps.Enqueue(() =>
        {
            SettingsScreen.Commit(SettingsScreen.Current with { Font = "atkinson", TextScalePercent = 130 }, GetTree().Root);
            var body = (FontVariation)AppTheme.Current.DefaultFont;
            Check(body.BaseFont?.ResourcePath == UiFonts.ById("atkinson").ResourcePath, $"the body font is Atkinson Hyperlegible ({body.BaseFont?.ResourcePath})");
            Check(Mathf.IsEqualApprox(GetTree().Root.ContentScaleFactor, 1.3f), $"the interface size is 130% ({GetTree().Root.ContentScaleFactor})");
            SettingsScreen.Commit(SettingsScreen.Current with { Font = "source-sans", TextScalePercent = 100 }, GetTree().Root);
            Check(body.BaseFont?.ResourcePath == UiFonts.ById("source-sans").ResourcePath, "Source Sans 3 is restored");
        });
        _steps.Enqueue(() =>
        {
            SettingsScreen.Commit(SettingsScreen.Current.WithBattleSpeed(BattleSpeeds.All.Single(s => s.Id == "fast")));
            Check(SettingsScreen.BattleShotDelaySeconds == 0.3 && SettingsScreen.BattleAttackDelaySeconds == 1.0, "Fast sets the battle pauses");
            Check(UserSettings.Load(_file) == SettingsScreen.Current, "the settings file reads back equal to the settings in force");
        });
        _steps.Enqueue(() =>
        {
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
            Check(_game.ActiveOverlay is null, "Esc closes the in-game Settings");
        });

        // #1280 F2: with "Confirm moves with a second click" on, the first click on an empty tile only marks
        // it; the second click on the same tile moves (MapClickCheck's army-0 to (100,36), Rome seated).
        _steps.Enqueue(() =>
        {
            RemoveChild(_game);
            _game.QueueFree();
            var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
            _game = new MainGameScreen
            {
                Session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome"),
                RepositoryRoot = GameDataContext.RepositoryRoot,
            };
            _game.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_game);
            SettingsScreen.Commit(SettingsScreen.Current with { ConfirmMoves = true });
        });
        _steps.Enqueue(() =>
        {
            // #1284: hovering a marker names it.
            _game.MapView.CentreOnTile(100, 37);
            var hover = _game.MapView.HoverText(_game.MapView.TileCenterForCheck(100, 37)) ?? "";
            Check(hover.StartsWith("Army of Rome · ", StringComparison.Ordinal) && hover.EndsWith(" troops", StringComparison.Ordinal),
                $"hovering army-0 names it ('{hover}')");
            ClickTile(100, 37);
        });
        _steps.Enqueue(() =>
        {
            Check(_game.MapView.PendingMoveForCheck is null, "selecting an army marks no move");
            ClickTile(100, 36);
        });
        _steps.Enqueue(() =>
        {
            var army = _game.Session.State.ArmyById("army-0")!;
            Check(army.X == 100 && army.Y == 37, $"the first click on (100,36) does not move army-0 (at {army.X},{army.Y})");
            Check(_game.MapView.PendingMoveForCheck is { X: 100, Y: 36 }, "the first click marks (100,36) as the destination");
            Check(_game.LastMessageForCheck.Contains("click it again", StringComparison.Ordinal), $"the message line says how to confirm ('{_game.LastMessageForCheck}')");
            ClickTile(100, 36);
        });
        _steps.Enqueue(() =>
        {
            var army = _game.Session.State.ArmyById("army-0")!;
            Check(army.X == 100 && army.Y == 36, $"the second click on (100,36) moves army-0 there (at {army.X},{army.Y})");
            Check(_game.MapView.PendingMoveForCheck is null, "the move clears the marked destination");
            SettingsScreen.Commit(SettingsScreen.Current with { ConfirmMoves = false });
        });
    }

    public override void _Process(double delta)
    {
        if (++_frame < SettleFrames)
        {
            return;
        }

        _frame = 0;
        try
        {
            if (_steps.Count > 0)
            {
                _steps.Dequeue()();
                return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SettingsCheck: unhandled exception: {ex}");
            _ok = false;
        }

        SetProcess(false);
        SettingsScreen.SettingsPath = null;
        if (File.Exists(_file)) File.Delete(_file);
        GD.Print($"SettingsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void ClickTile(int x, int y)
    {
        var map = _game.MapView;
        map.CentreOnTile(x, y);
        var position = map.TileCenterForCheck(x, y);
        map._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position });
        map._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
    }

    private int LabelsWithTextColour(Color colour) =>
        _game.FindChildren("*", "Label", true, false).OfType<Label>()
            .Count(l => l.HasThemeColorOverride("font_color") && Same(l.GetThemeColor("font_color"), colour));

    private static bool Same(Color a, Color b) =>
        Math.Abs(a.R - b.R) < 0.003f && Math.Abs(a.G - b.G) < 0.003f && Math.Abs(a.B - b.B) < 0.003f;

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
