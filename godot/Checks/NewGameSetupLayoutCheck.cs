using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T288: measure the real long-roster form, including resize and independent scrolling.</summary>
public partial class NewGameSetupLayoutCheck : Control
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            Theme = AppTheme.Current;
            var selection = new NewGameSelection
            {
                SelectedCard = RulesetPresets.CardForScenario(GameDataContext.Repository.Resolve("classical-mediterranean"))
            };
            var screen = new ScenarioSeatScreen { Selection = selection };
            screen.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(screen);
            var start = screen.FindChild("StartGame", true, false) as Button ?? throw new Exception("Start missing");
            var players = (ScrollContainer)screen.FindChild("PlayerScroll", true, false);
            var settings = (ScrollContainer)screen.FindChild("SettingsScroll", true, false);
            var loyalty = (HSlider)screen.FindChild("Loyaltytoalliances", true, false);
            var lastLeader = players.GetChild<GridContainer>(0).GetChildren().OfType<LineEdit>().Last();
            foreach (var (size, scale) in new[] { (new Vector2I(1500, 850), 1f), (new Vector2I(2560, 1351), 1f), (new Vector2I(1000, 650), 1.5f), (new Vector2I(750, 425), 2f), (new Vector2I(1500, 850), 1f) })
            {
                Size = size;
                if (DisplayServer.GetName() != "headless")
                {
                    GetWindow().ContentScaleSize = size;
                    GetWindow().Size = new Vector2I((int)(size.X * scale), (int)(size.Y * scale));
                }
                await Settle();
                var bounds = new Rect2(GlobalPosition, Size);
                Check(bounds.Encloses(start.GetGlobalRect()), $"Start fully visible at {size}");
                Check(bounds.Encloses(players.GetGlobalRect()) && players.Size.Y > 40, $"roster fits at {size}");
                Check(bounds.Encloses(settings.GetGlobalRect()) && settings.Size.Y > 20, $"settings fit at {size}");
                var pinned = start.GetGlobalRect();
                players.ScrollVertical = 10000;
                settings.ScrollVertical = 10000;
                await Settle();
                Check(start.GetGlobalRect() == pinned, $"scrolling leaves Start pinned at {size}");
                Check(settings.GetGlobalRect().Encloses(loyalty.GetGlobalRect()), $"last personality control reachable at {size}");
                Check(players.GetGlobalRect().Encloses(lastLeader.GetGlobalRect()), $"last leader reachable at {size}");
                Check(!players.GetGlobalRect().Intersects(settings.GetGlobalRect()), $"independent areas do not overlap at {size}");
                if (DisplayServer.GetName() != "headless")
                {
                    players.ScrollVertical = 0;
                    settings.ScrollVertical = 0;
                    await Settle();
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/ui/v0.11"));
                    System.IO.Directory.CreateDirectory(output);
                    Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, $"new-game-{size.X}x{size.Y}.png")) == Error.Ok, "saved window capture");
                }
            }
            var first = screen.HumanNationIds[0];
            screen.SetLeaderNameForCheck(first, "Layout test");
            ((HSlider)screen.FindChild("Aggression", true, false)).Value = 0.25;
            ((HSlider)screen.FindChild("Expansiondrive", true, false)).Value = 0.75;
            ((HSlider)screen.FindChild("Loyaltytoalliances", true, false)).Value = 1;
            var starts = 0;
            screen.Started += () => starts++;
            start.EmitSignal(BaseButton.SignalName.Pressed);
            Check(starts == 1 && selection.LeaderNames[first] == "Layout test", "real Start confirms the selected human and edited leader");
            Check(selection.AiPersonalityOverride == new IC2.Engine.Model.AiPersonality(0.25, 0.75, 1), "all three settings reach selection unchanged");
            screen.SetHumanForCheck(first, false);
            Check(start.Disabled, "no human disables Start");
            screen.ObserverChoice.ButtonPressed = true;
            start.EmitSignal(BaseButton.SignalName.Pressed);
            Check(starts == 2 && selection.ObserverMode && selection.HumanNationIds.Count == 0, "observer starts without human seats");
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        GD.Print($"NewGameSetupLayoutCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Settle()
    {
        for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
