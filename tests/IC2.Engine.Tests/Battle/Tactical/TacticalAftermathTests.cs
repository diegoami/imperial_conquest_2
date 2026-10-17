using System.Text.Json;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Screens;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

public sealed class TacticalAftermathTests
{
    [Theory]
    [InlineData("faithful")]
    [InlineData("escape")]
    [InlineData("pursuit")]
    [InlineData("boxed")]
    [InlineData("cap")]
    public void Captures_actual_fate_unity_and_losses_without_changing_legacy_report(string mode)
    {
        var rules = ToyRuleset;
        var improved = mode != "faithful";
        rules = rules with { Combat = rules.Combat with { Tactical = rules.Combat.Tactical with
        {
            RetreatLosses = improved, WinnerRoutedUnitsRejoin = improved,
            RetreatLossBase = mode == "pursuit" ? 1000 : 0,
            RetreatLossCap = mode == "pursuit" ? 1000 : 0, RetreatLossPursuit = 0,
        } } };
        var unit = new UnitSlot(0, "light_infantry", 1000, 5, "unit");
        var armies = new[]
        {
            TacticalSessionTestbed.Army("north-army-1", "north", 3, 2, 68, unit, unit),
            TacticalSessionTestbed.Army("south-army-1", "south", 4, 2, 59, unit),
        };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, rules, armies);
        var before = session.State with { Nations = ValueList.From(session.State.Nations.Select(n => n.Id == "north" ? n with { Unity = 980 } : n)) };
        var cap = mode == "cap";
        var battle = Arena((0, Unit(LI, 3, 3, cap ? 900 : 1000)),
            (1, Unit(LI, 4, 3, 0) with { RoutedTroops = improved ? 500 : 0 }),
            (D0, Unit(LI, 3, 4, cap ? 900 : 0) with { RoutedTroops = improved && !cap ? 700 : 0 })) with
        {
            AttackerArmyId = "north-army-1", DefenderArmyId = "south-army-1", IsOver = true, Capped = cap,
            Log = ValueList.Of<TacticalEvent>(new TacticalShotEvent(0, D0, 100, 100, 1, 900, 50),
                new TacticalRoutedEvent(1), new TacticalRoutedEvent(D0), new TacticalCascadeRemovedEvent(D0)),
        };
        var context = TacticalContext.From(rules);
        var draws = new ScriptedDraws(1, 0);
        var initial = TacticalWriteBack.Apply(before, battle, context, draws, NullEventSink.Instance);
        var world = mode == "boxed" ? session.World with { TileTypes = ValueList.From(session.World.TileTypes.Select(t => t with { PassableByArmies = false })) } : session.World;
        var result = TacticalWriteBack.CompleteSurvivors(initial, before, context, draws, world, NullEventSink.Instance);
        var report = result.Report;
        var facts = Assert.IsType<TacticalBattleAftermath>(report.Aftermath);
        var winner = result.Game.ArmyById(report.WinnerArmyId)!;
        var loser = result.Game.ArmyById(report.LoserArmyId);
        Assert.Equal(winner.TotalTroops, facts.Winner.SurvivingTroops);
        Assert.Equal(loser is null, facts.Loser.ArmyRemoved);
        Assert.Equal(loser?.TotalTroops ?? 0, facts.Loser.SurvivingTroops);
        Assert.Equal(result.Game.NationById(report.WinnerNationId)!.Unity, facts.Winner.UnityAfter);
        Assert.Equal(before.NationById(report.WinnerNationId)!.Unity, facts.Winner.UnityBefore);
        Assert.Equal(cap ? 0 : 10, facts.Winner.UnityAfter - facts.Winner.UnityBefore);
        Assert.Equal(100, cap ? facts.Winner.ExchangeCasualties : facts.Loser.ExchangeCasualties);
        Assert.Equal(1, facts.Loser.RoutedUnits);
        Assert.Equal(0, facts.Loser.CascadeUnits); // repeated exit never counts a unit twice
        Assert.Equal(improved && !cap ? 500 : 0, facts.Winner.RejoinedTroops);
        Assert.Equal(mode == "boxed", facts.ScatterFailed);
        Assert.Equal(mode == "pursuit" ? 700 : 0, facts.PursuitLosses);
        Assert.Equal(mode is "escape" or "cap", loser is not null);
        Assert.Equal(JsonSerializer.Serialize(report with { Aftermath = null }), JsonSerializer.Serialize(report));
        var model = BattleEndedViewModel.FromReport(report, result.Game, rules);
        Assert.Contains(model.AftermathLines, line => line.Contains("100 exchange casualties"));
        Assert.Contains(model.AftermathLines, line => line.Contains("routing does not mean every troop was killed"));
        Assert.Contains(model.AftermathLines, line => line.Contains(loser is null ? "army removed" : "scattered to safety"));
        Assert.DoesNotContain(model.AftermathLines, line => line.Contains("all routed troops died"));
        Assert.Equal(cap ? new[] { 3 } : mode is "faithful" or "pursuit" ? new[] { 4 } : new[] { 4, 3 }, draws.Bounds);
    }
}
