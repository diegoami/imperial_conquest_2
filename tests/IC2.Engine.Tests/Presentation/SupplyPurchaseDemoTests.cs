using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// The rule T10's Done-when 1 used to pin through the CLI demo's golden transcript: a supply purchase at
/// an <strong>owned</strong> city is free, and one at a <strong>foreign</strong> city is paid — asserted
/// through the Presentation session a player actually goes through, with the exact talents named.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this test exists at all.</strong> T22 registers an AI into <see cref="Core.TurnPhase"/>'s
/// <c>Orders</c> phase, and the demo's <c>south</c> seat is AI-controlled at <c>aggression: 0.8</c>.
/// <c>demo.txt</c> walks <c>north-army-1</c> to <c>(4,3)</c>, one tile from <c>south-army-1</c>; the AI
/// attacks on its first turn and wins on the engine's own arithmetic, so the script's two <c>buy</c>
/// lines now address an army that no longer exists. The demonstration moved here rather than being
/// patched back into the fixture, because no arrangement of that script can hold both: buying before the
/// army's stock drains admits 1 ton and costs 0 talents, and buying after it drains needs an army the AI
/// has already destroyed. See <c>docs/task-catalogue.md</c> T10 and this task's PR.
/// </para>
/// <para>
/// <strong>Why every seat is human here.</strong> Not to dodge the AI, but because the rule under test is
/// the supply seam and the AI is not part of it. Leaving an AI seat in would make this fixture breakable
/// by a future change to an AI scoring weight — which is exactly how the demo came to stop demonstrating
/// the rule in the first place, and the mistake worth not repeating. Everything else is the committed
/// <c>toy-3city</c> scenario, world, ruleset and seed: only <see cref="Seat.Control"/> differs.
/// </para>
/// <para>
/// <strong>Why the script ends twelve turns.</strong> The purchase has to be <em>substantive</em> — a
/// degenerate one proves nothing. <c>north-army-1</c> starts holding 185 tons against a dialog capacity of
/// 186, so it has room for exactly one ton; six rounds of
/// <see cref="Economy.SupplyConsumption"/> empty it, and only then can a real 50 tons move at a real
/// price. Twelve <c>end</c>s make six rounds because both seats are human and each <c>end</c> advances one
/// seat — the demo needed six because its AI seat played itself.
/// <see cref="The_two_purchases_are_substantive_and_not_degenerate"/> asserts the quantities rather than
/// leaving that reasoning in a comment.
/// </para>
/// </remarks>
public sealed class SupplyPurchaseDemoTests
{
    private readonly ITestOutputHelper _output;

    public SupplyPurchaseDemoTests(ITestOutputHelper output) => _output = output;

    /// <summary>The army's own city, one tile from it at <c>(4,3)</c>: the free path.</summary>
    private const string OwnedCityId = "portus";

    /// <summary>A city belonging to the other nation, also one tile away: the paid path.</summary>
    private const string ForeignCityId = "meridia";

    private const string ArmyId = "north-army-1";
    private const string BuyerNationId = "north";
    private const string SellerNationId = "south";

    /// <summary>The tons each purchase asks for. Well under the army's drained capacity, so both are admitted in full.</summary>
    private const int RequestedTons = 50;

    private static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    [Fact]
    public void A_purchase_at_an_owned_city_is_free()
    {
        var run = RunTheTwoPurchases();

        Assert.Equal(
            $"{ArmyId} bought {RequestedTons} tons of supply at {OwnedCityId}, free at your own city.",
            run.FreeLine);

        // Free means nobody paid: neither the army's own purse nor its nation's treasury moved, on a
        // turn where the supply itself certainly did.
        Assert.Equal(run.Before.ArmyMoney, run.AfterFree.ArmyMoney);
        Assert.Equal(run.Before.BuyerTreasury, run.AfterFree.BuyerTreasury);

        // ...and the tons really moved, from that city to that army.
        Assert.Equal(run.Before.ArmySupply + RequestedTons, run.AfterFree.ArmySupply);
        Assert.Equal(run.Before.OwnedCitySupply - RequestedTons, run.AfterFree.OwnedCitySupply);
    }

    [Fact]
    public void A_purchase_at_a_foreign_city_costs_exactly_the_rulesets_rate()
    {
        var run = RunTheTwoPurchases();

        // The exact price, stated twice: as the number, and as the ruleset expression that produces it,
        // so a change to supplyTonsPerTalent fails the second and not silently only the first.
        const int expectedTalents = 10;
        Assert.Equal(expectedTalents, RequestedTons / Ruleset.Economy.SupplyTonsPerTalent);

        Assert.Equal(
            $"{ArmyId} bought {RequestedTons} tons of supply at {ForeignCityId}, costing {expectedTalents} talents.",
            run.PaidLine);

        // The buyer paid it out of the army's own purse -- the toy ruleset runs perUnitPurses -- and the
        // seller's treasury gained exactly the same amount. Money is conserved, not invented.
        Assert.Equal(run.AfterFree.ArmyMoney - expectedTalents, run.AfterPaid.ArmyMoney);
        Assert.Equal(run.AfterFree.SellerTreasury + expectedTalents, run.AfterPaid.SellerTreasury);

        Assert.Equal(run.AfterFree.ArmySupply + RequestedTons, run.AfterPaid.ArmySupply);
        Assert.Equal(run.AfterFree.ForeignCitySupply - RequestedTons, run.AfterPaid.ForeignCitySupply);
    }

    /// <summary>
    /// The guard against the failure mode that made relocating this test necessary. A purchase of one ton
    /// for free and zero tons for zero talents satisfies the words of T10's Done-when 1 and proves
    /// nothing at all; this asserts both purchases move a real quantity and the paid one costs a real
    /// price.
    /// </summary>
    [Fact]
    public void The_two_purchases_are_substantive_and_not_degenerate()
    {
        var run = RunTheTwoPurchases();

        _output.WriteLine(run.FreeLine);
        _output.WriteLine(run.PaidLine);

        // The army really was empty first, so there was room for a full purchase rather than a token one.
        Assert.Equal(0, run.Before.ArmySupply);
        Assert.Equal(RequestedTons * 2, run.AfterPaid.ArmySupply);

        Assert.True(
            run.AfterPaid.ArmyMoney < run.AfterFree.ArmyMoney,
            "the foreign purchase must actually cost the buyer something");
        Assert.True(
            run.AfterPaid.SellerTreasury > run.AfterFree.SellerTreasury,
            "the foreign purchase must actually pay the seller something");
    }

    /// <summary>
    /// The free and the paid path must be distinguishable, and this is where that is asserted as a
    /// difference rather than as two separate facts: the same army, the same tons, the same turn, two
    /// cities, and only the owner differs.
    /// </summary>
    [Fact]
    public void The_only_difference_between_the_two_purchases_is_who_owns_the_city()
    {
        var run = RunTheTwoPurchases();

        var owned = CoreTestbed.Toy.World.CityById(OwnedCityId)!;
        var foreign = CoreTestbed.Toy.World.CityById(ForeignCityId)!;
        Assert.Equal(BuyerNationId, owned.Owner);
        Assert.Equal(SellerNationId, foreign.Owner);

        // Same quantity admitted on both paths, so the price difference cannot be explained by the tons.
        Assert.Equal(
            run.AfterFree.ArmySupply - run.Before.ArmySupply,
            run.AfterPaid.ArmySupply - run.AfterFree.ArmySupply);

        // One was free and one was not.
        Assert.Contains("free at your own city", run.FreeLine, StringComparison.Ordinal);
        Assert.DoesNotContain("free", run.PaidLine, StringComparison.Ordinal);
        Assert.Contains("costing", run.PaidLine, StringComparison.Ordinal);
    }

    /// <summary>The numbers this test reads, at one moment.</summary>
    private sealed record Snapshot(
        int ArmySupply,
        int ArmyMoney,
        int BuyerTreasury,
        int SellerTreasury,
        int OwnedCitySupply,
        int ForeignCitySupply)
    {
        public static Snapshot Of(GameState state) => new(
            state.ArmyById(ArmyId)!.SupplyTons,
            state.ArmyById(ArmyId)!.Money,
            state.NationById(BuyerNationId)!.Treasury,
            state.NationById(SellerNationId)!.Treasury,
            state.CityById(OwnedCityId)!.SupplyTons,
            state.CityById(ForeignCityId)!.SupplyTons);
    }

    private sealed record Run(
        Snapshot Before, Snapshot AfterFree, Snapshot AfterPaid, string FreeLine, string PaidLine);

    /// <summary>
    /// Drives the session: march the army between the two cities, play six rounds so its supply drains,
    /// then buy at each city, snapshotting the state either side of each purchase.
    /// </summary>
    private static Run RunTheTwoPurchases()
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, AllSeatsHuman(toy.Scenario));

        // (4,3) is the one tile adjacent to both cities: Chebyshev 1 from portus (5,2) and from meridia
        // (3,4). The merged supply gate refuses a provider further than one tile away.
        Submit(session, $"move {ArmyId} 4 3");
        for (var i = 0; i < 12; i++)
        {
            Submit(session, "end");
        }

        var before = Snapshot.Of(session.State);
        var freeLine = PurchaseLine(Submit(session, $"buy {ArmyId} {OwnedCityId} {RequestedTons}"));
        var afterFree = Snapshot.Of(session.State);
        var paidLine = PurchaseLine(Submit(session, $"buy {ArmyId} {ForeignCityId} {RequestedTons}"));
        var afterPaid = Snapshot.Of(session.State);

        return new Run(before, afterFree, afterPaid, freeLine, paidLine);
    }

    /// <summary>
    /// The committed toy scenario with every seat human. Only <see cref="Seat.Control"/> and the now-unused
    /// personality change; the world, the ruleset, the victory condition and the seed are the shipped ones.
    /// </summary>
    private static Scenario AllSeatsHuman(Scenario scenario) =>
        scenario with
        {
            Seats = ValueList.From(
                scenario.Seats.Select(seat => seat with { Control = SeatControl.Human, Personality = null })),
        };

    private static SessionOutput Submit(GameSession session, string line) => session.Submit(line);

    /// <summary>
    /// The session's own rendered answer to a <c>buy</c>. Fails loudly rather than returning an empty
    /// string when the purchase was refused, so a broken fixture reports the refusal instead of an
    /// unexplained assertion mismatch.
    /// </summary>
    private static string PurchaseLine(SessionOutput output)
    {
        foreach (var line in output.Lines)
        {
            if (line.Contains("bought", StringComparison.Ordinal))
            {
                return line;
            }
        }

        Assert.Fail(
            "The session refused the purchase instead of making it: "
            + string.Join(" | ", output.Lines));
        return string.Empty;
    }
}
