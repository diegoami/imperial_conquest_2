using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ParchmentControlsCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            foreach (var scheme in ColorSchemes.All)
            {
                SettingsScreen.Commit(new UserSettings { ColorScheme = scheme.Id }, GetTree().Root);
                var settings = new SettingsScreen { InGame = true, Size = GetViewport().GetVisibleRect().Size };
                AddChild(settings);
                var note = settings.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Single(l => l.Text == "Changes apply at once and are remembered.");
                if (scheme.Id == "parchment")
                    Check(Contrast(note.GetThemeColor("font_color"), UiKit.PanelColor) >= 7
                          && Contrast(note.GetThemeColor("font_color"), UiKit.PanelColorRaised) >= 7,
                        "actual Parchment Settings note has strong contrast on both surfaces");
                RemoveChild(settings); settings.QueueFree();

                var data = GameDataContext.Repository.Resolve("classical-mediterranean");
                var session = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "rome");
                var before = session.State;
                var commands = 0;
                var recruit = new RecruitUnitDialog { Session = session, Size = GetViewport().GetVisibleRect().Size,
                    Submit = line => { commands++; return session.Submit(line).Lines; } };
                AddChild(recruit);
                var town = recruit.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().Single();
                await CheckPicker(town, scheme.Id + " recruitment");
                Check(town.ItemCount > 1, "recruitment fixture has alternative towns");
                var nextTown = (town.Selected + 1) % town.ItemCount;
                town.Select(nextTown);
                town.EmitSignal(OptionButton.SignalName.ItemSelected, (long)nextTown);
                Check(recruit.SelectedCityIdForCheck == town.GetItemMetadata(nextTown).AsString(), "town selection reaches the recruitment model");
                Check(commands == 0 && session.State == before, "browsing towns changes no campaign or RNG");
                RemoveChild(recruit); recruit.QueueFree();

                foreach (var fleetMode in new[] { false, true }) await CheckSupply(scheme.Id, fleetMode);
            }
        }
        catch (Exception ex) { GD.PrintErr($"ParchmentControlsCheck: {ex}"); _ok = false; }
        GD.Print($"ParchmentControlsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task CheckSupply(string scheme, bool fleetMode)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var army = original.State.Armies.First(a => a.Nation == original.State.ActiveNationId) with { X = 3, Y = 2 };
        var fleet = original.State.Fleets.First(f => f.Nation == army.Nation) with { X = 3, Y = 3, CarriedArmyId = null };
        var other = fleet with { Id = "selector-alternative" };
        var state = original.State with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet, other) };
        var save = new SaveGame(state.SchemaVersion, "selector-check", "selector-check", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new SupplyDialog { Session = session, ArmyId = fleetMode ? null : army.Id, FleetId = fleetMode ? fleet.Id : null,
            Size = GetViewport().GetVisibleRect().Size, Submit = line => { commands++; return session.Submit(line).Lines; } };
        AddChild(dialog);
        var picker = dialog.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().Single();
        await CheckPicker(picker, scheme + (fleetMode ? " fleet supply" : " army supply"));
        Check(picker.ItemCount > 1, "supply fixture has alternative money sources");
        picker.Select(1);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        var expected = dialog.ModelForCheck.MoneyViaChoices[1];
        Check(dialog.ModelForCheck.SelectedViaFleetId == expected.FleetId
              && dialog.ModelForCheck.SelectedViaArmyId == expected.ArmyId, "source selection reaches the supply model");
        Check(commands == 0 && session.State == state, "browsing money sources transfers no resources");
        RemoveChild(dialog); dialog.QueueFree();
    }

    private async Task CheckPicker(OptionButton picker, string context)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var box = (StyleBoxFlat)picker.GetThemeStylebox("normal");
        Check(box.BorderWidthLeft >= 1 && Contrast(box.BorderColor, box.BgColor) >= 3
              && !box.BgColor.IsEqualApprox(UiKit.PanelColor), context + ": unfocused dropdown has a contrasting outline and fill");
        Check(picker.GetThemeConstant("modulate_arrow") == 1 && picker.GetThemeIcon("arrow") is not null,
            context + ": dropdown arrow follows the readable text colour");
        foreach (var (state, font) in new[] { ("normal", "font_color"), ("hover", "font_hover_color"),
                     ("pressed", "font_pressed_color"), ("hover_pressed", "font_hover_pressed_color"),
                     ("normal", "font_focus_color"), ("disabled", "font_disabled_color") })
            Check(picker.GetThemeStylebox(state) is StyleBoxFlat surface
                  && Contrast(picker.GetThemeColor(font), surface.BgColor) >= 4.5,
                context + ": " + font + " remains readable on " + state);
        picker.ShowPopup();
        Check(picker.GetPopup().Visible, context + ": actual dropdown opens");
        picker.GetPopup().Hide();
    }

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
