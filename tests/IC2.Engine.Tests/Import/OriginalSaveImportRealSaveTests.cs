using IC2.Data;
using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Imports real original saves and checks the Done-when claims that need an actual save's bytes to
/// prove — everything <see cref="OriginalSaveImportRejectionTests"/> and
/// <see cref="EmbarkationLinkerTests"/> don't already cover file-free. Every test skips with an explicit
/// "original files not configured" result when <c>assets.local.ini</c> is absent (Done-when 4).
/// </summary>
public class OriginalSaveImportRealSaveTests
{
    // ---- Done-when 1: representative sample (operating-guide.md §5's sampling rule), justified here
    // and in the PR body -- at least one mid-turn save (1_thracia_271_spring_3.sav carries an army
    // tombstone, DoD 1's own example of what a mid-turn save looks like), one with the corpus's one
    // signed-moves underflow (DoD 6), and one with the corpus's morale-above-70 army (DoD 8) -- three
    // distinct hazards T21's own Hazards/Done-when list names, from three different save families
    // (Thracia, Rome/Ptolemaic, Carthage) so no one family's quirks hide another's.
    private const string MidTurnTombstoneSave = "1_thracia_271_spring_3.sav";
    private const string NegativeMovesSave = "1_rome_270_summer_7.sav";
    private const string Morale72Save = "1_cartago_271_summer_1.sav";

    // ---- Local-only 2026-09-20 batch (not part of CI's 54-save fixtures repo) ----
    private const string FleetTombstoneSave = "IP012B.sav"; // bug #276: 1 fleet tombstone, 2 army tombstones
    private const string OverCapPurseSave = "IP016.sav"; // bug #312/#315: army 1 money 1066 > purseCapPerUnit
    private const string EmbarkedArmySave = "IP010B.sav"; // review N2: army 12 aboard fleet 4, a real link

    private static OriginalSaveImportResult ImportFixture(string fileName)
    {
        var data = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(fileName));
        return OriginalSaveImporter.Import(
            data, fileName, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            saveId: "imported-" + fileName, saveLabel: "Imported " + fileName);
    }

    public static IEnumerable<object[]> RepresentativeSample() => new[]
    {
        new object[] { MidTurnTombstoneSave },
        new object[] { NegativeMovesSave },
        new object[] { Morale72Save },
    };

    [SkippableTheory]
    [MemberData(nameof(RepresentativeSample))]
    public void Imported_state_validates_and_round_trips_through_SaveManager(string fileName)
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(fileName);

        // Done-when 1: "import, save to the new format, and reload to an equal state." Validate first,
        // exactly as SaveManager.LoadFile would on the way back in.
        GameDataValidation.Validate(fileName, result.Save.State);
        GameDataValidation.Validate(fileName, result.Save);

        var tempPath = Path.Combine(Path.GetTempPath(), "ic2-t21-roundtrip-" + Guid.NewGuid().ToString("N") + ".sav.json");
        try
        {
            SaveManager.WriteFile(tempPath, result.Save);
            var reloaded = SaveManager.LoadFile(tempPath, RealGameData.World, RealGameData.Ruleset);

            Assert.Equal(result.Save.State, reloaded.State);
            Assert.Equal(result.Save.Id, reloaded.Id);
            Assert.Equal(result.Save.WorldId, reloaded.WorldId);
            Assert.Equal(result.Save.RulesetId, reloaded.RulesetId);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(RepresentativeSample))]
    public void Import_report_claims_zero_unmapped_fields_except_the_declared_mercenary_waiver(string fileName)
    {
        // Done-when 2, as narrowed by the user's waiver (docs/tasks/T21.md "Mercenary position", #321):
        // the report's UnmappedFields is derived from OriginalSaveFieldMapping (review B1), and the only
        // entries it can ever carry are exactly the two waived MercenaryRecord fields -- a third entry
        // appearing here means a field IC2.Data now parses stopped being mapped, and this test must fail.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(fileName);

        Assert.Equal(
            new[] { "MercenaryRecord.X", "MercenaryRecord.Y" },
            result.Report.UnmappedFields.OrderBy(f => f, StringComparer.Ordinal));
    }

    [SkippableTheory]
    [MemberData(nameof(RepresentativeSample))]
    public void Every_nation_is_imported_with_exactly_16_nations_and_334_cities(string fileName)
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(fileName);

        Assert.Equal(16, result.Report.NationsImported);
        Assert.Equal(WorldPrefix.CityCount, result.Report.CitiesImported);
        Assert.Equal(16, result.Save.State.Nations.Count);
        Assert.Equal(WorldPrefix.CityCount, result.Save.State.Cities.Count);
    }

    [SkippableFact]
    public void Nation_tax_base_and_wealth_are_read_directly_never_recomputed()
    {
        // Done-when 5: "an imported nation's tax base is SAV nation +0x44c and its wealth +0x430, read
        // directly through T34's parse and never recomputed from the cities." Cross-checked against an
        // independent parse of the same bytes, not against a from-cities rebuild -- a rebuild could
        // legitimately disagree mid-quarter, which is exactly the point DoD 5 is guarding.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var fileName = MidTurnTombstoneSave;
        var data = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(fileName));
        var directParse = SaveNationTable.Parse(data);

        var result = OriginalSaveImporter.Import(
            data, fileName, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            "s", "s");

        for (var code = 0; code < 16; code++)
        {
            var expected = directParse.Nations[code];
            var actual = result.Save.State.Nations[code];
            Assert.Equal((int)expected.TaxBase, actual.TaxBase);
            Assert.Equal(expected.Wealth, actual.Wealth);
        }
    }

    [SkippableFact]
    public void The_negative_moves_army_imports_clamped_to_zero_not_negative_or_65535()
    {
        // Done-when 6: Ptolemaic army 9 at (192, 96) reads Moves -1 in the SAV (ArmyMovesSignedTests) --
        // clamp to 0 is the chosen, stated value (never a negative move count in the new model, never
        // the unsigned reinterpretation 65535), reported in ArmiesWithClampedMoves.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(NegativeMovesSave);

        var army9 = result.Save.State.ArmyById("army-9");
        Assert.NotNull(army9);
        Assert.Equal(0, army9!.Moves);
        Assert.Contains("army-9", result.Report.ArmiesWithClampedMoves);

        // Rome's own army in the same save keeps its ordinary positive Moves (ArmyMovesSignedTests'
        // own worked example: 8) -- proves the clamp targets only the negative record, not every army.
        var army0 = result.Save.State.ArmyById("army-0");
        Assert.NotNull(army0);
        Assert.Equal(8, army0!.Moves);
        Assert.DoesNotContain("army-0", result.Report.ArmiesWithClampedMoves);
    }

    [SkippableFact]
    public void An_army_morale_of_72_survives_the_import_unclamped()
    {
        // Done-when 8: morale is not bounded 51..70 on import -- Celtiberia (nation code 8)'s army at
        // 72 in this save family must survive as 72, never clamped, rejected or "repaired".
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(Morale72Save);

        var celtiberia = result.Save.State.NationById("celtiberia");
        Assert.NotNull(celtiberia);
        var hasMorale72 = result.Save.State.Armies.Any(a =>
            string.Equals(a.Nation, "celtiberia", StringComparison.Ordinal) && a.Morale == 72);
        Assert.True(hasMorale72, "Expected a Celtiberian army imported with Morale 72.");
    }

    [SkippableFact]
    public void Fleet_and_army_tombstones_are_reported_and_leave_no_dangling_reference()
    {
        // Done-when 1 and 10: IP012B.sav has 1 fleet tombstone (slot 4) and 2 army tombstones. Every
        // report entry must be present, and — the delete-sweep hazard — no surviving fleet or army may
        // reference a tombstoned index.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.TryResolve(FleetTombstoneSave);
        Skip.If(path is null, $"'{FleetTombstoneSave}' is not present in the configured corpus on this machine.");
        var data = File.ReadAllBytes(path!);

        var result = OriginalSaveImporter.Import(
            data, FleetTombstoneSave, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "s", "s");

        var skippedFleet = Assert.Single(result.Report.SkippedFleets);
        Assert.Equal(4, skippedFleet.Index);
        Assert.Equal(2, result.Report.SkippedArmies.Count);

        Assert.DoesNotContain(result.Save.State.Fleets, f => f.Id == "fleet-4");
        foreach (var skippedArmy in result.Report.SkippedArmies)
        {
            var danglingId = $"army-{skippedArmy.Index}";
            Assert.DoesNotContain(result.Save.State.Armies, a => a.Id == danglingId);
            Assert.DoesNotContain(result.Save.State.Fleets, f => f.CarriedArmyId == danglingId);
        }

        // The imported state must still validate: GameDataValidation.ValidateState would reject a
        // dangling CarriedArmyId/AboardFleetId immediately.
        GameDataValidation.Validate(FleetTombstoneSave, result.Save.State);
    }

    [SkippableFact]
    public void A_purse_above_the_supply_dialogs_cap_survives_the_import_uncapped()
    {
        // Bug #315: JoinArmiesCommandHandler wrongly applies PurseAccounting.Credit's 1,000 cap to the
        // uncapped join path. The importer must not make the same mistake on its own, separate path:
        // IP016.sav army 1 holds 1066, above purseCapPerUnit (1,000).
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.TryResolve(OverCapPurseSave);
        Skip.If(path is null, $"'{OverCapPurseSave}' is not present in the configured corpus on this machine.");
        var data = File.ReadAllBytes(path!);

        Assert.True(1066 > RealGameData.Ruleset.Economy.PurseCapPerUnit,
            "Fixture assumption stale: the corpus value is no longer above the ruleset's purse cap.");

        var result = OriginalSaveImporter.Import(
            data, OverCapPurseSave, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "s", "s");

        var army1 = result.Save.State.ArmyById("army-1");
        Assert.NotNull(army1);
        Assert.Equal(1066, army1!.Money);
    }

    [SkippableFact]
    public void Relations_are_imported_from_the_saves_own_matrix_not_reset_to_uniform_peace()
    {
        // Done-when 11 (review B2): 1_rome_270_summer_7.sav's own relation matrix, read independently
        // through SaveNationTable.Parse and confirmed by hand: 6 wars (rome-gaul, carthage-celtiberia,
        // seleucid-ptolemaic, seleucid-bithynia, seleucid-galatia, greece-illyria) plus cooldowns such
        // as seleucid-media at -5. DiplomaticRelations.Uniform would make every one of these 0 (peace).
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(NegativeMovesSave); // = 1_rome_270_summer_7.sav

        var relations = result.Save.State.Relations;
        Assert.True(relations.IsWellFormed());

        var wars = new (string A, string B)[]
        {
            ("rome", "gaul"),
            ("carthage", "celtiberia"),
            ("seleucid", "ptolemaic"),
            ("seleucid", "bithynia"),
            ("seleucid", "galatia"),
            ("greece", "illyria"),
        };
        var warCode = RealGameData.Ruleset.Diplomacy.StateCodes.War;
        foreach (var (a, b) in wars)
        {
            Assert.Equal(warCode, relations.Get(a, b));
            Assert.Equal(warCode, relations.Get(b, a));
        }

        // A named cooldown cell -- negative, not one of the four named relation states.
        Assert.Equal(-5, relations.Get("seleucid", "media"));
        Assert.Equal(-5, relations.Get("media", "seleucid"));

        // Exactly 6 unique war pairs across the whole matrix, matching the entry's own count.
        var nationIds = relations.NationIds;
        var warPairCount = 0;
        for (var i = 0; i < nationIds.Count; i++)
        {
            for (var j = i + 1; j < nationIds.Count; j++)
            {
                if (relations.Get(nationIds[i], nationIds[j]) == warCode)
                {
                    warPairCount++;
                }
            }
        }

        Assert.Equal(6, warPairCount);
    }

    [SkippableFact]
    public void Turn_order_is_imported_from_the_saves_own_order_and_index_not_worlds()
    {
        // Done-when 12 (review B2): 1_thracia_271_spring_3.sav's own trailer names Thracia active at
        // turn-order index 5 of 16 (SaveTurnState.TurnOrderIndex == 5, TurnOrder[5] == 15 == Thracia) --
        // last in world.TurnOrder, but mid-pack in the save's own order. The 10 nations after it
        // (indices 6..15) are still due to move this cycle, and ending Thracia's turn (index 5 -> 6)
        // must not wrap the turn order and must not signal the round-scoped calendar tick.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(MidTurnTombstoneSave); // = 1_thracia_271_spring_3.sav
        var state = result.Save.State;

        Assert.Equal(16, state.TurnOrder.Count);
        Assert.Equal(5, state.ActiveSeatIndex);
        Assert.Equal("thracia", state.TurnOrder[5]);
        Assert.Equal(10, state.TurnOrder.Count - 1 - state.ActiveSeatIndex);

        // A registry scoped to only SeatRotationSystem (IC2.Engine.Calendar's own namespace), built
        // locally rather than depending on another task's own test fixtures -- this only needs T06's
        // rotation rule, over the real imported state, to prove the "does not tick the calendar" claim.
        var registry = SystemRegistry.FromAssemblies(
            new[] { typeof(SeatRotationSystem).Assembly },
            t => t == typeof(SeatRotationSystem));
        var coordinator = new TurnCoordinator(registry, RealGameData.Ruleset, RealGameData.World, NullEventSink.Instance);

        var turnResult = coordinator.RunTurn(state);

        Assert.Equal(6, turnResult.State.ActiveSeatIndex);
        Assert.False(turnResult.RoundTickRan, "Ending Thracia's turn (index 5 of 16) must not tick the calendar.");
    }

    [SkippableFact]
    public void An_embarked_army_round_trips_through_the_real_embarkation_link()
    {
        // Review N2: none of the three Done-when-1 samples has an embarked army, so no test yet ran a
        // real link through EmbarkationLinker end to end. IP010B.sav: army 12 (Ptolemaic) rides fleet 4.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.TryResolve(EmbarkedArmySave);
        Skip.If(path is null, $"'{EmbarkedArmySave}' is not present in the configured corpus on this machine.");
        var data = File.ReadAllBytes(path!);

        var result = OriginalSaveImporter.Import(
            data, EmbarkedArmySave, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "s", "s");

        var army = result.Save.State.ArmyById("army-12");
        var fleet = result.Save.State.FleetById("fleet-4");
        Assert.NotNull(army);
        Assert.NotNull(fleet);
        Assert.Equal("fleet-4", army!.AboardFleetId);
        Assert.Equal("army-12", fleet!.CarriedArmyId);
        Assert.Null(army.CoveredTileCode);

        GameDataValidation.Validate(EmbarkedArmySave, result.Save.State);
    }

    [SkippableFact]
    public void An_under_construction_fleet_imports_with_no_position_and_condition_zero()
    {
        // Review N3: the comment on this mapping branch (OriginalSaveImporter.cs, the fleets loop) was
        // never itself asserted. 1_cartago_271_summer_1.sav's fleet 2 (Greece) is still building at city
        // index 166, countdown 24 -- ConditionPercent must read 0 (the field has no meaning yet),
        // CoveredTileCode must be null (no map position yet), and BuildCityId/ConstructionTicksRemaining
        // must carry the launch order's own values.
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);

        var result = ImportFixture(Morale72Save); // = 1_cartago_271_summer_1.sav

        var fleet = result.Save.State.FleetById("fleet-2");
        Assert.NotNull(fleet);
        Assert.Equal("greece", fleet!.Nation);
        Assert.Equal(0, fleet.ConditionPercent);
        Assert.Null(fleet.CoveredTileCode);
        Assert.Equal(24, fleet.ConstructionTicksRemaining);
        Assert.Equal(RealGameData.World.Cities[166].Id, fleet.BuildCityId);
    }
}
