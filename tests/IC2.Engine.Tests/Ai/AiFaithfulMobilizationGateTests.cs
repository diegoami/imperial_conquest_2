using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using M = IC2.Engine.Tests.Recruitment.MobilizationFixture;

namespace IC2.Engine.Tests.Ai;

/// <summary>T171: pinned research 0b0b55e case D and FUN_004504f4/0044a610.</summary>
public sealed class AiFaithfulMobilizationGateTests
{
    private const string Me = "north";
    private const string Foe = "south";
    private static World World => M.OpenWorld(30, 30);
    private static Ruleset Rules => BattleCommandTestbed.ToyRuleset with
    {
        Flags = BattleCommandTestbed.ToyRuleset.Flags with { AiEconomy = AiEconomyModel.Original },
        Ai = BattleCommandTestbed.ToyRuleset.Ai with { MobilizeReadyRecruitBaseScore = int.MaxValue },
    };

    private static RecruitmentSlot Slot(int troops = 1000, int state = 24, string city = "home") =>
        new(city, "light_infantry", troops, state);

    private static GameState State(IEnumerable<RecruitmentSlot> slots, int? distance = 5,
        int ownTroops = 1000, bool war = true, bool foreignArmy = false)
    {
        var nation = F.Nation(Me, treasury: 0, capitalCityId: "home") with
        {
            Control = SeatControl.Ai, MobilizedPercent = 100,
            RecruitmentSlots = ValueList.From(slots),
        };
        var cities = new[]
        {
            F.City("home", "Home", 5, 5, Me, Me, 80, 100, 10, 100, 10),
            F.City("away", "Away", 20, 5, Me, Me, 80, 100, 10, 100, 10),
            F.City("third", "Third", 5, 20, Me, Me, 80, 100, 10, 100, 10),
            F.City("fourth", "Fourth", 20, 20, Me, Me, 80, 100, 10, 100, 10),
            F.City("enemy", "Enemy", 28, 28, Foe, Foe, 80, 100, 10, 100, 10),
        };
        var armies = distance is { } d
            ? new[] { F.Army("existing", foreignArmy ? Foe : Me, 5 + d, 5 + d, 60,
                F.Unit("light_infantry", ownTroops)) with { Moves = 0 } }
            : Array.Empty<ArmyState>();
        var state = BattleCommandTestbed.StateWith(
            new[] { nation, F.Nation(Foe, capitalCityId: "enemy") }, cities, armies);
        state = state with { TurnOrder = ValueList.Of(Me, Foe), ActiveSeatIndex = 0 };
        return war ? state with { Relations = state.Relations.WithRelation(Me, Foe, Rules.Diplomacy.StateCodes.War) } : state;
    }

    private static List<AiCandidate> Candidates(GameState state, Ruleset? rules = null)
    {
        var r = rules ?? Rules;
        var into = new List<AiCandidate>();
        AiEconomyPhase.Propose(new AiView(state, r, World, Me),
            AiPersonalityProfile.For(state.NationById(Me)!, r), into);
        return into.Where(c => c.Kind == "mobilize").ToList();
    }

    private static (AiTurnOutcome Outcome, RecruitMobilized[] Mobilized) Turn(GameState state, Ruleset? rules = null)
    {
        var r = rules ?? Rules;
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), r, World, sink);
        var outcome = AiTurn.Run(state, r, World, dispatcher, SplitMix64Rng.ForStream(12345, "T171"), sink);
        Assert.Equal(0, outcome.CommandsRejected);
        return (outcome, sink.Events.OfType<RecruitMobilized>().ToArray());
    }

    [Theory]
    [InlineData(74900, true)]
    [InlineData(75000, false)]
    [InlineData(75100, false)]
    public void Strict_threat_gate_cannot_be_bypassed_by_maximum_score(int troops, bool allowed)
    {
        // One war contributes 15000; LI contributes (troops/100)*20.
        var state = State(new[] { Slot() }, ownTroops: troops);
        var own = AiFaithfulEconomy.Own(state, Rules, Me);
        var threat = AiFaithfulEconomy.Threat(state, Rules, Me);
        Assert.Equal(15000, threat);
        Assert.Equal(allowed, own < threat);
        Assert.Equal(allowed ? 1 : 0, Candidates(state).Count);
        var driven = Turn(state);
        Assert.Equal(allowed ? 1 : 0, driven.Mobilized.Length);
        Assert.Equal(allowed ? 0 : 1, driven.Outcome.State.NationById(Me)!.RecruitmentSlots.Count);
    }

    [Theory]
    [InlineData(5, 1, false, true)]
    [InlineData(6, 1, false, false)]
    [InlineData(6, 3, false, false)]
    [InlineData(6, 4, false, true)]
    [InlineData(null, 3, false, false)]
    [InlineData(null, 4, false, true)]
    [InlineData(5, 1, true, false)]
    public void Proximity_and_ready_count_boundaries(int? distance, int count, bool foreign, bool allowed)
    {
        var state = State(Enumerable.Range(0, count).Select(_ => Slot()), distance, foreignArmy: foreign);
        Assert.Equal(allowed ? count : 0, Candidates(state).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(22)]
    [InlineData(25)]
    public void Only_exact_state_24_counts_and_mobilizes(int stateCode)
    {
        var state = State(new[] { Slot(), Slot(), Slot(), Slot(state: stateCode) }, distance: null);
        Assert.Empty(Candidates(state));
        Assert.Empty(Candidates(State(new[] { Slot(state: stateCode) })));
    }

    [Fact]
    public void Zero_troop_entries_do_not_open_the_four_ready_gate()
    {
        Assert.Empty(Candidates(State(new[] { Slot(), Slot(), Slot(), Slot(0) }, distance: null)));
    }

    [Fact]
    public void Peaceful_and_distant_controls_stay_queued()
    {
        foreach (var state in new[] { State(new[] { Slot() }, war: false), State(new[] { Slot() }, distance: 8) })
        {
            var driven = Turn(state);
            Assert.Empty(driven.Mobilized);
            Assert.Equal(state.NationById(Me)!.RecruitmentSlots, driven.Outcome.State.NationById(Me)!.RecruitmentSlots);
        }
    }

    [Fact]
    public void Near_positive_joins_and_four_ready_positive_creates_through_shared_command()
    {
        var joined = Turn(State(new[] { Slot() }, distance: 4));
        Assert.Equal("existing", Assert.Single(joined.Mobilized).ArmyId);
        Assert.False(joined.Mobilized[0].ArmyWasCreated);
        var created = Turn(State(new[] { Slot(), Slot(), Slot(), Slot() }, distance: null));
        Assert.Equal(2, created.Mobilized.Length);
        Assert.True(created.Mobilized[0].ArmyWasCreated);
        Assert.False(created.Mobilized[1].ArmyWasCreated);
    }

    [Fact]
    public void Initial_whole_queue_latch_survives_removal_and_compacted_scan_skips_original_slots_1_and_3()
    {
        // Four geographically separated cities prevent a newly created army from opening a new gate.
        // FUN_0044a610 compacts; FUN_004504f4 nevertheless advances the table index.
        var slots = new[] { Slot(1000), Slot(2000, city: "away"),
            Slot(3000, city: "third"), Slot(4000, city: "fourth") };
        var state = State(slots, distance: null);
        var driven = Turn(state);
        Assert.Equal(new[] { 1000, 3000 }, driven.Mobilized.Select(e => e.Troops));
        Assert.Equal(new[] { slots[1], slots[3] }, driven.Outcome.State.NationById(Me)!.RecruitmentSlots);
        Assert.Equal(2, driven.Mobilized.Length); // deficit 15000 greatly exceeds mobilized value 800
        Assert.All(driven.Mobilized, e => Assert.True(e.ArmyWasCreated));
        var again = Turn(state);
        Assert.Equal(driven.Outcome.State, again.Outcome.State);
        Assert.Equal(driven.Outcome.Log, again.Outcome.Log);
        Assert.Equal(driven.Outcome.CommandsIssued, again.Outcome.CommandsIssued);
        Assert.Equal(driven.Mobilized, again.Mobilized);
    }

    [Fact]
    public void One_near_slot_opens_gate_for_distant_slots_in_the_same_queue()
    {
        var state = State(new[] { Slot(city: "away"), Slot(), Slot(3000, city: "third") });
        Assert.Equal(3, Candidates(state).Count);
        var driven = Turn(state);
        Assert.Equal(new[] { "away", "third" }, driven.Mobilized.Select(e => e.CityId));
    }

    [Fact]
    public void Initial_deficit_stops_pass_after_overshoot_and_later_candidate_passes_do_not_repeat_it()
    {
        // Initial own=14000, threat=15000. Original slots0&2 each add600, covering the deficit.
        var slots = new[] { Slot(3000), Slot(1000), Slot(3000), Slot(1000), Slot(1000) };
        var state = State(slots, ownTroops: 70000);
        var driven = Turn(state);
        Assert.Equal(new[] { 3000, 3000 }, driven.Mobilized.Select(e => e.Troops));
        Assert.Equal(3, driven.Outcome.State.NationById(Me)!.RecruitmentSlots.Count);
        Assert.Contains(driven.Outcome.Log, l => l == "faithful economy: threat 15000, own 14000");
    }

    [Fact]
    public void Human_command_and_improved_candidates_keep_their_prior_outcomes()
    {
        var state = State(new[] { Slot(state: 16) }, distance: null, war: false);
        var human = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == Me ? n with { Control = SeatControl.Human } : n)) };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Rules, World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(human, new MobilizeRecruitSlotCommand(Me, 0, "human-new"));
        Assert.True(result.IsAccepted, result.Rejection?.ToString());
        Assert.Empty(result.State.NationById(Me)!.RecruitmentSlots);
        var improved = Rules with { Flags = Rules.Flags with { AiEconomy = AiEconomyModel.Designed, SeatAsymmetry = SeatAsymmetryModel.Normalized } };
        Assert.Single(Candidates(state, improved));
        var driven = Turn(state, improved);
        Assert.Single(driven.Mobilized);
        Assert.Empty(driven.Outcome.State.NationById(Me)!.RecruitmentSlots);
    }
}





