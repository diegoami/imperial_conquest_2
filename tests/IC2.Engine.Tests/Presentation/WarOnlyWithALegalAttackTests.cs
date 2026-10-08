using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T135 (bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/579">#579</see>): an
/// attack that the engine refuses declares no war. Before this task the session composed
/// <c>diplomacy.declare-war</c> ahead of an attack, committed its state and news, and only then ran the
/// attack's own legality check — so a siege on a non-adjacent city, an army that is not adjacent, or a
/// fleet docked at its own city left the war (and the one-step cascade to the target's allies) in place.
/// The rule under test is the one the original's decompiled order supports: the declaration and the
/// attack commit together or not at all, so a refused attack leaves the state exactly as the order found
/// it — relations, news log, random seed and every other field — and reports its own rejection line
/// alone. An <em>accepted</em> attack is unchanged: the same declaration line, relation writes, cascade
/// and news.
/// </summary>
/// <remarks>
/// Everything here drives the real <see cref="GameSession.Submit"/> over the shipped
/// <c>classical-mediterranean</c> pair as Rome (the CLI's <c>--seat rome</c> shape), the same seam the
/// CLI and the Godot seat use. The two refused cases that need no setup (<c>besiege-city army-0
/// misurata</c>, <c>attack-army army-0 army-2</c>) are refused because their targets are far away; the
/// fleet case and the accepted case need a target placed beside an army/fleet, so their worlds are
/// built from the shipped one with the one placement changed, exactly as
/// <c>GameSessionBattleResultsTests.BattleFixture</c> does.
/// </remarks>
public sealed class WarOnlyWithALegalAttackTests
{
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";
    private const string NumidiaId = "numidia";

    private const string CarthageArmyId = "army-2";
    private const string CarthageFleetId = "fleet-0";
    private const string RomeFleetId = "rome-fleet-test";

    /// <summary>Carthage's <c>akra-leuke</c>, at (47, 63) — the harbour the fleet case docks at.</summary>
    private const int HarbourX = 47;
    private const int HarbourY = 63;

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    /// <summary>A Rome-seated session over the shipped classical pair, the CLI's <c>--seat rome</c> shape.</summary>
    private static GameSession RomeSession(World? world = null, ulong seed = 1)
    {
        var classical = Classical();
        return new GameSession(
            world ?? classical.World,
            classical.Ruleset,
            classical.Scenario,
            seedOverride: seed,
            humanSeatNationId: RomeId);
    }

    /// <summary>
    /// The live state as the save format serialises it — DoD 1's "the state before the order" comparison.
    /// Through <see cref="SaveManager.Serialize"/>, not a record comparison, so a field the engine's own
    /// value equality happens to ignore (or a nested list whose order changed) still shows up.
    /// </summary>
    private static string Serialized(GameSession session) =>
        SaveManager.Serialize(new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "t135-state-probe",
            Label: "T135 state probe",
            ScenarioId: session.State.ScenarioId,
            WorldId: session.State.WorldId,
            RulesetId: session.State.RulesetId,
            State: session.State));

    /// <summary>The one non-blank line after <c>Submit</c>'s own echo, or a failure naming what was there instead.</summary>
    private static string SingleLineAfterEcho(SessionOutput output)
    {
        var echoIndex = -1;
        for (var i = 0; i < output.Lines.Count; i++)
        {
            if (output.Lines[i].StartsWith("> ", StringComparison.Ordinal))
            {
                echoIndex = i;
                break;
            }
        }

        Assert.True(echoIndex >= 0, "Submit's output carries its own echo line");
        var after = output.Lines.Skip(echoIndex + 1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        return Assert.Single(after);
    }

    /// <summary>The index of <paramref name="line"/> in <paramref name="lines"/>, or -1.</summary>
    private static int IndexOf(IReadOnlyList<string> lines, string line)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i], line, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// DoD 1: a siege Rome is not adjacent to is refused after the declaration would have been composed,
    /// and the declaration is not committed — exactly one line after the echo, no declaration line, Rome
    /// and Carthage still at peace, and the whole state byte-identical to the state before the order.
    /// </summary>
    [Fact]
    public void A_refused_siege_commits_none_of_the_declaration()
    {
        var session = RomeSession();
        var before = Serialized(session);
        Assert.Equal(0, session.State.Relations.Get(RomeId, CarthageId));

        var output = session.Submit("besiege-city army-0 misurata");

        var line = SingleLineAfterEcho(output);
        Assert.StartsWith("battle.besiege-city rejected (battle.siege-not-adjacent): ", line, StringComparison.Ordinal);
        Assert.Contains("is not adjacent to 'misurata'", line, StringComparison.Ordinal);
        Assert.DoesNotContain(
            output.Lines, l => l.Contains("diplomacy.declare-war", StringComparison.Ordinal));

        Assert.Equal(0, session.State.Relations.Get(RomeId, CarthageId));
        Assert.Equal(before, Serialized(session));
    }

    /// <summary>
    /// DoD 2, army half: an adjacent-order attack whose target is four tiles away is refused after the
    /// declaration would have been composed, and commits none of it.
    /// </summary>
    [Fact]
    public void A_refused_army_attack_commits_none_of_the_declaration()
    {
        var session = RomeSession();
        var before = Serialized(session);

        var output = session.Submit("attack-army army-0 army-2");

        var line = SingleLineAfterEcho(output);
        Assert.StartsWith("battle.attack-army rejected (battle.not-adjacent): ", line, StringComparison.Ordinal);
        Assert.Contains("is not adjacent to 'army-2'", line, StringComparison.Ordinal);
        Assert.DoesNotContain(
            output.Lines, l => l.Contains("diplomacy.declare-war", StringComparison.Ordinal));

        Assert.Equal(0, session.State.Relations.Get(RomeId, CarthageId));
        Assert.Equal(before, Serialized(session));
    }

    /// <summary>
    /// DoD 2, fleet half: a foreign fleet the original refuses <em>before any prompt</em> — one docked at
    /// its own city, a city of its owner anywhere in the 3 × 3 around it (<c>AttackLegality.DockedAtOwnCity</c>,
    /// the test the 2026-10-05 report reads from <c>FUN_004494E4</c>). The scripted world puts Rome's fleet
    /// one tile from Carthage's, which sits beside Carthage's own <c>akra-leuke</c>; Rome and Carthage are
    /// at peace and Carthage's ally Numidia is too. The refusal must leave all three relations at peace, so
    /// the declaration's one-step cascade does not survive either.
    /// </summary>
    [Fact]
    public void A_refused_fleet_attack_commits_neither_the_declaration_nor_its_ally_cascade()
    {
        var classical = Classical();
        var carthageFleet = classical.World.StartingFleets.Single(f => f.Id == CarthageFleetId)
            with { X = 48, Y = HarbourY };
        var romeFleet = new StartingFleet(
            RomeFleetId, RomeId, X: 49, Y: HarbourY, Ships: 40, ConditionPercent: 100,
            Money: 0, SupplyTons: 80, Moves: 25);
        var world = classical.World with
        {
            StartingFleets = ValueList.From(
                classical.World.StartingFleets.Select(f => f.Id == CarthageFleetId ? carthageFleet : f)
                    .Append(romeFleet)),
        };

        var session = RomeSession(world);
        var before = Serialized(session);

        // The setup is real: Carthage's fleet really is beside its own city, and Numidia really is
        // Carthage's ally at peace with Rome (so the cascade this test rules out really would fire).
        Assert.Equal(2, session.State.Relations.Get(CarthageId, NumidiaId));
        Assert.Equal(0, session.State.Relations.Get(RomeId, NumidiaId));
        Assert.Contains(
            session.State.Cities,
            c => c.Owner == CarthageId && c.X == HarbourX && c.Y == HarbourY);

        var output = session.Submit($"attack-fleet {RomeFleetId} {CarthageFleetId}");

        var line = SingleLineAfterEcho(output);
        Assert.StartsWith(
            "battle.attack-fleet rejected (battle.target-docked-at-own-city): ", line, StringComparison.Ordinal);
        Assert.Contains("docked at its own city 'akra-leuke'", line, StringComparison.Ordinal);
        Assert.DoesNotContain(
            output.Lines, l => l.Contains("diplomacy.declare-war", StringComparison.Ordinal));

        Assert.Equal(0, session.State.Relations.Get(RomeId, CarthageId));
        Assert.Equal(0, session.State.Relations.Get(RomeId, NumidiaId));
        Assert.Equal(before, Serialized(session));
    }

    /// <summary>
    /// DoD 3: the accepted path is unchanged. Rome's army-0 is placed beside Carthage's army-2 in a
    /// scripted world, at peace; the order declares war, wins the field battle and prints the
    /// declaration line before the attack's own acceptance. The relation becomes war both ways with the
    /// one-step cascade to Carthage's ally Numidia, and the declaration's news precedes the battle's.
    /// </summary>
    [Fact]
    public void An_accepted_army_attack_at_peace_still_declares_war_first()
    {
        var classical = Classical();
        var carthageArmy = classical.World.StartingArmies.Single(a => a.Id == CarthageArmyId)
            with { X = 99, Y = 37 };
        var world = classical.World with
        {
            StartingArmies = ValueList.From(
                classical.World.StartingArmies.Select(a => a.Id == CarthageArmyId ? carthageArmy : a)),
        };

        var session = RomeSession(world);
        var war = classical.Ruleset.Diplomacy.StateCodes.War;

        var output = session.Submit("attack-army army-0 army-2");

        Assert.Contains(
            "diplomacy.declare-war accepted (composed ahead of the attack).", output.Lines, StringComparer.Ordinal);
        Assert.Contains("battle.attack-army accepted.", output.Lines, StringComparer.Ordinal);
        Assert.True(
            IndexOf(output.Lines, "diplomacy.declare-war accepted (composed ahead of the attack).")
            < IndexOf(output.Lines, "battle.attack-army accepted."),
            "the declaration line comes before the attack's own acceptance");

        Assert.Equal(war, session.State.Relations.Get(RomeId, CarthageId));
        Assert.Equal(war, session.State.Relations.Get(RomeId, NumidiaId));

        var news = session.State.NewsLog.Slots.Select(slot => slot.Text).ToList();
        var declarationIndex = news.FindIndex(
            text => text.Contains("ROME DECLARES WAR ON CARTHAGE", StringComparison.Ordinal));
        Assert.True(declarationIndex >= 0, $"the news log carries the declaration (got: {string.Join(" | ", news)})");

        // The classical scenario ships its own starting news, which already carries an old "destroys army
        // of" line; this order's own battle entry is the one after the declaration it just wrote.
        var battleIndex = news.FindIndex(
            declarationIndex + 1, text => text.Contains("destroys army of", StringComparison.Ordinal));
        Assert.True(
            battleIndex > declarationIndex,
            $"the battle's news follows the declaration's (declaration={declarationIndex}, "
            + $"battle={battleIndex}, news: {string.Join(" | ", news)})");
    }
}
