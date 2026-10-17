using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Synthetic write-back facts rendered by the production result screen, without private files.</summary>
public partial class TacticalAftermathCheck : Node
{
    private sealed class Draws : IBattleDraws { public int Random(int bound) => 0; }

    public override async void _Ready()
    {
        var ok = true;
        try
        {
            var capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture=", StringComparison.Ordinal))?.Split('=')[1];
            foreach (var mode in capture is null ? new[] { "faithful", "improved", "cap" } : new[] { capture })
            {
                var toy = GameDataContext.Repository.Resolve("toy-3city");
                var improved = mode != "faithful";
                var cap = mode == "cap";
                var rules = toy.Ruleset with { Combat = toy.Ruleset.Combat with { Tactical = toy.Ruleset.Combat.Tactical with
                {
                    RetreatLosses = improved, WinnerRoutedUnitsRejoin = improved,
                    RetreatLossBase = 0, RetreatLossCap = 0, RetreatLossPursuit = 0,
                } } };
                var session = new GameSession(toy.World, rules, toy.Scenario);
                var before = session.State with
                {
                    Armies = ValueList.From(session.State.Armies.Select(a => a.Id is "north-army-1" or "south-army-1"
                        ? a with { Units = ValueList.Of(new UnitSlot(0, "light_infantry", a.Nation == "north" ? 1000 : 800, 5, "Infantry")) }
                        : a)),
                };
                var context = TacticalContext.From(rules);
                var slots = Enumerable.Repeat(TacticalSlot.Empty, 2 * context.SlotsPerSide).ToArray();
                slots[0] = new(3, 3, 0, 0, 900, 5, 70, 0, 0, -1, "Infantry");
                slots[context.SlotsPerSide] = new(4, 3, 0, 0, cap ? 700 : 0, 5, 0, 0, 0, -1, "Infantry", improved && !cap ? 700 : 0);
                var battle = new TacticalBattleState("north-army-1", "south-army-1", 0, true, 4, true,
                    ValueList.From(slots), ValueList.From(new int[context.Rules.BoardWidth * context.Rules.BoardHeight]),
                    ValueList.Of(false, false), ValueList.Of(false, false), ValueList.From(Enumerable.Repeat(-1, slots.Length)), -1,
                    ValueList.From(new TacticalEvent[]
                    {
                        new TacticalShotEvent(context.SlotsPerSide, 0, 100, 100, 1, 900, 70),
                        new TacticalShotEvent(0, context.SlotsPerSide, 100, 100, 1, 700, 0),
                    }.Concat(cap ? Array.Empty<TacticalEvent>() : new TacticalEvent[] { new TacticalRoutedEvent(context.SlotsPerSide) })), cap);
                var initial = TacticalWriteBack.Apply(before, battle, context, new Draws(), NullEventSink.Instance);
                var result = TacticalWriteBack.CompleteSurvivors(initial, before, context, new Draws(), toy.World, NullEventSink.Instance);
                var model = BattleEndedViewModel.FromReport(result.Report, result.Game, rules);
                var screen = new BattleEndedScreen { Model = model };
                screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                AddChild(screen);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var valid = model.AftermathLines.Any(l => l.Contains("100 exchange casualties"))
                    && model.AftermathLines.Any(l => l.Contains("unity:"))
                    && model.AftermathLines.Any(l => l.Contains(improved ? "scattered to safety" : "army removed"))
                    && screen.WinnerLabel.Text == model.WinnerLine;
                GD.Print($"{(valid ? "PASS" : "FAIL")}: {mode} actual aftermath on production result screen");
                ok &= valid;
                if (capture is not null)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var path = System.IO.Path.Combine(GameDataContext.RepositoryRoot, "rendered", $"T181-{mode}.png");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    var error = GetViewport().GetTexture().GetImage().SavePng(path);
                    if (error != Error.Ok) throw new InvalidOperationException($"Capture failed: {error}");
                    GD.Print($"TacticalAftermathCheck: capture {path}");
                }
                RemoveChild(screen);
                screen.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"FAIL: TacticalAftermathCheck {ex}"); ok = false; }
        GD.Print($"TacticalAftermathCheck: exiting with code {(ok ? 0 : 1)}.");
        GetTree().Quit(ok ? 0 : 1);
    }
}
