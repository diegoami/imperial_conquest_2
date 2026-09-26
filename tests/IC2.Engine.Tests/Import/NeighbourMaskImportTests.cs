using System.Buffers.Binary;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// T86 Done-when 3's own bullet: "An imported original save seeds it from the save's own masks
/// (<c>+0x46</c>), not from the world, since a save may carry masks already merged by conquest." This
/// builds a synthetic, structurally-minimal SAV byte array (no original file needed, and no army/fleet
/// content — <see cref="EmbarkationLinkerTests"/>'s own <c>BuildMinimalSavWithATombstonedFleetCarryingASurvivingArmy</c>
/// is the model this follows, simplified to zero armies and zero fleets since this test needs neither)
/// whose neighbour mask differs from <see cref="RealGameData.World"/>'s own <c>startingNeighbours</c> by
/// one merged pair — Rome (nation 0) and Media (nation 14), which the DAT's own mask does not carry
/// (<c>dat-neighbour-mask.md</c> §2's 24 pairs) — and shows the imported <see cref="GameState.Neighbours"/>
/// carries the save's own mask exactly, not the world's.
/// </summary>
public class NeighbourMaskImportTests
{
    [Fact]
    public void An_imported_saves_neighbour_set_comes_from_its_own_masks_not_the_world()
    {
        var data = BuildMinimalSavWithAMergedRomeMediaNeighbourPair();

        var result = OriginalSaveImporter.Import(
            data, "synthetic-neighbour-mask.sav (T86 Done-when 3, no corpus save has this shape)",
            RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "test-neighbour-mask", saveLabel: "Test neighbour mask");

        var imported = result.Save.State.Neighbours;
        Assert.NotNull(imported);

        var romeId = RealGameData.World.Nations[0].Id;
        var mediaId = RealGameData.World.Nations[14].Id;

        // The synthetic save's own mask: Rome <-> Media, both ways, and nothing else.
        var romeEntry = Assert.Single(imported!, e => e.NationId == romeId);
        Assert.Equal(new[] { mediaId }, romeEntry.NeighbourIds);
        var mediaEntry = Assert.Single(imported!, e => e.NationId == mediaId);
        Assert.Equal(new[] { romeId }, mediaEntry.NeighbourIds);

        // The world's own startingNeighbours does NOT carry this pair (dat-neighbour-mask.md §2's 24
        // pairs) -- proving the imported value is the save's own mask, not a silent fall-through to the
        // world's fixed field.
        Assert.False(WorldHasPair(RealGameData.World, romeId, mediaId));
    }

    private static bool WorldHasPair(World world, string a, string b)
    {
        if (world.StartingNeighbours is not { } neighbours)
        {
            return false;
        }

        foreach (var entry in neighbours)
        {
            if (entry.NationId == a)
            {
                return entry.NeighbourIds.Contains(b);
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a minimal SAV-shaped byte array, structurally valid against
    /// <see cref="RealGameData.World"/> (16 nations, 334 cities) but otherwise all zeros -- zero armies,
    /// zero fleets -- with exactly one change from an all-zero neighbour mask: nation 0 (Rome) and
    /// nation 14 (Media) each set the other's bit in their own <c>+0x46</c> word.
    /// </summary>
    private static byte[] BuildMinimalSavWithAMergedRomeMediaNeighbourPair()
    {
        const int nationCount = 16;
        const int savNationRecordLength = 1172;
        const int trailerLength = 55;
        var mercenaryTableLength = SaveMercenaryTable.RecordCount * SaveMercenaryTable.RecordLength;

        var mapAndCityLength = WorldPrefix.SharedPrefixLength; // 89,600 map + 334 city records.
        var totalLength = mapAndCityLength
            + 2 + 0 * SaveArmyTable.RecordLength // army count word, zero army records
            + 2 + 0 * SaveFleetTable.RecordLength // fleet count word, zero fleet records
            + nationCount * savNationRecordLength
            + mercenaryTableLength
            + 2 // news log's own newsIndex field
            + 0 // (newsIndex + 1) slots -- newsIndex -1, an empty log, needs none
            + trailerLength;

        var data = new byte[totalLength];

        // ---- Map + city table: every one of the 334 city records needs a non-empty, NUL-terminated
        // ASCII name; every other field defaults to zero, within WorldPrefix.Parse's own accepted range.
        for (var i = 0; i < WorldPrefix.CityCount; i++)
        {
            data[WorldPrefix.CityStart + i * WorldPrefix.CityRecordLength] = (byte)'C';
        }

        // ---- Army/fleet tables: both counts zero -- neither parser reads a single record.
        var armyCountOffset = mapAndCityLength;
        WriteUInt16(data, armyCountOffset, 0);
        var fleetCountOffset = armyCountOffset + 2;
        WriteUInt16(data, fleetCountOffset, 0);

        // ---- Nation table: 16 records. Name must equal NationCatalog.Name(i) exactly; the leader field
        // (+11, 27 bytes) must hold at least one non-NUL byte; the capital is left unset (the sentinel)
        // so no nation claims a city it does not own; relations default to zero (peace, symmetric,
        // zero diagonal). The neighbour mask (+0x46) is zero for every nation except Rome (0) and
        // Media (14), which set each other's bit -- the one merged pair this test is about.
        var nationTableStart = fleetCountOffset + 2;
        for (var i = 0; i < nationCount; i++)
        {
            var offset = nationTableStart + i * savNationRecordLength;
            WriteAscii(data, offset, NationCatalog.Name((ushort)i));
            data[offset + 11] = (byte)'L'; // leader: one non-NUL byte is enough
            WriteUInt16(data, offset + 0x444, SaveNationTable.NoCapitalSentinel); // capitalCity: none
        }

        const int rome = 0;
        const int media = 14;
        var romeMaskOffset = nationTableStart + rome * savNationRecordLength + 0x46;
        var mediaMaskOffset = nationTableStart + media * savNationRecordLength + 0x46;
        WriteUInt16(data, romeMaskOffset, (ushort)(1 << media));
        WriteUInt16(data, mediaMaskOffset, (ushort)(1 << rome));

        // ---- Mercenary pool: 50 all-zero records -- every one reads as empty (zero troops).
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
