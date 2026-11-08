using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class IndependentPanelsCheck : Control
{
    private bool _ok = true;
    private static readonly string[] Ids = { "Overview", "Object", "News", "Log" };

    public override async void _Ready()
    {
        try
        {
            Theme = AppTheme.Current;
            new SidePanelToggle().Set(true);
            foreach (var scheme in ColorSchemes.All)
            {
                Appearance.ApplyColors(scheme, this);
                var data = GameDataContext.Repository.Resolve("classical-mediterranean");
                var session = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "rome");
                var game = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
                game.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(game);
                game.SubmitForCheck("invalid-panel-layout-check");
                var state = session.State;
                var commands = game.CommandTable.IssuedCount;
                foreach (var size in new[] { new Vector2I(1500, 850), new Vector2I(1000, 650) })
                {
                    Size = size;
                    if (DisplayServer.GetName() != "headless")
                    {
                        GetWindow().ContentScaleSize = size;
                        GetWindow().Size = size == new Vector2I(1000, 650) ? new Vector2I(1500, 975) : size;
                    }
                    await Settle();
                    var mapWithoutPanels = 0f;
                    foreach (var button in game.Toolbar.Buttons.Where(button => button.Icon is not null))
                        Check(button.Size.X <= 32.5f && button.Size.Y <= 32.5f
                            && button.AccessibilityName.Length > 0, $"compact main icon {button.Name} retains accessible 32px target: {button.Size}");
                    for (var mask = 0; mask < 16; mask++)
                    {
                        for (var i = 0; i < Ids.Length; i++) game.Panels.SetOpen(Ids[i], (mask & (1 << i)) != 0);
                        await Settle();
                        var bounds = new Rect2(GlobalPosition, Size);
                        Check(game.SideColumn.Visible == (mask != 0), $"{scheme.Id}/{size}/{mask}: empty column releases space");
                        Check(bounds.Encloses(game.Panels.Toggles.GetGlobalRect()), "reopen controls remain inside screen");
                        if (mask == 0) mapWithoutPanels = game.MapView.Size.X;
                        else Check(game.MapView.Size.X < mapWithoutPanels, "visible panels consume only their column");
                        foreach (var id in Ids)
                        {
                            var frame = game.Panels.Frame(id);
                            Check(!frame.Visible || (bounds.Encloses(frame.GetGlobalRect()) && frame.Size.Y > 28), id + " fits when open");
                            Check(game.Panels.Toggle(id).ButtonPressed == frame.Visible, id + " toggle reflects panel choice");
                        }
                    }
                    game.Panels.SetOpen("Overview", false);
                    game.SidePanelButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await Settle();
                    game.NewsLog.ShowLatest();
                    Check(!game.SideColumn.Visible, "arriving news respects global hide");
                    game.SidePanelButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await Settle();
                    Check(game.SideColumn.Visible && !game.Panels.IsOpen("Overview"), "global reopen preserves independent choices");
                    game.Panels.SetOpen("Object", false);
                    var city = session.State.Cities.First().Id;
                    game.ContextPanel.ShowCity(city);
                    game.Panels.SetOpen("Object", true);
                    Check(game.ContextPanel.SubjectForCheck == "City:" + city, "selection updates while hidden");
                    game.Panels.SetOpen("Object", false);
                    var army = session.State.Armies.First();
                    game.ContextPanel.ShowArmy(army.Id);
                    game.Panels.SetOpen("Object", true);
                    Check(game.ContextPanel.SubjectForCheck == "Army:" + army.Id, "army inspection updates while hidden");
                    if (session.State.Fleets.FirstOrDefault() is { } fleet)
                    {
                        game.Panels.SetOpen("Object", false);
                        game.ContextPanel.ShowFleet(fleet.Id);
                        game.Panels.SetOpen("Object", true);
                        Check(game.ContextPanel.SubjectForCheck == "Fleet:" + fleet.Id, "fleet inspection updates while hidden");
                    }
                    game.Panels.SetOpen("News", false);
                    Check(game.NewsLog.DismissedByPlayer, "hiding News uses existing dismissal policy");
                    game.Panels.SetOpen("News", true);
                    Check(game.NewsLog.Visible && !game.NewsLog.DismissedByPlayer, "News reopens through existing formatter");
                    await Settle();
                    if (DisplayServer.GetName() != "headless")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/ui/v0.11-panels"));
                        System.IO.Directory.CreateDirectory(output);
                        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, $"{scheme.Id}-{size.X}.png")) == Error.Ok, "saved panel capture");
                        game.Panels.SetOpen("Overview", true);
                        await Settle();
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, $"{scheme.Id}-{size.X}-all.png")) == Error.Ok, "saved all-panel capture");
                    }
                }
                Check(ReferenceEquals(state, session.State) && commands == game.CommandTable.IssuedCount, "panel actions issue no command and preserve campaign/RNG state");
                RemoveChild(game); game.QueueFree();
                await Settle();
            }
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        new SidePanelToggle().Set(true);
        GD.Print($"IndependentPanelsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Settle() { for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string message) { if (!condition) GD.PrintErr("FAIL: " + message); _ok &= condition; }
}
