using System.Text.Json;
using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Strength;
using IC2.Engine.Tests.Model;
using IC2.Slice.UI;
using IC2.Slice.Screens;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Barbarians;

public sealed class ItalyBarbarianCampaignTests(ITestOutputHelper output)
{
    private const string Id = "italy-500bc-barbarians";

    [Fact]
    public void Variant_reuses_Italy_data_and_rules_without_changing_old_campaign_or_human_choices()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var original = repository.Resolve("italy-500bc");
        var variant = repository.Resolve(Id);
        Assert.Equal(original.World.Cities, variant.World.Cities);
        Assert.Equal(original.World.StartingArmies, variant.World.StartingArmies);
        Assert.Equal(original.World.StartingFleets, variant.World.StartingFleets);
        Assert.Equal(original.World.Terrain, variant.World.Terrain);
        Assert.Equal(original.World.Nations, new ValueList<NationDefinition>(variant.World.Nations.Where(n => n.Id != "barbarians")));
        Assert.Same(original.Ruleset, variant.Ruleset);
        Assert.True(original.Scenario.Listed);
        Assert.Null(original.Scenario.Barbarians);
        Assert.True(variant.Scenario.Listed);
        Assert.Equal(13, variant.World.Nations.Count);
        var choices = GameSessionFactory.SeatOptionsFor(variant);
        Assert.Equal(12, choices.Count);
        Assert.DoesNotContain(choices, choice => choice.NationId == "barbarians");
        var card = RulesetPresets.CardsFor(repository).Single(c => c.ScenarioId == Id);
        var session = GameSessionFactory.CreateSession(repository, new NewGameSelection { SelectedCard = card, HumanNationId = "rome" });
        Assert.Equal(SeatControl.Ai, session.State.NationById("barbarians")!.Control);
        Assert.False(session.State.NationById("barbarians")!.Eliminated);
        var hostile = DiplomacyGridViewModel.BuildRows(session.State, variant.Ruleset, "rome").Single(row => row.NationId == "barbarians");
        Assert.Equal("ALWAYS HOSTILE", hostile.StatusCaption);
        Assert.False(hostile.CanDeclareWar || hostile.CanMakePeace || hostile.CanProposeAlliance || hostile.CanProposeTrade);
        var state = session.State;
        var save = new SaveGame(GameDataSchema.CurrentVersion, "test", "test", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var restored = JsonSerializer.Deserialize<SaveGame>(GameJson.Serialize(save), GameJson.Options)!;
        GameDataValidation.Validate("test", restored);
        var resumed = new GameSession(variant.World, variant.Ruleset, variant.Scenario, restored);
        Assert.Equal(state, resumed.State);
        Assert.True(resumed.State.NationById("barbarians")!.IsBarbarian);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void Seeded_Italy_stress_uses_real_seasonal_arrivals_and_normal_orders_without_invalid_forces(ulong seed)
    {
        var variant = GameDataRepository.Load(TestPaths.DataRoot).Resolve(Id);
        // Stress the production callback every season; this is not a claim about the shipped 25% balance.
        var scenario = variant.Scenario with { Barbarians = variant.Scenario.Barbarians! with { ChancePercent = 100 } };
        var result = AiGameRunner.Run(variant.World, variant.Ruleset, scenario, seed, turnCap: 400);
        output.WriteLine(result.Summary());
        output.WriteLine($"Barbarian towns held at checkpoint: {result.FinalState.CountCitiesOwnedBy("barbarians")}");
        Assert.Equal(0, result.CommandsRejected);
        Assert.Equal(0, result.ProjectionMismatches);
        Assert.Equal(0, result.FullRoundStalls);
        var state = result.FinalState;
        var faction = state.NationById("barbarians")!;
        Assert.True(faction.IsBarbarian);
        Assert.False(faction.Eliminated);
        Assert.NotNull(faction.LastBarbarianArrivalTurn);
        Assert.NotEqual("barbarians", result.WinningNationId);
        Assert.InRange(state.Armies.Count(a => a.Nation == faction.Id), 0, 4);
        GameDataValidation.Validate("stress", state);
        foreach (var army in state.Armies.Where(a => a.Nation == faction.Id))
        {
            Assert.InRange(army.X, 0, variant.World.Width - 1);
            Assert.InRange(army.Y, 0, variant.World.Height - 1);
            if (army.AboardFleetId is { } carrier)
            {
                var fleet = state.FleetById(carrier)!;
                Assert.Equal(army.Id, fleet.CarriedArmyId);
                Assert.Equal((army.X, army.Y), (fleet.X, fleet.Y));
            }
            else Assert.DoesNotContain(state.Cities, c => c.X == army.X && c.Y == army.Y);
        }
        if (seed == 1)
        {
            var repeat = AiGameRunner.Run(variant.World, variant.Ruleset, scenario, seed, turnCap: 400);
            Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(repeat.FinalState));
            Assert.Equal(result.Transcript, repeat.Transcript);
        }
    }

    [Fact]
    public void Provisional_arrival_force_can_threaten_normal_Italy_towns_using_existing_siege_strength()
    {
        var variant = GameDataRepository.Load(TestPaths.DataRoot).Resolve(Id);
        var state = GameStateFactory.CreateInitial(variant.World, variant.Ruleset, variant.Scenario);
        var config = variant.Scenario.Barbarians!;
        var strength = SiegeStrength.Attacker(config.ArrivalUnits, config.ArmyMorale, variant.Ruleset, variant.Ruleset.ArcherUnitTypeId);
        var threatened = state.Cities.Count(c => strength > AiArmyTargetTree.CityDefense(c, variant.Ruleset, state));
        output.WriteLine($"Arrival siege strength {strength}; {threatened}/{state.Cities.Count} starting towns have lower defence before an army intervenes.");
        Assert.True(threatened > 0, "The provisional arrival must be capable of threatening a normal starting town without changing combat rules.");
    }
}
