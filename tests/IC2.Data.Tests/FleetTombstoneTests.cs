using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T64 (issue #301), the parser half of bug #276: the 0xFFFF no-owner fleet tombstone — a fleet
/// absorbed into another during the turn and not yet compacted — must be skipped by
/// <see cref="SaveFleetTable.Parse"/> (not returned as a degenerate owner-65535 entry), surfaced
/// separately so a caller can tell "clean parse" from "parse with tombstones", exactly mirroring
/// <see cref="ArmyTombstoneTests"/> for <see cref="SaveArmyTable"/>. Unlike the army table, the fleet
/// table has no all-tombstoned guard: see <see cref="SaveFleetTable.Parse"/>'s own remarks and
/// <see cref="An_all_tombstoned_fleet_table_parses_to_zero_fleets_not_an_exception"/> below for why.
/// </summary>
public class FleetTombstoneTests
{
    // ---- The real save (bug #276) ----

    [SkippableFact]
    public void IP012B_sav_skips_its_one_tombstoned_fleet_and_keeps_the_rest()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        // IP012B.sav is one of the 2026-09-20 batch, not yet part of CI's fixtures-repo clone (a fixed
        // 54-save corpus) — a per-case Skip, not a hard failure, when this machine's configured corpus
        // doesn't have it, mirroring CorpusSweepTests' own TryResolve-then-Skip.If pattern.
        var path = FixtureResolver.TryResolve("IP012B.sav");
        Skip.If(path is null, "'IP012B.sav' is not present in the configured corpus on this machine.");
        var data = File.ReadAllBytes(path!);

        var table = SaveFleetTable.Parse(data);

        var skipped = Assert.Single(table.SkippedRecords);
        Assert.Equal(4, skipped.Index);

        // Excluded from Fleets, not present as a degenerate owner-65535 entry; every surviving fleet
        // keeps its own original slot index rather than being renumbered.
        Assert.DoesNotContain(table.Fleets, f => f.OwnerCode == SaveFleetTable.TombstoneOwnerSentinel);
        Assert.DoesNotContain(table.Fleets, f => f.Index == 4);
        Assert.Contains(table.Fleets, f => f.Index == 0);
    }

    // ---- Synthetic records: no need for the original files ----

    [Fact]
    public void Tombstone_is_skipped_even_among_other_fleets_which_keep_their_own_slot_index()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithFleets(armyCount: 0, fleetCount: 3,
            fleetWriters: new (int, Action<byte[], int>)[]
            {
                (0, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 2)),
                (1, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF)),
                (2, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 5)),
            });

        var table = SaveFleetTable.Parse(data);

        Assert.Equal(2, table.Fleets.Count);
        var skipped = Assert.Single(table.SkippedRecords);
        Assert.Equal(1, skipped.Index);
        Assert.DoesNotContain(table.Fleets, f => f.OwnerCode == SaveFleetTable.TombstoneOwnerSentinel);
        // Slot indices are preserved, not renumbered to 0/1 for the two survivors.
        Assert.Equal(new[] { 0, 2 }, table.Fleets.Select(f => f.Index));
        Assert.Equal(new ushort[] { 2, 5 }, table.Fleets.Select(f => f.OwnerCode));
    }

    [Fact]
    public void The_rest_of_a_skipped_fleet_record_is_not_validated()
    {
        // Mirrors ArmyTombstoneTests: a tombstoned record's other fields describe a slot that is not
        // really a fleet, so they are never read as anything other than X/Y for diagnostics — a
        // tombstone with, say, an out-of-range ship count elsewhere in the record must still be
        // skipped cleanly rather than throwing.
        var data = SyntheticSaveBuilder.MinimalSavWithFleets(armyCount: 0, fleetCount: 1,
            fleetWriters: new (int, Action<byte[], int>)[]
            {
                (0, (d, off) =>
                {
                    SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF);
                    // Ship count word (+18): a wildly implausible value that a real fleet would never
                    // carry. SaveFleetTable has no range validation on any field (Hazards: "validates
                    // no owner range today, and this task adds none"), so this only proves the record
                    // is skipped whole, not that some other field would have thrown.
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                        d.AsSpan(off + 18, 2), 65000);
                }),
            });

        var table = SaveFleetTable.Parse(data);

        Assert.Empty(table.Fleets);
        Assert.Single(table.SkippedRecords);
    }

    [Fact]
    public void A_table_with_one_tombstone_among_real_fleets_is_not_rejected()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithFleets(armyCount: 0, fleetCount: 2,
            fleetWriters: new (int, Action<byte[], int>)[]
            {
                (0, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF)),
                (1, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 3)),
            });

        var table = SaveFleetTable.Parse(data);

        Assert.Single(table.SkippedRecords);
        Assert.Single(table.Fleets);
    }

    [Fact]
    public void An_empty_fleet_table_has_no_skipped_records()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithFleets(armyCount: 0, fleetCount: 0);

        var table = SaveFleetTable.Parse(data);

        Assert.Empty(table.Fleets);
        Assert.Empty(table.SkippedRecords);
    }

    // ---- Done-when 2: the all-tombstoned case, decided ----
    //
    // Unlike SaveArmyTable, SaveFleetTable has no AllArmyRecordsTombstonedException analogue. The
    // army guard exists because the confirmed corpus never showed more than one tombstone per army
    // table out of hundreds of records, making "every record is a tombstone" unambiguously
    // suspicious. A whole-corpus scan after this fix (100 files, including the 2026-09-20 IP*.sav
    // batch) finds exactly one file with any skipped fleet at all (IP012B.sav, one skip out of five
    // slots) and zero files whose fleet table is nonempty yet 100% tombstoned — see the PR body. A
    // fleet count of zero, or a fleet table that turns out to be entirely tombstones, carries none of
    // the army table's "suspiciously empty" signal: plenty of nations simply have no fleets afloat on
    // a given turn, so this case parses to zero fleets rather than throwing.

    [Fact]
    public void An_all_tombstoned_fleet_table_parses_to_zero_fleets_not_an_exception()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithFleets(armyCount: 0, fleetCount: 3,
            fleetWriters: new (int, Action<byte[], int>)[]
            {
                (0, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF)),
                (1, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF)),
                (2, (d, off) => SyntheticSaveBuilder.WriteFleetOwner(d, off, 0xFFFF)),
            });

        var table = SaveFleetTable.Parse(data);

        Assert.Empty(table.Fleets);
        Assert.Equal(3, table.SkippedRecords.Count);
    }
}
