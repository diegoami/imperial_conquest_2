using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// docs/build-orchestration-plan.md, T30, Defect A and Done-when lines 1-3: the 0xFFFF no-owner army
/// tombstone must be skipped (not returned as a degenerate owner-65535 entry, and not aborting the
/// whole file), surfaced separately so a caller can tell "clean parse" from "parse with tombstones",
/// while any other malformed record still throws, naming the record index and the failing field.
/// </summary>
public class ArmyTombstoneTests
{
    // ---- Real saves (docs/investigations/thracia-supply-morale.md / dat-file-layout.md) ----
    // These three are the only saves in the whole 51-file corpus carrying a tombstone, one each.

    [SkippableTheory]
    [InlineData("saves/1_thracia_271_spring_3.sav", 10, 41, 62)]
    [InlineData("saves/1_thracia_271_autumn_1.sav", 9, 38, 52)]
    [InlineData("saves/1_cartago_271_spring_5.sav", 0, 97, 31)]
    public void Known_tombstone_saves_skip_the_record_and_keep_parsing(
        string relativePath, int expectedIndex, ushort expectedX, ushort expectedY)
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath(relativePath));

        // Would have thrown InvalidDataException before this task's fix (see the investigation docs'
        // "A real parser bug found on the way" / "The parse sweep" sections).
        var table = SaveArmyTable.Parse(data);

        var skipped = Assert.Single(table.SkippedRecords);
        Assert.Equal(expectedIndex, skipped.Index);
        Assert.Equal(expectedX, skipped.X);
        Assert.Equal(expectedY, skipped.Y);

        // Excluded from Armies, not present as a degenerate owner-65535 entry.
        Assert.DoesNotContain(table.Armies, a => a.OwnerCode == ArmyRecord.TombstoneOwnerSentinel);
        Assert.DoesNotContain(table.Armies, a => a.Index == expectedIndex);
    }

    [SkippableFact]
    public void List_armies_finds_the_thracian_army_in_spring_3_instead_of_aborting()
    {
        // docs/build-orchestration-plan.md, Done-when line 1: "--list-armies … Thracia prints the
        // Thracian army for the first two [saves] instead of aborting." Thracia is nation code 15
        // (docs/investigations/thracia-supply-morale.md, "Nation code 15 (Thracia)").
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves/1_thracia_271_spring_3.sav"));

        var table = SaveArmyTable.Parse(data);
        var thracianArmies = table.Armies.Where(a => a.OwnerCode == 15).ToList();

        var army = Assert.Single(thracianArmies);
        Assert.Equal(6, army.Index);
        Assert.Equal((ushort)160, army.X);
        Assert.Equal((ushort)30, army.Y);
        // docs/investigations/thracia-supply-morale.md's table: spring_3 supplies 98t / 44% / morale 66.
        Assert.Equal((ushort)98, army.Supplies);
        Assert.Equal(44, army.SupplyPercent);
        Assert.Equal((ushort)66, army.Morale);
    }

    [SkippableFact]
    public void List_armies_finds_the_thracian_army_in_autumn_1_instead_of_aborting()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves/1_thracia_271_autumn_1.sav"));

        var table = SaveArmyTable.Parse(data);
        var thracianArmies = table.Armies.Where(a => a.OwnerCode == 15).ToList();

        var army = Assert.Single(thracianArmies);
        Assert.Equal(6, army.Index);
        // docs/investigations/thracia-supply-morale.md's table: autumn_1 supplies 0t / 0% / morale 51 (floor).
        Assert.Equal((ushort)0, army.Supplies);
        Assert.Equal(0, army.SupplyPercent);
        Assert.Equal((ushort)51, army.Morale);
    }

    // ---- Synthetic records: malformed-for-other-reasons must still throw (Done-when line 3) ----
    // These do not need the original files; SyntheticSaveBuilder constructs a minimal, structurally
    // valid SAV byte array itself. See SyntheticSaveBuilder's own remarks for why no Skip is needed.

    [Fact]
    public void Tombstone_is_skipped_even_among_other_valid_and_would_be_invalid_records()
    {
        var data = SyntheticSaveBuilder.MinimalSav(3,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 1, owner: 2)),
            (1, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 7, y: 7, owner: 0xFFFF)),
            (2, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 3, y: 3, owner: 5)));

        var table = SaveArmyTable.Parse(data);

        Assert.Equal(2, table.Armies.Count);
        var skipped = Assert.Single(table.SkippedRecords);
        Assert.Equal(1, skipped.Index);
        Assert.Equal((ushort)7, skipped.X);
        Assert.Equal((ushort)7, skipped.Y);
        Assert.DoesNotContain(table.Armies, a => a.OwnerCode == ArmyRecord.TombstoneOwnerSentinel);
        Assert.Equal(new ushort[] { 2, 5 }, table.Armies.Select(a => a.OwnerCode));
    }

    [Fact]
    public void Owner_code_out_of_range_and_not_the_sentinel_still_throws_naming_index_and_field()
    {
        // Do NOT widen the owner check to accept all values <= 65535 (build-orchestration-plan.md's
        // T30 hazard note): 0xFFFF is a specific sentinel, every other out-of-range owner is a genuine
        // parse failure.
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 1, owner: 999)));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Out_of_range_x_coordinate_still_throws_naming_index_and_field()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 9999, y: 1, owner: 0)));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("X coordinate", ex.Message);
    }

    [Fact]
    public void Out_of_range_y_coordinate_still_throws_naming_index_and_field()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 9999, owner: 0)));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("Y coordinate", ex.Message);
    }

    [Fact]
    public void Unit_slot_out_of_range_type_still_throws_naming_index_slot_and_field()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyWithUnit(d, off, type: 9, quality: 7, name: "Legion")));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("unit slot 0", ex.Message);
        Assert.Contains("type code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unit_slot_out_of_range_quality_still_throws_naming_index_slot_and_field()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyWithUnit(d, off, type: 2, quality: 99, name: "Legion")));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("unit slot 0", ex.Message);
        Assert.Contains("quality code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unit_slot_missing_name_still_throws_naming_index_slot_and_field()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyWithUnit(d, off, type: 2, quality: 7, name: "")));

        var ex = Assert.Throws<InvalidDataException>(() => SaveArmyTable.Parse(data));
        Assert.Contains("Army 0", ex.Message);
        Assert.Contains("unit slot 0", ex.Message);
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
