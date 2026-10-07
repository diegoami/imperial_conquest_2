using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Fix #710: the session's <c>transfer-money</c> reply must tell the player what actually moved — the
/// amount, the from and to purses, and a clamp (including a transfer that moved nothing). Before the fix
/// the session printed only the generic <c>"economy.transfer-money accepted."</c> line, so a transfer
/// clamped to the source's own balance read exactly like a full one.
/// </summary>
/// <remarks>
/// <see cref="GameSession"/> has no seam to inject a <see cref="GameState"/> directly (its setter is
/// private), so each test customizes the shipped toy world's starting armies/fleets/nations and drives the
/// real session through <see cref="GameSession.Submit"/> — the same path the Godot dialog and the CLI use.
/// The bug's own case (an own <c>via</c> fleet holding nothing) is the first test.
/// </remarks>
public sealed class TransferMoneyReplyTests
{
    private const string ArmyId = "north-army-1";
    private const string ViaFleetId = "north-fleet-1";

    /// <summary>The generic engine line the bug reported as the whole reply; the fix must drop it.</summary>
    private const string RawLine = "economy.transfer-money accepted.";

    /// <summary>The bug's own case: an own fleet as <c>via</c> holding 0 talents, pressing +10.</summary>
    [Fact]
    public void ViaFleetHoldingNothing_ReplyNamesTheZeroMovedAndTheSource()
    {
        var session = SessionWithViaFleet(y: 3, money: 0);
        var armyMoneyBefore = session.State.ArmyById(ArmyId)!.Money;
        Assert.Equal(0, session.State.FleetById(ViaFleetId)!.Money);

        var output = session.Submit($"transfer-money {ArmyId} 10 via {ViaFleetId}");

        Assert.Contains(
            $"{ArmyId} received 0 of 10 requested talents from fleet {ViaFleetId} "
            + $"(fleet {ViaFleetId} held 0).",
            output.Lines);
        Assert.DoesNotContain(RawLine, string.Join("\n", output.Lines));
        Assert.Equal(armyMoneyBefore, session.State.ArmyById(ArmyId)!.Money);
        Assert.Equal(0, session.State.FleetById(ViaFleetId)!.Money);
    }

    /// <summary>The treasury side of the same clamp: a nation holding 0 moves nothing and says so.</summary>
    [Fact]
    public void TreasuryHoldingNothing_ReplyNamesTheZeroMovedAndTheTreasury()
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            Nations = Replace(
                toy.World.Nations,
                nation => nation.Id == "north" ? nation with { Treasury = 0 } : nation),
        };

        var session = new GameSession(world, toy.Ruleset, toy.Scenario);
        var armyMoneyBefore = session.State.ArmyById(ArmyId)!.Money;

        var output = session.Submit($"transfer-money {ArmyId} 10");

        Assert.Contains(
            $"{ArmyId} received 0 of 10 requested talents from the treasury (the treasury held 0).",
            output.Lines);
        Assert.DoesNotContain(RawLine, string.Join("\n", output.Lines));
        Assert.Equal(armyMoneyBefore, session.State.ArmyById(ArmyId)!.Money);
        Assert.Equal(0, session.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// An unclamped transfer names the source and the destination in both directions — into the army's
    /// purse, and back out of it into the treasury.
    /// </summary>
    [Fact]
    public void UnclampedTransfers_NameSourceAndDestinationInBothDirections()
    {
        var session = new GameSession(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario);
        var armyMoneyBefore = session.State.ArmyById(ArmyId)!.Money;
        var treasuryBefore = session.State.NationById("north")!.Treasury;

        var intoPurse = session.Submit($"transfer-money {ArmyId} 100");
        Assert.Contains($"{ArmyId} received 100 talents from the treasury.", intoPurse.Lines);
        Assert.DoesNotContain(RawLine, string.Join("\n", intoPurse.Lines));
        Assert.Equal(armyMoneyBefore + 100, session.State.ArmyById(ArmyId)!.Money);
        Assert.Equal(treasuryBefore - 100, session.State.NationById("north")!.Treasury);

        var back = session.Submit($"transfer-money {ArmyId} -50");
        Assert.Contains($"the treasury received 50 talents from {ArmyId}.", back.Lines);
        Assert.DoesNotContain(RawLine, string.Join("\n", back.Lines));
        Assert.Equal(armyMoneyBefore + 50, session.State.ArmyById(ArmyId)!.Money);
        Assert.Equal(treasuryBefore - 50, session.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// Pins the clamp wording when the <em>receiving</em> purse is at its cap rather than the source
    /// short: the army sends 100 to a via fleet already holding the cap less 50, so only 50 fits and the
    /// reply names the cap, never the source's own balance.
    /// </summary>
    [Fact]
    public void ReceivingPurseAtItsCap_ReplyNamesTheCapAsTheReason()
    {
        var cap = CoreTestbed.Toy.Ruleset.Economy.PurseCapPerUnit;
        var session = SessionWithViaFleet(y: 3, money: cap - 50);

        var output = session.Submit($"transfer-money {ArmyId} -100 via {ViaFleetId}");

        Assert.Contains(
            $"fleet {ViaFleetId} received 50 of 100 requested talents from {ArmyId} "
            + $"(fleet {ViaFleetId} is at its {cap}-talent cap).",
            output.Lines);
        Assert.DoesNotContain(RawLine, string.Join("\n", output.Lines));
    }

    /// <summary>
    /// The screen's bottom status line rewrites only the engine's generic <c>"accepted."</c> lines (T147's
    /// <see cref="CommandOutcomeWording"/>); the new reply is not one, so it reaches that line verbatim
    /// instead of being replaced by the old per-kind fallback. Pinned so a later change cannot silently
    /// put a placeholder back over the amount.
    /// </summary>
    [Fact]
    public void TheScreenStatusLine_ShowsTheRichReplyVerbatim()
    {
        var reply = $"{ArmyId} received 0 of 10 requested talents from fleet {ViaFleetId} "
            + $"(fleet {ViaFleetId} held 0).";

        Assert.Equal(reply, CommandOutcomeWording.ReadableLine(reply));

        // The old per-kind fallback still exists for the engine's generic line; it is simply no longer
        // what the session prints for a transfer.
        Assert.Equal("Money transferred.", CommandOutcomeWording.ReadableLine(RawLine));
    }

    private static GameSession SessionWithViaFleet(int y, int money)
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            StartingFleets = Replace(
                toy.World.StartingFleets,
                fleet => fleet.Id == ViaFleetId ? fleet with { X = 3, Y = y, Money = money } : fleet),
        };

        return new GameSession(world, toy.Ruleset, toy.Scenario);
    }

    private static ValueList<T> Replace<T>(ValueList<T> list, Func<T, T> replace) =>
        ValueList.From(list.Select(replace));
}
