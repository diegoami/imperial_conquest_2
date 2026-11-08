using Godot;
using IC2.Engine.Serialization;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class CampaignGuideCheck : Control
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            Theme = AppTheme.Current;
            var repository = GameDataContext.Repository;
            var catalog = CampaignGuideCatalog.Load(repository);
            foreach (var scheme in ColorSchemes.All)
            {
                Appearance.ApplyColors(scheme, this);
                var chooser = new RulesetChooserScreen();
                chooser.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(chooser);
                Check(chooser.SelectedPreset == RulesetPreset.ClassicalFaithful, "Classical remains the default");
                foreach (var size in new[] { new Vector2I(1500, 850), new Vector2I(1000, 650) })
                {
                    Size = size;
                    if (DisplayServer.GetName() != "headless")
                    {
                        GetWindow().ContentScaleSize = size;
                        GetWindow().Size = size == new Vector2I(1000, 650) ? new Vector2I(1500, 975) : size;
                    }
                    await Settle();
                    foreach (var card in RulesetPresets.CardsFor(repository))
                    {
                        var button = card.Preset == RulesetPreset.Listed ? chooser.ScenarioCardButtons[card.ScenarioId] : chooser.CardButtons[card.Preset];
                        button.EmitSignal(BaseButton.SignalName.Pressed);
                        await Settle();
                        var guide = catalog.ForScenario(card.ScenarioId)!;
                        var labels = chooser.DescriptionScroll.FindChildren("*", "Label", true, false).OfType<Label>().ToArray();
                        Check(chooser.SelectedCard.ScenarioId == card.ScenarioId && labels.Any(l => l.Text == guide.Background), card.ScenarioId + " updates actual background");
                        Check(labels.Any(l => l.Text == guide.Validation) && labels.Any(l => l.Text == guide.Abstractions), "advice evidence and abstractions are visible content");
                        foreach (var power in guide.Powers)
                            Check(labels.Any(l => l.Text == power.Advice), "actual power advice is rendered: " + power.NationId);
                        Check(chooser.DescriptionScroll.ScrollVertical == 0, "selection resets description to the top");
                        var bounds = new Rect2(GlobalPosition, Size);
                        Check(bounds.Encloses(chooser.ContinueButton.GetGlobalRect()) && bounds.Encloses(chooser.DescriptionScroll.GetGlobalRect()), scheme.Id + "/" + size + ": description and Continue fit");
                        chooser.DescriptionScroll.ScrollVertical = int.MaxValue;
                        await Settle();
                        Check(chooser.DescriptionScroll.ScrollVertical > 0, "long guidance scrolls");
                        var final = chooser.DescriptionScroll.FindChildren("*", "Label", true, false).OfType<Label>().Last();
                        Check(chooser.DescriptionScroll.GetGlobalRect().Encloses(final.GetGlobalRect()), "last rules paragraph is reachable");
                    }
                    var first = chooser.CardButtons[RulesetPreset.ClassicalFaithful];
                    first.GrabFocus();
                    await Settle();
                    first.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Pressed = true, Keycode = Key.Down });
                    await Settle();
                    Check(chooser.SelectedPreset == RulesetPreset.Improved && chooser.CardButtons[RulesetPreset.Improved].HasFocus(), "keyboard Down updates selection and advice");
                    RulesetCard? continued = null;
                    Action<RulesetCard> capture = card => continued = card;
                    chooser.Continued += capture;
                    chooser.ContinueButton.EmitSignal(BaseButton.SignalName.Pressed);
                    chooser.Continued -= capture;
                    Check(continued?.ScenarioId == "example-classical-improved", "Continue preserves selected scenario");
                    if (DisplayServer.GetName() != "headless")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/ui/v0.11-guidance"));
                        System.IO.Directory.CreateDirectory(output);
                        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, $"{scheme.Id}-{size.X}.png")) == Error.Ok, "saved guidance capture");
                    }
                }
                RemoveChild(chooser); chooser.QueueFree();
                await Settle();
            }
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        GD.Print($"CampaignGuideCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }
    private async Task Settle() { for (var i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string message) { if (!condition) GD.PrintErr("FAIL: " + message); _ok &= condition; }
}
