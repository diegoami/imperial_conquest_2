using Godot;
using IC2.Engine.Serialization;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T270/#1707: actual shipped cards remain reachable and the barbarian role cannot be selected.</summary>
public partial class BarbarianCampaignCheck : Node
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                GetTree().Root.Size = size;
                GetTree().Root.ContentScaleSize = size;
                var chooser = new RulesetChooserScreen();
                chooser.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                AddChild(chooser);
                await Frames(8);
                var buttons = chooser.CardButtons.Values.Concat(chooser.ScenarioCardButtons.Values).ToArray();
                Check(buttons.Length >= 4, "all shipped preset and campaign cards exist");
                Check(buttons.Max(b => b.Position.Y) > buttons.Min(b => b.Position.Y), $"cards wrap at {size}");
                var scroll = Descendants<ScrollContainer>(chooser).FirstOrDefault();
                Check(scroll is not null, "campaign rows have a scroll container");
                foreach (var button in buttons)
                {
                    var rect = button.GetGlobalRect();
                    Check(rect.Position.X >= -1 && rect.End.X <= size.X + 1, $"card fits window horizontally at {size}");
                    Check(Descendants<Label>(button).All(label => label.GetGlobalRect().End.Y <= rect.End.Y + 1),
                        $"card text stays inside its button at {size}");
                    if (scroll is null) continue;
                    scroll.EnsureControlVisible(button);
                    await Frames(3);
                    rect = button.GetGlobalRect();
                    var visible = scroll.GetGlobalRect();
                    Check(rect.Position.Y >= visible.Position.Y - 1 && rect.End.Y <= visible.End.Y + 1,
                        $"each card can be fully reached by scrolling at {size}");
                }
                var card = RulesetPresets.CardsFor(GameDataContext.Repository).Single(c => c.ScenarioId == "italy-500bc-barbarians");
                RulesetCard? confirmed = null;
                chooser.Continued += selected => confirmed = selected;
                chooser.SelectCard(card);
                chooser.ConfirmSelection();
                Check(confirmed == card, "Continue confirms the selected barbarian-enabled Italy card");
                chooser.QueueFree();
                await Frames(3);
            }

            var selectedCard = RulesetPresets.CardsFor(GameDataContext.Repository).Single(c => c.ScenarioId == "italy-500bc-barbarians");
            var selection = new NewGameSelection { SelectedCard = selectedCard };
            var seats = new ScenarioSeatScreen { Selection = selection };
            seats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(seats);
            await Frames(8);
            var choices = Descendants<CheckBox>(seats).Where(b => b.Name.ToString().StartsWith("Human_", StringComparison.Ordinal)).ToArray();
            Check(choices.Length == 12, "all twelve regular Italy nations remain selectable");
            Check(choices.All(b => b.Name != "Human_barbarians"), "the AI-only barbarian faction has no human checkbox");
            Check(seats.HumanNationIds.SequenceEqual(new[] { "rome" }), "Rome remains the default human seat");
            var started = false;
            seats.Started += () => started = true;
            seats.ConfirmSeatAndStart();
            Check(started && selection.HumanNationId == "rome", "the ordinary Start Game flow retains the selected human nation");
            seats.QueueFree();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"BarbarianCampaignCheck: {ex}");
            _ok = false;
        }
        GD.Print($"BarbarianCampaignCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void Check(bool condition, string message)
    {
        GD.Print($"BarbarianCampaignCheck: {(condition ? "PASS" : "FAIL")} {message}");
        _ok &= condition;
    }
}
