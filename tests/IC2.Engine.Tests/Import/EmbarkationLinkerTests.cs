using IC2.Engine.Import;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Deterministic, file-free proof of <see cref="EmbarkationLinker"/> — every one of these runs on
/// every machine, including CI, no <c>assets.local.ini</c> needed.
/// </summary>
public class EmbarkationLinkerTests
{
    [Fact]
    public void A_fleet_carrying_a_surviving_army_links_both_ways()
    {
        var result = EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, 5) },
            liveArmyIndices: new HashSet<int> { 5 },
            tombstonedArmyIndices: new HashSet<int>(),
            documentPath: "test.sav");

        Assert.Equal(5, result.FleetCarriesArmyIndex[0]);
        Assert.Equal(0, result.ArmyCarriedByFleetIndex[5]);
    }

    [Fact]
    public void A_fleet_carrying_nothing_produces_no_link()
    {
        var result = EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, null) },
            liveArmyIndices: new HashSet<int>(),
            tombstonedArmyIndices: new HashSet<int>(),
            documentPath: "test.sav");

        Assert.Empty(result.FleetCarriesArmyIndex);
        Assert.Empty(result.ArmyCarriedByFleetIndex);
    }

    [Fact]
    public void A_fleet_naming_a_tombstoned_army_drops_the_link_instead_of_dangling()
    {
        // The delete-sweep hazard (build-process.md §4.2): army 5 was merged/eliminated and compacted
        // out of the import, so fleet 0's own stale claim must not survive as a reference to an army
        // this import never creates.
        var result = EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, 5) },
            liveArmyIndices: new HashSet<int>(),
            tombstonedArmyIndices: new HashSet<int> { 5 },
            documentPath: "test.sav");

        Assert.Empty(result.FleetCarriesArmyIndex);
        Assert.Empty(result.ArmyCarriedByFleetIndex);
    }

    [Fact]
    public void A_fleet_naming_an_army_that_is_neither_live_nor_tombstoned_throws()
    {
        // Review N1: previously this index was silently kept as a "plausible" link just because it was
        // not in the tombstoned set -- corrupt or out-of-range data (no corpus save has this shape)
        // must fail loudly instead.
        var ex = Assert.Throws<InvalidDataException>(() => EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, 5) },
            liveArmyIndices: new HashSet<int>(),
            tombstonedArmyIndices: new HashSet<int>(),
            documentPath: "test.sav"));

        Assert.Contains("fleet 0", ex.Message);
        Assert.Contains("army 5", ex.Message);
    }

    [Fact]
    public void Two_fleets_claiming_the_same_surviving_army_throws_naming_both()
    {
        var ex = Assert.Throws<InvalidDataException>(() => EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, 5), new EmbarkationLinker.FleetClaim(1, 5) },
            liveArmyIndices: new HashSet<int> { 5 },
            tombstonedArmyIndices: new HashSet<int>(),
            documentPath: "test.sav"));

        Assert.Contains("army 5", ex.Message);
        Assert.Contains("fleet 0", ex.Message);
        Assert.Contains("fleet 1", ex.Message);
    }

    [Fact]
    public void Two_fleets_claiming_the_same_now_tombstoned_army_does_not_throw()
    {
        // Both claims are dropped by the tombstone rule before the duplicate-claim check ever runs --
        // there is no real ambiguity left once neither claim points at a surviving army.
        var result = EmbarkationLinker.Resolve(
            new[] { new EmbarkationLinker.FleetClaim(0, 5), new EmbarkationLinker.FleetClaim(1, 5) },
            liveArmyIndices: new HashSet<int>(),
            tombstonedArmyIndices: new HashSet<int> { 5 },
            documentPath: "test.sav");

        Assert.Empty(result.FleetCarriesArmyIndex);
    }

    [Fact]
    public void Resolve_army_aboard_fleet_returns_the_carrying_fleets_id()
    {
        var links = new Dictionary<int, int> { [5] = 0 };

        var fleetId = EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5, isAboardFleet: true, links, fleetIndex => $"fleet-{fleetIndex}", "test.sav");

        Assert.Equal("fleet-0", fleetId);
    }

    [Fact]
    public void Resolve_army_not_aboard_returns_null_when_nothing_claims_it()
    {
        var fleetId = EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5, isAboardFleet: false, new Dictionary<int, int>(), fleetIndex => $"fleet-{fleetIndex}", "test.sav");

        Assert.Null(fleetId);
    }

    [Fact]
    public void An_army_claiming_aboard_with_no_carrying_fleet_throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5, isAboardFleet: true, new Dictionary<int, int>(), fleetIndex => $"fleet-{fleetIndex}", "test.sav"));

        Assert.Contains("army 5", ex.Message);
        Assert.Contains("aboard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_army_not_claiming_aboard_while_a_fleet_claims_it_throws()
    {
        var links = new Dictionary<int, int> { [5] = 2 };

        var ex = Assert.Throws<InvalidDataException>(() => EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5, isAboardFleet: false, links, fleetIndex => $"fleet-{fleetIndex}", "test.sav"));

        Assert.Contains("fleet 2", ex.Message);
        Assert.Contains("army 5", ex.Message);
    }
}
