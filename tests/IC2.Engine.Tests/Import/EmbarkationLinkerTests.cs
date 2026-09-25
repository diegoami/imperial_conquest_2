using System.Buffers.Binary;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Serialization;
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

    /// <summary>
    /// Follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/340">#340</see> N1: a
    /// fleet absorbed into another during the turn (bug #276) is tombstoned, but the army it was carrying
    /// can still survive. That army's own covered-cell sentinel still says "aboard" (the DAT is a
    /// snapshot; nothing rewrites it once the fleet is gone), yet no <em>surviving</em> fleet's claim
    /// names it, since the only fleet that ever did was compacted out of <c>SaveFleetTable.Fleets</c>
    /// entirely. Before this fix, that shape fell into the generic "no surviving fleet names it"
    /// <see cref="InvalidDataException"/> below (still asserted separately above) even though the cause
    /// is fully explained. With the tombstoned fleet's own claim passed in, the army is unlinked instead:
    /// <see cref="EmbarkationLinker.ResolveArmyAboardFleet"/> returns <see langword="null"/> exactly as
    /// it already does for an army that was never aboard anything
    /// (<see cref="Resolve_army_not_aboard_returns_null_when_nothing_claims_it"/>), which is what makes
    /// <c>OriginalSaveImporter</c>'s own <c>CoveredTileCode</c> assignment fall back to the army's own
    /// record instead of null -- the army keeps its position, it is just no longer marked as embarked.
    /// </summary>
    [Fact]
    public void An_army_aboard_only_a_now_tombstoned_fleet_is_unlinked_not_failed()
    {
        var noSurvivingClaims = new Dictionary<int, int>();
        var armiesClaimedByTombstonedFleets = new HashSet<int> { 5 };

        var fleetId = EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5,
            isAboardFleet: true,
            noSurvivingClaims,
            fleetIndex => $"fleet-{fleetIndex}",
            "test.sav",
            armiesClaimedByTombstonedFleets);

        Assert.Null(fleetId); // unlinked -- the caller then reads the army's own X/Y and CoveredCell.
    }

    /// <summary>
    /// The set is additive, never a blanket excuse: an army marked aboard that no fleet -- surviving or
    /// tombstoned -- ever claimed is still a genuine inconsistency, not a #340 N1 case.
    /// </summary>
    [Fact]
    public void An_army_aboard_that_no_fleet_at_all_claims_still_throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => EmbarkationLinker.ResolveArmyAboardFleet(
            armyIndex: 5,
            isAboardFleet: true,
            new Dictionary<int, int>(),
            fleetIndex => $"fleet-{fleetIndex}",
            "test.sav",
            armiesClaimedByTombstonedFleets: new HashSet<int> { 9 }));

        Assert.Contains("army 5", ex.Message);
        Assert.Contains("aboard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Owns amendment (PR #396, after PR #395): the amended Done-when 6 requires the unlink and the
    /// position to be shown through <see cref="OriginalSaveImporter"/> itself, not only through
    /// <see cref="EmbarkationLinker"/> directly (the tests above). This builds a minimal, synthetic
    /// SAV-shaped byte array against the real, committed <c>classical-mediterranean</c> world/ruleset
    /// (the only ones an original save is ever balanced against) -- structurally valid everywhere
    /// <c>OriginalSaveImporter.Import</c>'s own eight parsers and <see cref="GameDataValidation"/> read,
    /// but otherwise all zeros -- with exactly one army (index 0, marked aboard by its own covered-cell
    /// sentinel) and exactly one fleet (index 0, tombstoned: owner <c>0xFFFF</c>) whose
    /// <c>CarriedArmyIndex</c> still names that army. No corpus save has this shape (#340's own Done-when
    /// 6 bullet).
    /// </summary>
    [Fact]
    public void OriginalSaveImporter_unlinks_a_surviving_army_from_a_tombstoned_fleet_and_keeps_its_position()
    {
        var data = BuildMinimalSavWithATombstonedFleetCarryingASurvivingArmy();

        var result = OriginalSaveImporter.Import(
            data, "synthetic-tombstoned-fleet.sav (#340 N1, no corpus save has this shape)",
            RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "test-tombstoned-fleet-carry", saveLabel: "Test tombstoned-fleet carry");

        // Unlinked: no longer marked aboard.
        var army = Assert.Single(result.Save.State.Armies);
        Assert.Null(army.AboardFleetId);

        // Kept its position: the same X/Y the synthetic record wrote, untouched by the unlink.
        Assert.Equal(5, army.X);
        Assert.Equal(7, army.Y);

        // Review round 1, N1: the raw record's own CoveredCell is still AboardFleetSentinel (65535) --
        // nothing in the original ever had a reason to rewrite it once the carrying fleet was gone. The
        // imported army's CoveredTileCode must be the real terrain at (5, 7), not that stale sentinel.
        var terrain = RealGameData.World.Terrain.Decode(RealGameData.World.Width, RealGameData.World.Height);
        var expectedTerrain = terrain[(7 * RealGameData.World.Width) + 5];
        Assert.NotEqual(0xFFFF, army.CoveredTileCode);
        Assert.Equal(expectedTerrain, army.CoveredTileCode);

        // The tombstoned fleet never became a live FleetState -- it is reported as skipped instead.
        Assert.Empty(result.Save.State.Fleets);
        var skippedFleet = Assert.Single(result.Report.SkippedFleets);
        Assert.Equal(0, skippedFleet.Index);

        // GameDataValidation.Validate runs inside Import() itself (review N1's own belt-and-braces) --
        // reaching this line at all already proves the result round-trips through it without a dangling
        // reference; asserted again here so a future change to that internal call is still covered.
        GameDataValidation.Validate("synthetic-tombstoned-fleet.sav (#340 N1)", result.Save.State);
    }

    /// <summary>
    /// Builds a minimal SAV-shaped byte array that <c>OriginalSaveImporter.Import</c> accepts whole
    /// against <see cref="RealGameData.World"/> (16 nations, 334 cities) -- not
    /// <c>IC2.Data.Tests</c>'s own internal <c>SyntheticSaveBuilder</c> (a different assembly, and
    /// scoped to <c>SaveArmyTable</c>/<c>SaveFleetTable</c> alone, never a full import). Every region
    /// this method does not explicitly write is left at its zero-filled default, which every one of
    /// <c>OriginalSaveImporter.Import</c>'s eight parsers accepts as structurally valid on its own
    /// (mirroring <c>SyntheticSaveBuilder</c>'s own documented reasoning for the two tables it covers):
    /// zero coordinates, zero owner/allegiance (nation 0), zero relation cells (peace, symmetric, zero
    /// diagonal), zero-amount recruitment/mercenary slots (skipped by their own parsers), an empty
    /// (<c>-1</c>) news log, and a capital sentinel of <c>0xFFFF</c> on every nation (so none claims a
    /// city it does not own). The only content written is: 334 one-letter city names (
    /// <see cref="WorldPrefix.Parse"/> rejects an empty name), the 16 real nation names in
    /// <see cref="NationCatalog"/>'s own order plus a one-letter leader each (<c>SaveNationTable.Parse</c>
    /// rejects an empty name or an empty leader field), a turn order that is the identity permutation
    /// <c>0..15</c> with nation 0 active (<c>SaveTurnState.Parse</c> requires a permutation), a "no
    /// offer pending" sentinel, and the one army and one fleet this test cares about.
    /// </summary>
    private static byte[] BuildMinimalSavWithATombstonedFleetCarryingASurvivingArmy()
    {
        // Review round 1, N3: public IC2.Data constants instead of hand-typed literals, wherever one
        // exists. The SAV nation record length (1172), the 55-byte trailer and the per-field offsets
        // within a record have no public constant (SaveNationLayout.NationRecordLength is internal to
        // IC2.Data), so those stay literal, cited inline.
        const int nationCount = 16;
        const int savNationRecordLength = 1172;
        const int trailerLength = 55;
        var mercenaryTableLength = SaveMercenaryTable.RecordCount * SaveMercenaryTable.RecordLength;

        var mapAndCityLength = WorldPrefix.SharedPrefixLength; // 89,600 map + 334 city records.
        var totalLength = mapAndCityLength
            + 2 + 1 * SaveArmyTable.RecordLength // army count word + 1 army record
            + 2 + 1 * SaveFleetTable.RecordLength // fleet count word + 1 fleet record
            + nationCount * savNationRecordLength
            + mercenaryTableLength
            + 2 // news log's own newsIndex field
            + 0 // (newsIndex + 1) slots -- newsIndex -1, an empty log, needs none
            + trailerLength;

        var data = new byte[totalLength];

        // ---- Map + city table: WorldPrefix.Parse requires every one of the 334 city records to carry
        // a non-empty, NUL-terminated ASCII name in its first 14 bytes; coordinates (0, 0) and every
        // other field default to zero, all within WorldPrefix.Parse's own accepted range.
        for (var i = 0; i < WorldPrefix.CityCount; i++)
        {
            data[WorldPrefix.CityStart + i * WorldPrefix.CityRecordLength] = (byte)'C';
        }

        // ---- Army table: one army (index 0), owned by nation 0, at (5, 7), marked aboard a fleet by
        // its own covered-cell sentinel (ArmyRecord.CoveredCell == ArmyRecord.AboardFleetSentinel).
        var armyTableStart = mapAndCityLength + 2;
        WriteUInt16(data, mapAndCityLength, 1); // army count
        WriteUInt16(data, armyTableStart + 0, 5); // X
        WriteUInt16(data, armyTableStart + 2, 7); // Y
        WriteUInt16(data, armyTableStart + 4, 0); // Owner (nation 0)
        WriteUInt16(data, armyTableStart + 8, ArmyRecord.AboardFleetSentinel); // CoveredCell

        // ---- Fleet table: one fleet (index 0), tombstoned (owner 0xFFFF), still naming army 0 as its
        // own CarriedArmyIndex (+22) -- the #340 N1 shape: the carrier is gone, the cargo survived.
        var fleetCountOffset = armyTableStart + SaveArmyTable.RecordLength;
        var fleetTableStart = fleetCountOffset + 2;
        WriteUInt16(data, fleetCountOffset, 1); // fleet count
        WriteUInt16(data, fleetTableStart + 8, SaveFleetTable.TombstoneOwnerSentinel); // Owner
        WriteUInt16(data, fleetTableStart + 22, 0); // CarriedArmyIndex = army 0

        // ---- Nation table: 16 records. Name must equal NationCatalog.Name(i) exactly; the leader
        // field (+11, 27 bytes) must hold at least one non-NUL byte; the capital is left unset (the
        // sentinel) so no nation claims a city it does not own; relations, recruitment, wealth and
        // every other field default to zero, all within SaveNationTable.Parse's own accepted range
        // (a symmetric, zero-diagonal, all-peace relation matrix).
        var nationTableStart = fleetTableStart + SaveFleetTable.RecordLength;
        for (var i = 0; i < nationCount; i++)
        {
            var offset = nationTableStart + i * savNationRecordLength;
            WriteAscii(data, offset, NationCatalog.Name((ushort)i));
            data[offset + 11] = (byte)'L'; // leader: one non-NUL byte is enough
            WriteUInt16(data, offset + 0x444, SaveNationTable.NoCapitalSentinel); // capitalCity: none
        }

        // ---- Mercenary pool: 50 all-zero records -- every one reads as empty (zero troops), so
        // SaveMercenaryTable.Parse's own loop never inspects a field this test hasn't set.
        var mercenaryTableStart = nationTableStart + nationCount * savNationRecordLength;

        // ---- News log: newsIndex = -1, the documented "empty log" sentinel -- zero slots follow.
        var newsIndexOffset = mercenaryTableStart + mercenaryTableLength;
        WriteInt16(data, newsIndexOffset, -1);

        // ---- 55-byte trailer: the turn order must be a permutation of 0..15 (the identity here),
        // nation 0 is active at index 0, and no diplomatic offer is pending.
        var trailerStart = data.Length - trailerLength;
        for (ushort i = 0; i < nationCount; i++)
        {
            WriteUInt16(data, trailerStart + i * 2, i); // turn order
        }

        WriteUInt16(data, trailerStart + 32, SavePendingOffer.NoOfferSentinel); // pending offer: none
        WriteUInt16(data, trailerStart + 36, 0); // current nation
        WriteUInt16(data, trailerStart + 38, 0); // turn-order index
        WriteUInt16(data, trailerStart + 40, 1); // week (1..13)
        WriteUInt16(data, trailerStart + 42, 1); // year BC (1..1000)
        WriteUInt16(data, trailerStart + 44, 0); // season (0..3)

        return data;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static void WriteInt16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static void WriteAscii(byte[] data, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            data[offset + i] = (byte)text[i];
        }
    }
}
