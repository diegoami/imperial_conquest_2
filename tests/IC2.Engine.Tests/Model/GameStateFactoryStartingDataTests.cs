using System.Globalization;
using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T75 Done-when 1 and 4: a world with no starting data still opens at uniform peace with an empty
/// log, and the shipped classical world opens with the DAT's own relations and 27-line news seed --
/// which then actually drives a round's own news header and the attack-legality gate. Also Done-when
/// 2's ruleset-dependent half: <see cref="GameStateFactory"/> rejects a relation value outside the
/// ruleset's codes/cooldown range and a news text over the ruleset's message length in bytes or
/// holding a non-printable byte -- the checks <see cref="WorldStartingDataValidationTests"/> cannot
/// run, because <see cref="GameDataValidation.ValidateWorld"/> has no ruleset.
/// </summary>
public class GameStateFactoryStartingDataTests
{
    private static ResolvedScenario Toy() => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city");

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    /// <summary>
    /// Done-when 1: the shipped toy world carries neither field, and the state it opens is exactly the
    /// uniform-peace, empty-log shape <see cref="GameStateFactory"/> always produced before this task --
    /// the existing suite built on the toy world is this claim's own proof (it is unmodified), and this
    /// test states it directly for the one field this task actually touches.
    /// </summary>
    [Fact]
    public void A_world_with_no_starting_data_opens_at_uniform_peace_with_an_empty_log()
    {
        var toy = Toy();
        Assert.Null(toy.World.StartingRelations);
        Assert.Null(toy.World.StartingNews);

        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);

        Assert.Equal(NewsLog.Empty, state.NewsLog);
        var peace = toy.Ruleset.Diplomacy.StateCodes.Peace;
        foreach (var row in state.Relations.Matrix)
        {
            foreach (var value in row)
            {
                Assert.Equal(peace, value);
            }
        }
    }

    /// <summary>
    /// Done-when 4: the classical world's DAT relations and 27-line log reach <see cref="GameState"/>
    /// unchanged -- Rome-Gaul at war and Rome-Macedonia trading (the same pair
    /// <c>ExportedWorldTests</c> checks on the committed file), and slots 0 and 26 by their exact text.
    /// </summary>
    [Fact]
    public void The_classical_world_opens_with_the_DATs_relations_and_news_log()
    {
        var classical = Classical();
        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);

        Assert.Equal(27, state.NewsLog.Slots.Count);
        Assert.Equal(26, state.NewsLog.MostRecentSlot);
        Assert.Equal("272 BC", state.NewsLog.Slots[0].Text);
        Assert.Equal("Week 1      Spring      270 BC", state.NewsLog.Slots[26].Text);

        Assert.Equal(classical.Ruleset.Diplomacy.StateCodes.War, state.Relations.Get("rome", "gaul"));
        Assert.Equal(classical.Ruleset.Diplomacy.StateCodes.Trade, state.Relations.Get("rome", "macedonia"));
    }

    /// <summary>
    /// Done-when 4: ending one full round -- every seat, not just the round-scoped phases on their own
    /// (T75 rework B3) -- appends the round's own blank-line-and-header pair after slot 26 without
    /// duplicating it: slot 26's own text survives the round untouched and appears exactly once, and the
    /// new header is the exact text the engine's own formatter produces for the post-round calendar, not
    /// merely something that starts with "Week" and differs from slot 26 (which the DAT's own week-1
    /// header, rendered in the engine's format instead of the DAT's, would also have satisfied).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="AiGameRunner.Run"/> with <c>turnCap: 16</c> -- one seat turn per nation, so
    /// exactly one full round on the classical world's 16 seats, real AI orders included. The buffer
    /// does fill to its 40-slot capacity from the round's own diplomacy news, but only the ring buffer's
    /// <em>oldest</em> entries (earlier DAT seed lines) shift out; slot 26's own text is never touched,
    /// and it is confirmed to appear exactly once below rather than asserting where in the buffer it
    /// ends up, since that position depends on how much news the seed produces before it.
    /// </para>
    /// <para>
    /// Three seeds, matching the range the PR #367 review verified directly against the DAT and this
    /// engine's own formatter (seeds 1-3: slot 26 survives once each, followed by the round's own news,
    /// then <c>" "</c> and <c>"Week  3      Spring      270BC"</c> as the newest two entries).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void Ending_one_full_round_appends_the_exact_header_after_slot_26_without_duplicating_it(ulong seed)
    {
        var classical = Classical();
        var initial = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);
        var originalSlot26 = initial.NewsLog.Slots[26].Text;

        var result = AiGameRunner.Run(classical.World, classical.Ruleset, classical.Scenario, seed, turnCap: 16);
        var state = result.FinalState;

        Assert.Equal(0, result.CommandsRejected);
        Assert.Equal(0, result.ProjectionMismatches);

        // Slot 26's own text survives the round, exactly once -- not overwritten, not duplicated.
        Assert.Equal(1, state.NewsLog.Slots.Count(s => s.Text == originalSlot26));

        // The round's mandatory blank-line-and-header pair (news-log-format-and-messages.md Q3) is the
        // newest two entries, and the expected header is derived from the engine's own formatter and the
        // post-round calendar -- not a hand-typed literal -- so this asserts the actual rule the round
        // tick follows, not one seed's coincidental output.
        var seasonName = classical.Ruleset.NewsLog.SeasonNames[state.Calendar.SeasonIndex];
        var expectedHeader = NewsLogWriter.RenderWeekHeader(state.Calendar.Week, seasonName, state.Calendar.YearBc);
        Assert.Equal(" ", state.NewsLog.Slots[^2].Text);
        Assert.Equal(expectedHeader, state.NewsLog.Slots[^1].Text);

        // #372 N4: the last-slot check above would still pass if the round tick wrote this same header
        // twice (once at the true end, once earlier). It appears exactly once.
        Assert.Equal(1, state.NewsLog.Slots.Count(s => s.Text == expectedHeader));

        // Not duplicated in the engine's own format either: the DAT seed's slot 26 ("Week 1 ... 270 BC",
        // single-spaced, no space before "BC") is byte-different from the engine's own week-1 header
        // ("Week  1 ... 270BC" -- T75 Hazards), and this confirms the round tick never independently
        // wrote that second form of the same week during this round.
        var engineWeek1Header = NewsLogWriter.RenderWeekHeader(1, classical.Ruleset.NewsLog.SeasonNames[0], 270);
        Assert.DoesNotContain(state.NewsLog.Slots, s => s.Text == engineWeek1Header);
    }

    /// <summary>
    /// Done-when 4: a war in the starting matrix is a war to the attack-legality check -- not merely a
    /// value <see cref="DiplomaticRelations.Get"/> happens to return, but the exact state
    /// <see cref="AttackLegality.Check(GameState, Ruleset, AttackArmyCommand)"/> reads before refusing an
    /// attack as <see cref="AttackArmyRejections.NotAtWar"/>.
    /// </summary>
    [Fact]
    public void A_war_in_the_starting_matrix_is_a_war_to_the_attack_legality_check()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var state = GameStateFactory.CreateInitial(classical.World, ruleset, classical.Scenario);

        Assert.Equal(ruleset.Diplomacy.StateCodes.War, state.Relations.Get("rome", "gaul"));

        // Two synthetic, adjacent armies standing in for Rome and Gaul: the classical world's own DAT
        // starting armies are not guaranteed to be adjacent to each other, and this test's only interest
        // is whether the war state GameStateFactory wired in actually gates AttackLegality, not geography.
        var romeArmy = new ArmyState(
            Id: "test-rome-army", Nation: "rome", X: 10, Y: 10, Moves: 1, Morale: 50, Money: 0, SupplyTons: 0,
            CoveredTileCode: 2, AboardFleetId: null, Units: ValueList<UnitSlot>.Empty);
        var gaulArmy = new ArmyState(
            Id: "test-gaul-army", Nation: "gaul", X: 11, Y: 10, Moves: 1, Morale: 50, Money: 0, SupplyTons: 0,
            CoveredTileCode: 2, AboardFleetId: null, Units: ValueList<UnitSlot>.Empty);
        var warState = state with { Armies = ValueList.From(state.Armies.Append(romeArmy).Append(gaulArmy)) };

        var command = new AttackArmyCommand("rome", "test-rome-army", "test-gaul-army");
        var warRejection = AttackLegality.Check(warState, ruleset, command);

        Assert.True(
            warRejection is null || warRejection.Code != AttackArmyRejections.NotAtWar,
            $"Expected no NotAtWar refusal while at war; got: {warRejection?.Code.Value} - {warRejection?.Message}");

        // The mirror: peace between the same two nations is refused specifically as NotAtWar, proving
        // the gate actually reads state.Relations rather than always accepting.
        var peaceState = warState with
        {
            Relations = warState.Relations.WithRelation("rome", "gaul", ruleset.Diplomacy.StateCodes.Peace),
        };
        var peaceRejection = AttackLegality.Check(peaceState, ruleset, command);
        Assert.Equal(AttackArmyRejections.NotAtWar, peaceRejection?.Code);
    }

    /// <summary>
    /// Done-when 2's ruleset-dependent half: a relation value below the lowest configured cooldown is
    /// rejected when the game is created, since <see cref="GameDataValidation.ValidateWorld"/> has no
    /// ruleset to check it against at load time.
    /// </summary>
    [Fact]
    public void A_relation_value_below_the_minimum_cooldown_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var minCooldown = new[]
        {
            ruleset.Diplomacy.CooldownAfterBrokenTrade,
            ruleset.Diplomacy.CooldownAfterBrokenAlliance,
            ruleset.Diplomacy.CooldownAfterEndedWar,
            ruleset.Diplomacy.CooldownAfterPeaceTerms,
            ruleset.Diplomacy.CooldownAfterAllyPeace,
        }.Min();

        var badRelations = classical.World.StartingRelations!.WithRelation("rome", "gaul", minCooldown - 1);
        var world = classical.World with { StartingRelations = badRelations };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains($"{minCooldown - 1}", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>T75 rework N1: a value above the maximum relation code (War + 1) is rejected too.</summary>
    [Fact]
    public void A_relation_value_above_the_maximum_code_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var aboveWar = ruleset.Diplomacy.StateCodes.War + 1;

        var badRelations = classical.World.StartingRelations!.WithRelation("rome", "gaul", aboveWar);
        var world = classical.World with { StartingRelations = badRelations };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains(aboveWar.ToString(CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Done-when 2's ruleset-dependent half: a news text over the ruleset's message length, counted in
    /// bytes, is rejected when the game is created.
    /// </summary>
    [Fact]
    public void A_news_text_over_the_ruleset_message_length_in_bytes_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var tooLong = new string('x', ruleset.NewsLog.MessageByteLength); // one over the -1 NUL budget
        var badNews = new NewsLog(MostRecentSlot: 0, ValueList.Of(new NewsEntry(tooLong)));
        var world = classical.World with { StartingNews = badNews };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains("byte", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// T75 rework B4: a news text holding a byte outside the printable 0x20-0x7E range is rejected --
    /// and specifically as a byte-range violation, not merely as "too long", since a non-ASCII character
    /// can be short in UTF-16 <see cref="string.Length"/> while being multiple bytes in the encoding the
    /// DAT parser and the news writer both use. <c>"café"</c> is 4 chars, far under the byte limit
    /// by char count alone, which is exactly the gap the length-only check used to miss.
    /// </summary>
    [Fact]
    public void A_news_text_with_a_non_printable_byte_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var nonAscii = "café";
        var badNews = new NewsLog(MostRecentSlot: 0, ValueList.Of(new NewsEntry(nonAscii)));
        var world = classical.World with { StartingNews = badNews };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains("printable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Follow-up #372 N1: a slot at exactly the byte limit (one under
    /// <see cref="NewsLogRules.MessageByteLength"/>, the trailing NUL's own budget) passes -- proving the
    /// check above is <c>&gt;</c>, not <c>&gt;=</c>; a mutation that changed the comparison direction
    /// would go unnoticed without this boundary case.
    /// </summary>
    [Fact]
    public void A_news_text_at_exactly_the_byte_limit_passes()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var atLimit = new string('x', ruleset.NewsLog.MessageByteLength - 1);
        var goodNews = new NewsLog(MostRecentSlot: 0, ValueList.Of(new NewsEntry(atLimit)));
        var world = classical.World with { StartingNews = goodNews };

        var state = GameStateFactory.CreateInitial(world, ruleset, classical.Scenario);

        Assert.Equal(atLimit, state.NewsLog.Slots[0].Text);
    }

    /// <summary>
    /// Follow-up #372 N2: the low end of the printable range is rejected too, not just the high end
    /// <see cref="A_news_text_with_a_non_printable_byte_is_rejected_at_game_creation"/> already covers.
    /// A tab (0x09) is below 0x20, the printable floor.
    /// </summary>
    [Fact]
    public void A_news_text_with_a_byte_below_the_printable_floor_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var withTab = "a\tb";
        var badNews = new NewsLog(MostRecentSlot: 0, ValueList.Of(new NewsEntry(withTab)));
        var world = classical.World with { StartingNews = badNews };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains("printable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Follow-up #372 N3: <see cref="GameStateFactory"/>'s own remarks on
    /// <c>ValidateStartingRelationValues</c> (GameStateFactory.cs:205-208) say every cell is checked
    /// against the ruleset's own relation codes explicitly, rather than assuming they are a contiguous
    /// <c>0..max</c> range -- so a ruleset whose codes have a gap must still reject a value that falls in
    /// that gap. The toy ruleset's own codes are contiguous (0-3), so this widens them deliberately to
    /// leave 1 and 4-8 as gaps, and asserts a gap value (1) is rejected exactly as an out-of-range one
    /// would be.
    /// </summary>
    [Fact]
    public void A_relation_value_in_a_gap_between_non_contiguous_codes_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var nonContiguousCodes = new RelationStateCodes(Peace: 0, Trade: 2, Alliance: 5, War: 9);
        var ruleset = classical.Ruleset with
        {
            Diplomacy = classical.Ruleset.Diplomacy with { StateCodes = nonContiguousCodes },
        };

        var badRelations = classical.World.StartingRelations!.WithRelation("rome", "gaul", 1); // the Peace-Trade gap
        var world = classical.World with { StartingRelations = badRelations };

        var ex = Assert.Throws<ArgumentException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains("1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Follow-up #372 N6 (the user's decision of 2026-09-25): a starting news seed with more slots than
    /// the ruleset's own ring buffer is rejected at game creation, with a typed
    /// <see cref="MalformedGameDataException"/> -- <see cref="World.ValidateStartingNewsShape"/> cannot
    /// catch this at load time (it has no <see cref="Ruleset"/> to know the ring's capacity from), so
    /// without this check a seed this long would pass state validation and only be trimmed down to size
    /// on the first write.
    /// </summary>
    [Fact]
    public void A_news_seed_with_more_slots_than_the_ring_buffer_is_rejected_at_game_creation()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var tooManySlots = ruleset.NewsLog.RingBufferSlots + 1;
        var slots = ValueList.From(Enumerable.Range(0, tooManySlots).Select(i => new NewsEntry($"slot {i}")));
        var badNews = new NewsLog(MostRecentSlot: tooManySlots - 1, slots);
        var world = classical.World with { StartingNews = badNews };

        var ex = Assert.Throws<MalformedGameDataException>(
            () => GameStateFactory.CreateInitial(world, ruleset, classical.Scenario));
        Assert.Contains(tooManySlots.ToString(CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            ruleset.NewsLog.RingBufferSlots.ToString(CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
    }
}
