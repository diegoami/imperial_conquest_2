using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Strength;
using IC2.Engine.Tests.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T125.md</c> Done-when 1 to 3: a human's attack on an army opens the tactical battle in the session
/// (<see cref="HumanBattleMode.Tactical"/>), everything the original auto-resolves keeps its instant path, the battle
/// is fought with <c>battle</c> lines and ends in the write-back, the surrender and <em>Computer general</em>.
/// </summary>
public sealed class TacticalBattleSessionTests
{
    public static TheoryData<string> Worlds => new() { "toy", "classical-faithful", "improved" };

    // ---- Done-when 1: the branch ----

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Under_Tactical_a_human_attack_on_an_AI_army_opens_a_battle_instead_of_resolving_it(string world)
    {
        var fixture = TacticalSessionTestbed.For(world, HumanBattleMode.Tactical);
        var before = fixture.Session.State;
        var attacker = before.ArmyById(fixture.AttackerId)!;
        var defender = before.ArmyById(fixture.DefenderId)!;
        var context = TacticalContext.From(fixture.Ruleset);

        var output = fixture.Session.Submit($"attack-army {fixture.AttackerId} {fixture.DefenderId}");

        // The battle is open and nothing was resolved by the instant path.
        var view = Assert.IsType<TacticalBattleView>(output.OpenBattleView);
        Assert.Empty(output.Battles);
        Assert.Null(output.EndedBattleReport);
        Assert.False(view.IsOver);
        Assert.Equal(fixture.AttackerId, view.AttackerArmyId);
        Assert.Equal(fixture.DefenderId, view.DefenderArmyId);
        Assert.Equal(new[] { false, true }, view.ComputerControlled.ToArray());
        Assert.Equal(40, view.Slots.Count);
        Assert.Equal(fixture.Ruleset.Combat.Tactical.BoardWidth * fixture.Ruleset.Combat.Tactical.BoardHeight, view.Grid.Count);

        // The defender was placed at once (a computer half-round); the human attacker places next.
        Assert.False(view.Placed);
        Assert.Equal(TacticalBattleState.AttackerSide, view.SideToMove);
        Assert.Equal(2, view.Counter);

        // The attack spent the attacker's whole move, and the AI side's army carries the +3 and the sort.
        var after = fixture.Session.State;
        Assert.True(attacker.Moves > 0);
        Assert.Equal(0, after.ArmyById(fixture.AttackerId)!.Moves);
        Assert.Equal(attacker.Units, after.ArmyById(fixture.AttackerId)!.Units);
        Assert.Equal(attacker.Morale, after.ArmyById(fixture.AttackerId)!.Morale);
        var computerArmy = after.ArmyById(fixture.DefenderId)!;
        Assert.Equal(defender.Morale + fixture.Ruleset.Combat.Tactical.ComputerArmyMoraleBonus, computerArmy.Morale);
        Assert.Equal(TacticalCopyIn.SortForComputer(defender.Units, context), computerArmy.Units);
        Assert.Equal(before.Armies.Count, after.Armies.Count);
    }

    [Fact]
    public void A_human_defender_is_to_place_first_with_the_attackers_moves_at_zero()
    {
        var session = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Tactical);
        var output = session.Submit("attack-army north-army-1 south-army-1");

        var view = Assert.IsType<TacticalBattleView>(output.OpenBattleView);
        Assert.Equal(new[] { false, false }, view.ComputerControlled.ToArray());
        Assert.Equal(TacticalBattleState.DefenderSide, view.SideToMove);
        Assert.False(view.Placed);
        Assert.Equal(1, view.Counter);
        Assert.All(view.Slots.Take(20).Where(s => s.IsLive), s => Assert.Equal(0, s.Moves));
        Assert.All(view.Slots.Skip(20).Where(s => s.IsLive), s => Assert.True(s.Moves > 0));
        Assert.Equal(0, session.State.ArmyById("north-army-1")!.Moves);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void With_Instant_the_human_attack_resolves_exactly_as_the_dispatcher_resolves_it(string world)
    {
        foreach (var explicitMode in new[] { true, false })
        {
            var fixture = explicitMode
                ? TacticalSessionTestbed.For(world, HumanBattleMode.Instant)
                : TacticalSessionTestbed.For(world, null);
            var before = fixture.Session.State;
            var expected = TacticalSessionTestbed.MainAttack(fixture, before);

            var output = fixture.Session.Submit($"attack-army {fixture.AttackerId} {fixture.DefenderId}");

            Assert.Null(output.OpenBattleView);
            Assert.Null(fixture.Session.OpenTacticalBattle);
            Assert.Equal(expected.State, fixture.Session.State);
            var battle = Assert.Single(output.Battles);
            Assert.Equal(expected.Result, battle);
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Under_Tactical_a_battle_with_no_human_side_is_not_fought_on_the_board(string world)
    {
        // Every seat is an AI seat (watch mode): the order is refused exactly as on main, and no battle opens.
        var tactical = TacticalSessionTestbed.WatchFixture(world, HumanBattleMode.Tactical);
        var instant = TacticalSessionTestbed.WatchFixture(world, HumanBattleMode.Instant);
        var line = $"attack-army {tactical.AttackerId} {tactical.DefenderId}";

        var t = tactical.Session.Submit(line);
        var i = instant.Session.Submit(line);

        Assert.Equal(i.Lines, t.Lines);
        Assert.Equal(instant.Session.State, tactical.Session.State);
        Assert.Null(t.OpenBattleView);
        Assert.Empty(t.Battles);
    }

    [Fact]
    public void An_army_with_no_troops_cannot_be_fought_on_the_board_and_resolves_at_once()
    {
        var armies = TacticalSessionTestbed.DefaultArmies();
        armies[1] = TacticalSessionTestbed.Army("south-army-1", "south", 4, 2, 59, TacticalSessionTestbed.Unit("heavy_infantry", 0));
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies);

        var output = session.Submit("attack-army north-army-1 south-army-1");

        Assert.Null(output.OpenBattleView);
        Assert.Single(output.Battles);
    }

    [Fact]
    public void Under_Tactical_a_siege_resolves_exactly_as_under_Instant()
    {
        var (tactical, instant) = TacticalSessionTestbed.SiegePair();
        var line = TacticalSessionTestbed.SiegeLine;

        var t = tactical.Submit(line);
        var i = instant.Submit(line);

        Assert.Contains("accepted", string.Join('\n', i.Lines), StringComparison.Ordinal);
        Assert.Equal(i.Lines, t.Lines);
        Assert.Equal(instant.State, tactical.State);
        Assert.Null(t.OpenBattleView);
        Assert.Equal(i.Battles, t.Battles);
        Assert.Single(t.Battles);
    }

    [Fact]
    public void Under_Tactical_a_naval_attack_resolves_exactly_as_under_Instant()
    {
        var (tactical, instant) = TacticalSessionTestbed.NavalPair();
        var line = TacticalSessionTestbed.NavalLine;

        var t = tactical.Submit(line);
        var i = instant.Submit(line);

        Assert.Contains("accepted", string.Join('\n', i.Lines), StringComparison.Ordinal);
        Assert.Equal(i.Lines, t.Lines);
        Assert.Equal(instant.State, tactical.State);
        Assert.Null(t.OpenBattleView);
        Assert.Equal(i.Battles, t.Battles);
        Assert.Single(t.Battles);
    }

    [Fact]
    public void A_refused_tactical_attack_leaves_the_state_and_the_war_untouched()
    {
        var tactical = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.FarApart());
        var instant = TacticalSessionTestbed.ToySession(HumanBattleMode.Instant, armies: TacticalSessionTestbed.FarApart());
        var before = tactical.State;

        var t = tactical.Submit("attack-army north-army-1 south-army-1");
        var i = instant.Submit("attack-army north-army-1 south-army-1");

        Assert.Equal(i.Lines, t.Lines);
        Assert.Equal(before, tactical.State);
        Assert.Null(tactical.OpenTacticalBattle);
    }

    [Fact]
    public void The_battle_draws_come_from_one_stream_derived_from_the_seed_and_the_battle_key()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var ruleset = TacticalSessionTestbed.Toy.Ruleset;

        // The war is declared first, which advances the seed; the stream is derived from that seed.
        var declared = TacticalSessionTestbed.DeclaredState(session, ruleset, "north", "south");
        var key = TacticalBattleSession.StreamKey("north-army-1", "south-army-1", declared.Calendar.TurnIndex);
        Assert.Equal($"battle.tactical:north-army-1:south-army-1:{declared.Calendar.TurnIndex}", key);

        var context = TacticalContext.From(ruleset);
        var draws = new RngBattleDraws(SplitMix64Rng.ForStream(declared.RandomSeed, key));
        var expected = TacticalDriver.Run(
            TacticalCopyIn.Start(declared, "north-army-1", "south-army-1", context, draws).Battle,
            context,
            draws,
            new IC2.Engine.Battle.Tactical.General.ComputerGeneral());

        var output = session.Submit("attack-army north-army-1 south-army-1");
        var view = output.OpenBattleView!;

        Assert.Equal(expected.Slots, view.Slots);
        Assert.Equal(expected.Grid, view.Grid);
        Assert.Equal(expected.Counter, view.Counter);
    }

    [Fact]
    public void The_same_seed_and_lines_give_a_byte_identical_state_and_output_twice()
    {
        var first = TacticalSessionTestbed.PlayScripted(TacticalSessionTestbed.StrongAttacker(), TacticalSessionTestbed.WonBattleScript);
        var second = TacticalSessionTestbed.PlayScripted(TacticalSessionTestbed.StrongAttacker(), TacticalSessionTestbed.WonBattleScript);

        Assert.Equal(first.Played.Transcript, second.Played.Transcript);
        Assert.Equal(first.Played.Session.State, second.Played.Session.State);
        Assert.NotNull(first.Played.Report);
    }

    // ---- the lines refused while a battle is open ----

    [Fact]
    public void While_a_battle_is_open_every_line_but_help_save_quit_and_the_battle_verbs_is_refused()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        session.Submit("attack-army north-army-1 south-army-1");
        var state = session.State;

        foreach (var line in new[] { "status", "end", "move north-army-1 1 1", "news", "load x.json", "attack-army north-army-1 south-army-1", "bogus" })
        {
            var output = session.Submit(line);
            Assert.Contains("A battle is open", string.Join('\n', output.Lines), StringComparison.Ordinal);
            Assert.NotNull(output.OpenBattleView);
        }

        Assert.Equal(state, session.State);

        var help = session.Submit("help");
        Assert.Contains(help.Lines, l => l.Contains("battle surrender", StringComparison.Ordinal));
        Assert.False(help.ShouldExit);
        Assert.True(session.Submit("quit").ShouldExit);
    }

    [Fact]
    public void The_help_of_a_session_with_no_open_battle_lists_no_battle_verbs()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);

        Assert.DoesNotContain(session.Submit("help").Lines, l => l.Contains("battle surrender", StringComparison.Ordinal));
        Assert.Contains("There is no battle to fight.", session.Submit("battle show").Lines);
    }

    [Fact]
    public void The_battle_verbs_refuse_bad_orders_with_a_message_and_change_nothing()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        session.Submit("attack-army north-army-1 south-army-1");
        var view = session.OpenTacticalBattle;

        foreach (var line in new[]
                 {
                     "battle place 20 5 2",   // an enemy unit
                     "battle place 0 5 9",    // outside the home rows
                     "battle place 0 1 0",    // an occupied cell
                     "battle move 0 5 5",     // moves during placement
                     "battle attack 0 20",    // orders during placement
                     "battle shoot 1 20",
                     "battle place 0 x 2",    // malformed
                     "battle frobnicate",
                     "battle",
                 })
        {
            var output = session.Submit(line);
            Assert.NotNull(output.OpenBattleView);
            Assert.True(
                output.Lines.Any(l => l.StartsWith("Refused:", StringComparison.Ordinal) || l.StartsWith("Usage:", StringComparison.Ordinal)),
                $"'{line}' printed no refusal: {string.Join(" | ", output.Lines)}");
        }

        Assert.Equal(view, session.OpenTacticalBattle);
    }

    // ---- Done-when 2: a battle to its end ----

    [Fact]
    public void A_won_battle_is_written_back_compacted_promoted_and_paid()
    {
        var run = TacticalSessionTestbed.PlayScripted(TacticalSessionTestbed.StrongAttacker(), TacticalSessionTestbed.WonBattleScript);

        Assert.True(run.Played.Report!.WinnerSide == TacticalBattleState.AttackerSide, run.Played.Transcript);
        TacticalSessionTestbed.AssertWrittenBack(run);
        Assert.Contains(run.Played.Report!.ExchangeLog, e => e is TacticalShotEvent);
        Assert.Contains(run.Played.Report!.ExchangeLog, e => e is TacticalMeleeEvent);
        Assert.Contains("destroys", string.Join(' ', run.Played.Session.State.NewsLog.Slots.Select(s => s.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lost_battle_is_written_back_for_the_AI_winner()
    {
        var run = TacticalSessionTestbed.PlayScripted(TacticalSessionTestbed.WeakAttacker(), TacticalSessionTestbed.LostBattleScript);

        Assert.True(run.Played.Report!.WinnerSide == TacticalBattleState.DefenderSide, run.Played.Transcript);
        TacticalSessionTestbed.AssertWrittenBack(run);
        Assert.Null(run.Played.Session.State.ArmyById("north-army-1"));
    }

    [Fact]
    public void The_battle_ended_lines_name_the_winner_and_the_captured_goods()
    {
        var run = TacticalSessionTestbed.PlayScripted(TacticalSessionTestbed.StrongAttacker(), TacticalSessionTestbed.WonBattleScript);

        Assert.Contains("Battle ended.", run.Played.Transcript, StringComparison.Ordinal);
        Assert.Contains("Northern League's army defeats Southern League's army.", run.Played.Transcript, StringComparison.Ordinal);
        Assert.Contains("captured", run.Played.Transcript, StringComparison.Ordinal);
    }

    // ---- the peace dialog (TBattleOver_OK step 6) ----

    [Fact]
    public void The_peace_offer_appears_exactly_when_its_four_conditions_hold()
    {
        var all = TacticalSessionTestbed.PeaceCase();
        Assert.True(all.Conclusion.PeaceDrawn);
        Assert.True(all.Conclusion.PeaceOffered);
        Assert.Contains(all.Conclusion.Events, e => e is PeaceTreatyOffered);

        // Each condition failing alone: no offer, and no Random(5) drawn when one of the first three fails.
        var weakUnity = TacticalSessionTestbed.PeaceCase(unityThresholdDelta: 1);
        Assert.False(weakUnity.Conclusion.PeaceOffered);
        Assert.False(weakUnity.Conclusion.PeaceDrawn);

        var fewCities = TacticalSessionTestbed.PeaceCase(cityThresholdDelta: 1);
        Assert.False(fewCities.Conclusion.PeaceOffered);
        Assert.False(fewCities.Conclusion.PeaceDrawn);

        // strength(W) one less than strength(L) passes; equal to it fails; greater fails.
        var oneLess = TacticalSessionTestbed.PeaceCase(strengthDelta: 1);
        Assert.True(oneLess.Conclusion.PeaceOffered);
        var equal = TacticalSessionTestbed.PeaceCase(strengthDelta: 0);
        Assert.False(equal.Conclusion.PeaceOffered);
        Assert.False(equal.Conclusion.PeaceDrawn);
        var greater = TacticalSessionTestbed.PeaceCase(strengthDelta: -1);
        Assert.False(greater.Conclusion.PeaceOffered);
        Assert.False(greater.Conclusion.PeaceDrawn);

        // The fourth: the three tests pass, the Random(5) is drawn, and it comes up too high.
        var unlucky = TacticalSessionTestbed.PeaceCase(chanceNumerator: 0);
        Assert.True(unlucky.Conclusion.PeaceDrawn);
        Assert.False(unlucky.Conclusion.PeaceOffered);
    }

    [Fact]
    public void The_peace_offer_is_pending_for_the_human_loser_after_the_session_ends_the_battle()
    {
        var session = TacticalSessionTestbed.PeaceSession();
        var output = TacticalSessionTestbed.PlayUntilEnded(session, TacticalSessionTestbed.LostBattleScript);

        Assert.NotNull(output.EndedBattleReport);
        Assert.True(session.HasPendingPeaceOffers);
        Assert.Contains(output.Lines, l => l.Contains("peace-yes", StringComparison.Ordinal));
        Assert.Equal("Answer the pending peace treaty offer (peace-yes / peace-no) before saving.", session.Submit("save never.json").Lines[1]);
    }

    [Fact]
    public void Between_two_human_seats_no_peace_is_offered()
    {
        var session = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Tactical, TacticalSessionTestbed.WeakAttacker());
        session.Submit("attack-army north-army-1 south-army-1");
        var output = TacticalSessionTestbed.PlayUntilEnded(session, new[] { "battle general on" }, repeat: "battle general on");

        Assert.NotNull(output.EndedBattleReport);
        Assert.False(session.HasPendingPeaceOffers);
    }

    // ---- Done-when 3: surrender and Computer general ----

    [Fact]
    public void Surrender_asks_to_confirm_then_removes_the_surrendering_sides_units_with_no_cascade()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        session.Submit("attack-army north-army-1 south-army-1");
        var before = session.State;

        var ask = session.Submit("battle surrender");
        Assert.Contains("battle surrender yes", string.Join('\n', ask.Lines), StringComparison.Ordinal);
        Assert.NotNull(ask.OpenBattleView);
        Assert.Equal(before, session.State);

        var done = session.Submit("battle surrender yes");

        var report = Assert.IsType<TacticalBattleReport>(done.EndedBattleReport);
        Assert.Null(done.OpenBattleView);
        Assert.Equal(TacticalBattleState.DefenderSide, report.WinnerSide);
        Assert.Contains(report.ExchangeLog, e => e is TacticalSurrenderedEvent { Side: TacticalBattleState.AttackerSide });
        Assert.DoesNotContain(report.ExchangeLog, e => e is TacticalRoutedEvent or TacticalCascadeRemovedEvent or TacticalShotEvent or TacticalMeleeEvent);
        Assert.Null(session.State.ArmyById("north-army-1"));
        var winner = session.State.ArmyById("south-army-1")!;
        Assert.Equal(report.WinnerStartTotal, report.WinnerFinishTotal);
        Assert.Equal(before.ArmyById("south-army-1")!.TotalTroops, winner.TotalTroops);
        Assert.Contains("surrenders", string.Join('\n', done.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Computer_general_on_plays_the_humans_side_to_the_end()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        session.Submit("attack-army north-army-1 south-army-1");

        var output = session.Submit("battle general on");

        var report = Assert.IsType<TacticalBattleReport>(output.EndedBattleReport);
        Assert.Null(session.OpenTacticalBattle);
        Assert.Equal(TacticalBattleState.AttackerSide, report.WinnerSide);
        Assert.Null(session.State.ArmyById("south-army-1"));
        Assert.Contains("Battle ended.", output.Lines);
    }

    [Fact]
    public void Computer_general_off_returns_the_next_half_round_to_the_human()
    {
        // Without "off", the defender's general plays its half-rounds at once.
        var withGeneral = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Tactical);
        withGeneral.Submit("attack-army north-army-1 south-army-1");
        withGeneral.Submit("battle general on");               // the defender places by the general
        withGeneral.Submit("battle end");                       // the attacker's placement ends
        var played = withGeneral.Submit("battle end");          // the attacker's move ends, the defender's general moves
        Assert.True(withGeneral.OpenTacticalBattle!.ComputerGeneral[TacticalBattleState.DefenderSide]);
        Assert.Equal(TacticalBattleState.AttackerSide, withGeneral.OpenTacticalBattle!.SideToMove);
        Assert.Contains(played.Lines, l => l.Contains("(south) to move", StringComparison.Ordinal));

        // With "off", it is the human defender's half-round, and nothing is played for it.
        var withOff = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Tactical);
        withOff.Submit("attack-army north-army-1 south-army-1");
        withOff.Submit("battle general on");
        withOff.Submit("battle end");
        var off = withOff.Submit("battle general off");
        Assert.Contains(off.Lines, l => l.Contains("off", StringComparison.Ordinal));
        Assert.False(withOff.OpenTacticalBattle!.ComputerGeneral[TacticalBattleState.DefenderSide]);
        withOff.Submit("battle end");
        Assert.Equal(TacticalBattleState.DefenderSide, withOff.OpenTacticalBattle!.SideToMove);

        // And "off" with nothing on is refused.
        Assert.Contains("Refused", string.Join('\n', withOff.Submit("battle general off").Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void The_methods_for_the_battle_screen_are_the_same_lines()
    {
        var methods = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var lines = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        methods.Submit("attack-army north-army-1 south-army-1");
        lines.Submit("attack-army north-army-1 south-army-1");

        Assert.Equal(lines.Submit("battle place 0 5 2").Lines, methods.BattlePlace(0, 5, 2).Lines);
        Assert.Equal(lines.Submit("battle clear 0").Lines, methods.BattleClear(0).Lines);
        Assert.Equal(lines.Submit("battle end").Lines, methods.BattleEnd().Lines);
        Assert.Equal(lines.Submit("battle move 0 5 3").Lines, methods.BattleMove(0, 5, 3).Lines);
        Assert.Equal(lines.Submit("battle shoot 1 20").Lines, methods.BattleShoot(1, 20).Lines);
        Assert.Equal(lines.Submit("battle attack 0 20").Lines, methods.BattleAttack(0, 20).Lines);
        Assert.Equal(lines.Submit("battle surrender").Lines, methods.BattleSurrender(false).Lines);
        Assert.Equal(lines.Submit("battle general on").Lines, methods.BattleGeneral(true).Lines);
        Assert.Equal(lines.State, methods.State);
    }
}

/// <summary>The fixtures the T125 session, save and CLI tests share.</summary>
internal static class TacticalSessionTestbed
{
    /// <summary>
    /// A human battle won with <see cref="StrongAttacker"/>: the archers are placed, the light infantry and the archers
    /// advance, the archers shoot, the infantry takes the heavy infantry in melee until it routs.
    /// </summary>
    public static readonly string[] WonBattleScript =
    {
        "attack-army north-army-1 south-army-1",
        "battle place 1 1 2",
        "battle end",
        "battle move 0 8 5",
        "battle move 1 6 3",
        "battle end",
        "battle move 0 8 6",
        "battle move 1 8 5",
        "battle shoot 1 20",
        "battle end",
        "battle attack 0 20",
        "battle shoot 1 20",
        "battle clear 0",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
    };

    /// <summary>A human battle lost with <see cref="WeakAttacker"/>: the lone infantry advances and is beaten.</summary>
    public static readonly string[] LostBattleScript =
    {
        "attack-army north-army-1 south-army-1",
        "battle end",
        "battle move 0 8 5",
        "battle end",
        "battle move 0 8 6",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle attack 0 20",
        "battle end",
        "battle end",
        "battle end",
    };

    public static ResolvedScenario Toy => CoreTestbed.Toy;

    public static string SiegeLine => "besiege-city north-army-1 meridia";

    public static string NavalLine => "attack-fleet north-fleet-1 south-fleet-1";

    public static StartingArmy Army(string id, string nation, int x, int y, int morale, params UnitSlot[] units) =>
        new(id, nation, X: x, Y: y, Morale: morale, Money: 0, SupplyTons: 0, Moves: 5, Units: ValueList.From(units));

    public static UnitSlot Unit(string type, int troops, int quality = 6, string name = "unit", int label = 0) =>
        new(label, type, troops, quality, name);

    public static List<StartingArmy> DefaultArmies() => new()
    {
        Army("north-army-1", "north", 3, 2, 68, Unit("light_infantry", 3000, 6, "1st Foot"), Unit("archers", 1000, 7, "Bowmen")),
        Army("south-army-1", "south", 4, 2, 59, Unit("heavy_infantry", 1200, 6, "Guards")),
    };

    public static List<StartingArmy> FarApart() => new()
    {
        Army("north-army-1", "north", 0, 0, 68, Unit("light_infantry", 3000)),
        Army("south-army-1", "south", 7, 5, 59, Unit("heavy_infantry", 1200)),
    };

    public static List<StartingArmy> StrongAttacker() => new()
    {
        Army("north-army-1", "north", 3, 2, 68, Unit("light_infantry", 9000, 6, "1st Foot"), Unit("archers", 3000, 7, "Bowmen")) with { Money = 100, SupplyTons = 500 },
        Army("south-army-1", "south", 4, 2, 59, Unit("heavy_infantry", 600, 6, "Guards")) with { Money = 40, SupplyTons = 60 },
    };

    public static List<StartingArmy> WeakAttacker() => new()
    {
        Army("north-army-1", "north", 3, 2, 68, Unit("light_infantry", 400, 6, "1st Foot")) with { Money = 100, SupplyTons = 500 },
        Army("south-army-1", "south", 4, 2, 59, Unit("heavy_infantry", 6000, 6, "Guards")) with { Money = 40, SupplyTons = 60 },
    };

    /// <summary>The toy world with the given starting armies; the human is north unless <paramref name="hotseat"/>.</summary>
    public static GameSession ToySession(
        HumanBattleMode? mode,
        Ruleset? ruleset = null,
        IEnumerable<StartingArmy>? armies = null,
        bool hotseat = false)
    {
        var toy = Toy;
        var world = toy.World with { StartingArmies = ValueList.From(armies?.ToList() ?? DefaultArmies()) };
        var scenario = hotseat
            ? toy.Scenario with { Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Human, Personality = null })) }
            : toy.Scenario;
        return new GameSession(world, ruleset ?? toy.Ruleset, scenario) { HumanBattles = mode ?? HumanBattleMode.Instant };
    }

    public static GameSession HotseatSession(HumanBattleMode mode, IEnumerable<StartingArmy>? armies = null) =>
        ToySession(mode, armies: armies, hotseat: true);

    // ---- the three worlds ----

    public sealed record Fixture(GameSession Session, Ruleset Ruleset, World World, string AttackerId, string DefenderId);

    private static readonly Lazy<GameDataRepository> Repository = new(() => GameDataRepository.Load(ModelTestPaths.DataRoot));

    public static Fixture For(string world, HumanBattleMode? mode)
    {
        if (world == "toy")
        {
            var toy = Toy;
            return new Fixture(ToySession(mode), toy.Ruleset, toy.World with { StartingArmies = ValueList.From(DefaultArmies()) }, "north-army-1", "south-army-1");
        }

        var classical = Repository.Value.Resolve("classical-mediterranean");
        var ruleset = world == "improved" ? Repository.Value.RulesetById("improved")! : classical.Ruleset;
        ruleset = ruleset with { Flags = ruleset.Flags with { NewGameTurnOrder = NewGameTurnOrderPolicy.Fixed } };
        var armies = classical.World.StartingArmies.Select(a => a.Id == "army-3" ? a with { X = 101, Y = 37, Moves = 8 } : a).ToList();
        var worldWithArmies = classical.World with { StartingArmies = ValueList.From(armies) };
        var session = mode is null
            ? new GameSession(worldWithArmies, ruleset, classical.Scenario, null, "rome")
            : new GameSession(worldWithArmies, ruleset, classical.Scenario, null, "rome") { HumanBattles = mode.Value };
        return new Fixture(session, ruleset, worldWithArmies, "army-0", "army-3");
    }

    public static Fixture WatchFixture(string world, HumanBattleMode mode)
    {
        if (world == "toy")
        {
            var toy = Toy;
            var scenario = toy.Scenario with
            {
                Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })),
            };
            var worldWithArmies = toy.World with { StartingArmies = ValueList.From(DefaultArmies()) };
            return new Fixture(
                new GameSession(worldWithArmies, toy.Ruleset, scenario) { HumanBattles = mode },
                toy.Ruleset,
                worldWithArmies,
                "north-army-1",
                "south-army-1");
        }

        var classical = Repository.Value.Resolve("classical-mediterranean");
        var ruleset = world == "improved" ? Repository.Value.RulesetById("improved")! : classical.Ruleset;
        var armies = classical.World.StartingArmies.Select(a => a.Id == "army-3" ? a with { X = 101, Y = 37 } : a).ToList();
        var w = classical.World with { StartingArmies = ValueList.From(armies) };
        return new Fixture(new GameSession(w, ruleset, classical.Scenario) { HumanBattles = mode }, ruleset, w, "army-0", "army-3");
    }

    // ---- what main does ----

    public sealed record MainResult(GameState State, BattleResult Result);

    /// <summary>What <c>main</c> does with the human's attack: the composed declaration, then the dispatcher's own field battle.</summary>
    public static MainResult MainAttack(Fixture fixture, GameState before)
    {
        var dispatcher = DispatcherFor(fixture.Ruleset, fixture.World);
        var attacker = before.ArmyById(fixture.AttackerId)!;
        var defender = before.ArmyById(fixture.DefenderId)!;
        var state = before;
        var declared = dispatcher.Dispatch(state, new DeclareWarCommand(attacker.Nation, defender.Nation));
        if (!declared.IsRejected)
        {
            state = NewsLogWriter.Append(declared.State, declared.Events, fixture.Ruleset.NewsLog);
        }

        var result = dispatcher.Dispatch(state, new AttackArmyCommand(attacker.Nation, fixture.AttackerId, fixture.DefenderId));
        Assert.False(result.IsRejected, result.Rejection?.Message);
        return new MainResult(
            NewsLogWriter.Append(result.State, result.Events, fixture.Ruleset.NewsLog),
            result.Events.OfType<BattleResolved>().Single().Result);
    }

    private static CommandDispatcher DispatcherFor(Ruleset ruleset, World world) =>
        new(SystemRegistry.FromEngineAssembly(), ruleset, world, NullEventSink.Instance);

    /// <summary>The state the session derives the battle from: the composed declaration of war applied.</summary>
    public static GameState DeclaredState(GameSession session, Ruleset ruleset, string attackerNation, string defenderNation)
    {
        var declared = DispatcherFor(ruleset, session.World).Dispatch(session.State, new DeclareWarCommand(attackerNation, defenderNation));
        Assert.False(declared.IsRejected, declared.Rejection?.Message);
        return NewsLogWriter.Append(declared.State, declared.Events, ruleset.NewsLog);
    }

    // ---- siege and naval ----

    public static (GameSession Tactical, GameSession Instant) SiegePair()
    {
        // North's army stands next to meridia, which belongs to the south in the toy scenario.
        var armies = new List<StartingArmy>
        {
            Army("north-army-1", "north", 3, 3, 68, Unit("light_infantry", 15000, 6, "1st Foot")),
            Army("south-army-1", "south", 6, 5, 59, Unit("heavy_infantry", 600)),
        };
        return (ToySession(HumanBattleMode.Tactical, armies: armies), ToySession(HumanBattleMode.Instant, armies: armies));
    }

    public static (GameSession Tactical, GameSession Instant) NavalPair()
    {
        var toy = Toy;
        var fleets = toy.World.StartingFleets.Select(f => f.Id == "south-fleet-1" ? f with { X = 1, Y = 3 } : f).ToList();
        var world = toy.World with
        {
            StartingFleets = ValueList.From(fleets),
            StartingArmies = ValueList.From(DefaultArmies()),
        };
        return (
            new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical },
            new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Instant });
    }

    // ---- playing a script ----

    public sealed record Played(GameSession Session, string Transcript, TacticalBattleReport? Report);

    public sealed record Run(GameState Opened, GameState Before, Played Played);

    public static Played Play(GameSession session, IEnumerable<string> lines)
    {
        var transcript = new System.Text.StringBuilder();
        TacticalBattleReport? report = null;
        foreach (var line in lines)
        {
            var output = session.Submit(line);
            foreach (var text in output.Lines)
            {
                transcript.Append(text).Append('\n');
            }

            report ??= output.EndedBattleReport;
        }

        return new Played(session, transcript.ToString(), report);
    }

    public static Run PlayScripted(IEnumerable<StartingArmy> armies, string[] script)
    {
        var session = ToySession(HumanBattleMode.Tactical, armies: armies);
        var before = session.State;
        var opening = Play(session, script.Take(1));
        var opened = session.State;
        var rest = Play(session, script.Skip(1));
        return new Run(opened, before, new Played(session, opening.Transcript + rest.Transcript, rest.Report ?? opening.Report));
    }

    /// <summary>Plays the lines, then repeats <paramref name="repeat"/> (default <c>battle end</c>) until the battle has ended.</summary>
    public static SessionOutput PlayUntilEnded(GameSession session, IEnumerable<string> lines, string repeat = "battle end")
    {
        SessionOutput? last = null;
        foreach (var line in lines)
        {
            last = session.Submit(line);
            if (last.EndedBattleReport is not null)
            {
                return last;
            }
        }

        for (var i = 0; i < 200 && session.OpenTacticalBattle is not null; i++)
        {
            last = session.Submit(repeat);
            if (last.EndedBattleReport is not null)
            {
                return last;
            }
        }

        return last!;
    }

    public static ValueList<int> ByType(IEnumerable<UnitSlot> units, Ruleset ruleset) =>
        ValueList.From(ruleset.UnitTypes.Select(t => units.Where(u => u.UnitTypeId == t.Id).Sum(u => u.Troops)));

    /// <summary>The write-back's steps 1 to 5 against the state the battle opened from.</summary>
    public static void AssertWrittenBack(Run run)
    {
        var ruleset = Toy.Ruleset;
        var combat = ruleset.Combat;
        var report = run.Played.Report!;
        var opened = run.Opened;
        var after = run.Played.Session.State;
        var winnerBefore = opened.ArmyById(report.WinnerArmyId)!;
        var loserBefore = opened.ArmyById(report.LoserArmyId)!;

        // The loser army is deleted; the winner keeps its compacted, promoted survivors.
        Assert.Null(after.ArmyById(report.LoserArmyId));
        var winner = after.ArmyById(report.WinnerArmyId)!;
        Assert.True(winner.Units.Count <= winnerBefore.Units.Count);
        Assert.All(winner.Units, u => Assert.True(u.Troops > 0));
        Assert.Equal(winner.Units.Count, report.Promotions.Count);
        for (var i = 0; i < winner.Units.Count; i++)
        {
            var promotion = report.Promotions[i];
            Assert.Equal(i, promotion.Unit);
            Assert.Equal(winner.Units[i].Quality, promotion.QualityAfter);
            var floor = Math.Max(combat.QualityFloor, promotion.QualityBefore);
            Assert.True(promotion.QualityAfter == floor || promotion.QualityAfter == Math.Min(combat.QualityCap, floor + 1));
        }

        // The report's start and finish troops equal the state's, per type and in total.
        Assert.Equal(ByType(winnerBefore.Units, ruleset), report.WinnerStartByType);
        Assert.Equal(winnerBefore.TotalTroops, report.WinnerStartTotal);
        Assert.Equal(ByType(loserBefore.Units, ruleset), report.LoserStartByType);
        Assert.Equal(loserBefore.TotalTroops, report.LoserStartTotal);
        Assert.Equal(ByType(winner.Units, ruleset), report.WinnerFinishByType);
        Assert.Equal(winner.TotalTroops, report.WinnerFinishTotal);

        // Money moved, supplies moved and capped at the survivors' troops div the divisor.
        Assert.Equal(winnerBefore.Money + loserBefore.Money, winner.Money);
        Assert.Equal(report.CapturedMoney, loserBefore.Money);
        Assert.Equal(
            Math.Min(winnerBefore.SupplyTons + loserBefore.SupplyTons, winner.TotalTroops / combat.AbsorbedSupplyTroopDivisor),
            winner.SupplyTons);

        // Unity +25 (capped) for the winner, -25 for the loser, and the news line.
        var swing = combat.UnitySwing;
        Assert.Equal(
            Math.Min(ruleset.Economy.UnityCap, opened.NationById(report.WinnerNationId)!.Unity + swing),
            after.NationById(report.WinnerNationId)!.Unity);
        Assert.Equal(opened.NationById(report.LoserNationId)!.Unity - swing, after.NationById(report.LoserNationId)!.Unity);
        Assert.Equal(opened.NewsLog.Slots.Count + 1, after.NewsLog.Slots.Count);
        Assert.Contains("destroys army of", after.NewsLog.Slots[^1].Text, StringComparison.Ordinal);
        Assert.NotEqual(opened.RandomSeed, after.RandomSeed);
    }

    // ---- the peace dialog ----

    public sealed record PeaceResult(TacticalBattleConclusion Conclusion);

    private sealed record PeaceProbe(long WinnerPower, int LoserUnity, int LoserCities);

    private static Ruleset PeaceRuleset(Ruleset ruleset, int unityThreshold, int cityThreshold, int chanceNumerator) =>
        ruleset with
        {
            Combat = ruleset.Combat with
            {
                AutoPeaceLoserUnityThreshold = unityThreshold,
                AutoPeaceLoserCityThreshold = cityThreshold,
                AutoPeaceChanceNumerator = chanceNumerator,
            },
        };

    /// <summary>A reserve army of <paramref name="nation"/> whose total strength is exactly <paramref name="power"/>.</summary>
    private static StartingArmy ReserveWithPower(string nation, long power, Ruleset ruleset)
    {
        // power = (sum(weight * troops / divisor) / powerDivisor) * morale; one unit giving a quotient of 1 and a
        // morale of `power` is the smallest way to hit any integer exactly.
        for (var troops = 1; troops < 2_000_000; troops++)
        {
            var unit = Unit("heavy_infantry", troops, 5, "Reserve");
            if (ArmyPower.Compute(new[] { unit }, 1, ruleset) == 1)
            {
                return Army(nation + "-reserve", nation, 0, 0, checked((int)power), unit);
            }
        }

        throw new InvalidOperationException("No reserve unit has a strength quotient of 1.");
    }

    private static PeaceProbe Probe()
    {
        var toy = Toy;
        var world = toy.World with { StartingArmies = ValueList.From(WeakAttacker()) };
        var game = GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario);
        var battle = TacticalBattleSession.Open(game, "north-army-1", "south-army-1", toy.Ruleset, out var opened);
        battle.GeneralOn();
        var conclusion = battle.Conclude(opened, toy.Ruleset);
        var after = conclusion.State!;
        Assert.Equal(TacticalBattleState.DefenderSide, conclusion.Report!.WinnerSide);
        return new PeaceProbe(
            after.Armies.Where(a => a.Nation == "south").Sum(a => ArmyPower.Compute(a.Units, a.Morale, toy.Ruleset)),
            after.NationById("north")!.Unity,
            after.CountCitiesOwnedBy("north"));
    }

    /// <summary>
    /// The human north loses its weak army to the AI south; the loser keeps a reserve whose strength is the winner's plus
    /// <paramref name="strengthDelta"/>, and the unity and city thresholds sit just under the loser's figures
    /// (<paramref name="unityThresholdDelta"/> 1 puts the unity at the threshold, which fails).
    /// </summary>
    public static PeaceResult PeaceCase(
        int unityThresholdDelta = 0, int cityThresholdDelta = 0, int strengthDelta = 1, int chanceNumerator = 5)
    {
        var toy = Toy;
        var probe = Probe();
        var ruleset = PeaceRuleset(
            toy.Ruleset,
            probe.LoserUnity - 1 + unityThresholdDelta,
            probe.LoserCities - 1 + cityThresholdDelta,
            chanceNumerator == 5 ? toy.Ruleset.Combat.AutoPeaceChanceDenominator : chanceNumerator);
        var armies = WeakAttacker();
        armies.Add(ReserveWithPower("north", probe.WinnerPower + strengthDelta, ruleset));
        var world = toy.World with { StartingArmies = ValueList.From(armies) };
        var game = GameStateFactory.CreateInitial(world, ruleset, toy.Scenario);
        var battle = TacticalBattleSession.Open(game, "north-army-1", "south-army-1", ruleset, out var opened);
        battle.GeneralOn();
        return new PeaceResult(battle.Conclude(opened, ruleset));
    }

    /// <summary>A full session in which the human loses and the four peace conditions hold.</summary>
    public static GameSession PeaceSession()
    {
        var toy = Toy;
        var probeSession = ToySession(HumanBattleMode.Tactical, armies: WeakAttacker());
        var probe = PlayScripted(WeakAttacker(), LostBattleScript);
        var after = probe.Played.Session.State;
        var ruleset = PeaceRuleset(
            toy.Ruleset,
            after.NationById("north")!.Unity - 1,
            after.CountCitiesOwnedBy("north") - 1,
            toy.Ruleset.Combat.AutoPeaceChanceDenominator);
        var power = after.Armies.Where(a => a.Nation == "south").Sum(a => ArmyPower.Compute(a.Units, a.Morale, toy.Ruleset));
        var armies = WeakAttacker();
        armies.Add(ReserveWithPower("north", power + 1, ruleset));
        _ = probeSession;
        return ToySession(HumanBattleMode.Tactical, ruleset, armies);
    }
}
