using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Screens;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// bug #499, Done-when 1: the battle-result screen's own text, driven by a <em>real</em> failed siege
/// rather than a hand-built <see cref="BattleResult"/> — Rome's army-0 (23,700 troops, morale 70, so
/// 20,720 attack) beside Gaul's Felsina (loyalty 79, fortification 68%, population 26k, so 34,050
/// defence), the battle the user played. The before-fix screen printed only "Gaul's casualties: 0",
/// never the besieging army's own attrition (1,872 troops), never the city's before/after erosion, and
/// never a word about how far short the attack fell. This is a view fix: the assertions below read
/// <see cref="BattleResultViewModel"/>'s own text methods, the exact strings the screen renders.
/// </summary>
public sealed class BattleResultSiegeTextTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>
    /// Every figure in this test comes from the one real <c>BattleResolved</c> the engine produced; the
    /// bug marks "LoserCasualties holds the attacker's siege attrition" as unverified, so it is pinned
    /// both to the per-slot report and to the army's actual troop count — the attacker lost 1,872
    /// troops, the winning city side lost 0.
    /// </summary>
    [Fact]
    public void AFailedSiegeShowsBothSidesTheCitysErosionAndTheShortfall()
    {
        var (battle, stateAfter, troopsBefore) = ResolveSiege();

        Assert.Equal(BattleKind.Siege, battle.Kind);
        Assert.Equal(BattleSide.Defender, battle.Winner);
        Assert.False(battle.AttackerWon);
        Assert.Equal("felsina", battle.WinnerId);
        Assert.Equal("army-0", battle.LoserId);
        Assert.Equal(20_720, battle.AttackerPower);
        Assert.Equal(34_050, battle.DefenderPower);

        // The bug's unverified claim, established from this real battle: the failed siege's attacker
        // attrition is LoserCasualties, and the army's own troop count fell by exactly that.
        Assert.Equal(0, battle.WinnerCasualties);
        Assert.Equal(1_872, battle.LoserCasualties);
        Assert.Equal(battle.UnitCasualties.Sum(c => c.TroopsLost), battle.LoserCasualties);
        Assert.Equal(
            troopsBefore - battle.LoserCasualties,
            stateAfter.ArmyById("army-0")!.Units.Sum(u => u.Troops));

        // This attempt's own erosion of the city, as the result carries it.
        Assert.Equal(79, battle.CityLoyaltyBefore);
        Assert.Equal(76, battle.CityLoyaltyAfter);
        Assert.Equal(68, battle.CityFortificationPercentBefore);
        Assert.Equal(65, battle.CityFortificationPercentAfter);
        Assert.Equal(26, battle.CityPopulationThousandsBefore);
        Assert.Equal(25, battle.CityPopulationThousandsAfter);

        // The screen's text for that result, through the same view model the screen reads.
        var viewModel = BattleResultViewModel.FromResult(battle);
        var winnerName = stateAfter.NationById(battle.WinnerNationId)!.Name;
        var loserName = stateAfter.NationById(battle.LoserNationId)!.Name;
        var cityName = stateAfter.CityById("felsina")!.Name;

        var winnerLine = viewModel.WinnerCasualtiesText(winnerName);
        var loserLine = viewModel.LoserCasualtiesText(loserName);
        var cityLine = viewModel.SiegeCityText(cityName);
        var shortfallLine = viewModel.FailedSiegeShortfallText();

        // Both sides' casualties: the winner's "0" is not the only casualty figure shown.
        Assert.Equal("Gaul's casualties: 0", winnerLine);
        Assert.Equal("Rome's casualties: 1872", loserLine);

        // The city's loyalty, fortification and population, before and after the attempt.
        Assert.Equal("Felsina: loyalty 79 -> 76, fortification 68% -> 65%, population 26k -> 25k", cityLine);

        // The attack fell short, with the two powers the screen already shows.
        Assert.Equal("The attack fell short by 13330: 20720 against 34050.", shortfallLine);
    }

    /// <summary>
    /// The guard on the other side: a siege the attacker <em>wins</em> still shows the city's erosion,
    /// but is not called "fell short". The army is quadrupled (94,800 troops, under the confirmed
    /// 100,000 cap) so the same 34,050 defence loses, and the city is captured through the real command.
    /// </summary>
    [Fact]
    public void AWonSiegeShowsTheCitysErosionButNoShortfall()
    {
        var (battle, stateAfter, _) = ResolveSiege(army => army with
        {
            Units = ValueList.From(army.Units.Select(u => u with { Troops = u.Troops * 4 })),
        });

        Assert.True(battle.AttackerWon);
        Assert.True(battle.WinnerCasualties > 0);
        Assert.Equal(0, battle.LoserCasualties);

        var viewModel = BattleResultViewModel.FromResult(battle);
        Assert.NotNull(viewModel.SiegeCityText(stateAfter.CityById("felsina")!.Name));
        Assert.Null(viewModel.FailedSiegeShortfallText());
    }

    /// <summary>
    /// Dispatches a real <see cref="BesiegeCityCommand"/> for Rome's army-0 beside Felsina through the
    /// engine's own dispatcher — the same path a seat uses — and returns the <see cref="BattleResult"/>
    /// the resulting <c>BattleResolved</c> event carried, the state after it, and the army's troop count
    /// before the attempt.
    /// </summary>
    /// <param name="reshape">
    /// An optional last-minute change to army-0's own record (the won-siege test quadruples its troops);
    /// the default is the shipped army.
    /// </param>
    private static (BattleResult Battle, GameState StateAfter, int TroopsBefore) ResolveSiege(
        Func<ArmyState, ArmyState>? reshape = null)
    {
        var resolved = Repository.Resolve("classical-mediterranean");
        var initial = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);

        // army-0's shipped (100,37) is six tiles from Felsina (98,31); the user's own play put it beside
        // the city, so the fixture repositions the world before the battle — the same "reposition, then
        // play" seam godot/Screens/Checks/ScreensCheck.cs's BattleReadySession already uses.
        var army = (reshape ?? (a => a))(initial.ArmyById("army-0")!) with { X = 98, Y = 32 };

        var state = initial with
        {
            Armies = ValueList.From(initial.Armies.Select(a => a.Id == "army-0" ? army : a)),
        };

        var sink = new RecordingEventSink();
        var dispatched = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), resolved.Ruleset, resolved.World, sink)
            .Dispatch(state, new BesiegeCityCommand("rome", army.Id, "felsina"));

        Assert.True(dispatched.IsAccepted, dispatched.ToString());
        var battle = Assert.Single(sink.Events.OfType<BattleResolved>()).Result;
        return (battle, dispatched.State, army.Units.Sum(u => u.Troops));
    }
}
