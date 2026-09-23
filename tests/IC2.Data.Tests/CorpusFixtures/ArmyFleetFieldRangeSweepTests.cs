using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// T44 (issue #126) Done-when line 5, "the sweep": every other <c>ushort</c> field on
/// <see cref="ArmyRecord"/>, <see cref="ArmyUnit"/> and <see cref="FleetRecord"/> is checked against
/// the whole configured corpus (every <c>.sav</c> across the three save folders <see
/// cref="CorpusFileLocator"/> knows about) for values outside its plausible range and for the same
/// signedness question <see cref="ArmyRecord.Moves"/> turned out to have — a field the game guards
/// with <c>JLE</c>/<c>MOVSX</c> is signed whatever its current C# type says.
///
/// What this sweep found, field by field (also stated in the PR body):
/// - <see cref="ArmyRecord.Moves"/>: the one field with an actual out-of-range/signedness bug — fixed
///   by this task (see <see cref="ArmyMovesSignedTests"/>). Range asserted here: -1..10 (Scope's
///   corpus fact "legitimate values run 0-10", plus the one confirmed -1).
/// - <see cref="ArmyRecord.CoveredCell"/> (armies not aboard a fleet): 2..11, exactly the land-terrain
///   range docs/design-audit.md §2.1 confirms (<c>FUN_0044D420</c>'s <c>2 &lt;= cell &lt;= 11</c> guard)
///   — no out-of-range value, and this field's own AboardFleetSentinel handling is untouched.
/// - <see cref="ArmyRecord.Morale"/>: docs/design-audit.md §2.9a gives the weekly supply-driven tick's
///   own bounds as 51..70, but this corpus sweep finds one army (Celtiberia, nation code 8, in six of
///   the Carthage save-family files) at 72 — two points above that tick's stated ceiling. This is
///   **not** a signedness or decode problem (72 is a small positive number: unsigned and signed
///   16-bit reads agree exactly), so no code change follows from it. It is flagged here only because
///   §2.9a documents one mechanic that writes this field (the weekly tick), not every mechanic that
///   can — a battle or promotion event plausibly pushes it past that tick's own ceiling. Recorded as
///   an open range note, not a defect: see the PR body.
/// - <see cref="ArmyRecord.Supplies"/>: the report's own decompiled excerpts of the weekly-tick and
///   "has-not-acted" functions both happen to read this field with <c>MOVSX</c> (word +0xA), the same
///   instruction pattern that flagged <see cref="ArmyRecord.Moves"/>. But those two reads are
///   incidental to functions the report was dumping for Moves, not an exhaustive sweep of every
///   Supplies read/write the way <see cref="ArmyRecord.Moves"/> got — and this corpus shows no
///   Supplies value anywhere near the unsigned/signed boundary (max observed: 796, nowhere close to
///   32,768). No corpus manifestation, so no code change; flagged as a candidate for a dedicated
///   research pass, not resolved here.
/// - <see cref="ArmyRecord.Money"/>: docs/reports/upkeep-payment-and-desertion.md's billing pseudocode
///   shows a signed <c>JLE</c> test ("purse &lt;= 0") on this field (<c>CMP word ptr [ESI+0x8],0</c> /
///   <c>JLE</c> at <c>00451c1c</c>), but the same tick unconditionally floors it to 0 before the tick
///   ends (<c>a.money = max(0, a.money)</c>), so no legitimate save can ever show a negative purse —
///   meaning a stored word of <c>0x8000</c> or more, read the way the game itself reads it, IS a
///   negative purse that floor rules out. That makes the field's documented range <c>0..32767</c>
///   (<see cref="short.MaxValue"/>), not the full <c>ushort</c> width.
///   **T64 (#301), bug #312**: the 2026-09-20 corpus shows <c>IP016.sav</c>/<c>IP016B.sav</c> army 1 at
///   Money 1066 — above <c>data/rulesets/classical-faithful.json</c>'s <c>economy.caps.purseCapPerUnit</c>
///   (1,000), whose own provenance cites
///   docs/reports/decompiled-unit-map-orders-and-record-fields.md: <c>TAFSupply_ChangeMoney</c> caps an
///   army's or fleet's own purse at 1,000 — but that is the SUPPLY DIALOG's own clamp on one write
///   path, not a cap on the field. The same report's **Join armies** row
///   (<c>TUnitMap_JoinArmies</c> <c>0x004472FC</c>) is the direct source for the uncapped path: "Units
///   are moved one at a time, supplies and money add, the emptied army is deleted" — the join's own
///   caps are 20 combined units and 100,000 combined troops, with no money cap listed. (An earlier
///   revision of this remark cited <c>FUN_0044ab90</c> and
///   docs/reports/army-to-army-transfer-confirmed.md for this; that function is the army-removal
///   routine the join calls, and that report documents the transfer dialog, not the uncapped add —
///   corrected here, per review.) IP016/IP016B army 1 at 1066 is the empirical confirmation. So the
///   field itself has **no confirmed cap** — 1,000 is a per-dialog input clamp, not a data invariant —
///   and the assertion below is bounded by the game's own signed reading of the field (0..32767)
///   instead, evidenced above, not by the observed 1066. See also #315 (separately filed, not this
///   task's): the reimplementation's <c>JoinArmiesCommandHandler</c> currently applies the supply
///   dialog's 1,000 clamp to this uncapped join path.
/// - <see cref="ArmyUnit"/> (TypeCode, Troops, QualityCode, MercenaryLabel) and the remaining
///   <see cref="FleetRecord"/> fields (X, Y, OwnerCode, ConstructionCountdown, Moves, Supplies, Money,
///   ShipCount, BuildCityOrCondition, CarriedArmyIndex): nothing out of range, and no decompiled
///   signedness evidence for any of them was found in the research repo's reports. Nothing found.
///
/// Regenerate/reverify with a throwaway console app referencing IC2.Data + IC2.Inspect that calls
/// <see cref="CorpusFileLocator.DiscoverFileNames"/> and folds min/max over every
/// <see cref="SaveArmyTable.Parse"/>/<see cref="SaveFleetTable.Parse"/> result — exactly what this
/// task's own investigation did. This test asserts the same bounds as a durable regression: if the
/// corpus grows and a future file falls outside one of them, that is new information worth surfacing
/// (T30's precedent for the tombstone), not drift to silently accept.
/// </summary>
public class ArmyFleetFieldRangeSweepTests
{
    [SkippableFact]
    public void Every_army_and_fleet_field_across_the_whole_corpus_stays_within_its_swept_range()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var fileNames = CorpusFileLocator.DiscoverFileNames(settings).Keys
            .Where(n => n.EndsWith(".sav", StringComparison.Ordinal))
            .ToList();
        Skip.If(fileNames.Count == 0, "No .sav files found in the configured corpus.");

        var armyRecordsSeen = 0;
        var fleetRecordsSeen = 0;

        foreach (var name in fileNames)
        {
            var path = CorpusFileLocator.TryResolve(settings, name);
            if (path is null) continue;
            var data = File.ReadAllBytes(path);

            var armies = SaveArmyTable.Parse(data);
            var fleets = SaveFleetTable.Parse(data);

            foreach (var a in armies.Armies)
            {
                armyRecordsSeen++;

                // The one field this task fixes: -1 is the sole legitimate negative value, and the
                // Scope's own corpus fact bounds every other record at 0-10.
                Assert.InRange(a.Moves, (short)-1, (short)10);
                if (a.Moves == -1)
                    Assert.True(a.IsFrozen, $"{name} army {a.Index}: Moves -1 but IsFrozen is false.");

                // Land-terrain range (docs/design-audit.md §2.1); the aboard-fleet sentinel on this
                // same field is unaffected and excluded here exactly as ArmyRecord.IsAboardFleet does.
                if (!a.IsAboardFleet)
                    Assert.InRange(a.CoveredCell, (ushort)2, (ushort)11);

                // Morale: see this class's remarks. 72 (not just the tick's documented 70) is the
                // current corpus ceiling; 51 is the documented floor and matches every record seen.
                Assert.InRange(a.Morale, (ushort)51, (ushort)72);

                // Supplies: no corpus value anywhere near the unsigned/signed boundary (see remarks)
                // — a generous ceiling well below it, so a genuine future underflow (a value near
                // 65535) still fails loudly.
                Assert.InRange(a.Supplies, (ushort)0, (ushort)2000);
                // Money (T64 #301, bug #312): no confirmed CAP on the field — see this class's
                // remarks (purseCapPerUnit is the supply dialog's own clamp; the Join-armies path
                // adds two purses uncapped). But it IS read signed by the game (upkeep-payment-and-
                // desertion.md's JLE test), floored to 0 after the quarterly tick, so a stored word of
                // 0x8000+ is a negative purse the game itself never leaves in a save. That is a real,
                // narrower-than-ushort bound: 0..short.MaxValue, not the full 0..65535 storage width.
                Assert.InRange(a.Money, (ushort)0, (ushort)short.MaxValue);

                Assert.InRange(a.X, (ushort)0, (ushort)333);
                Assert.InRange(a.Y, (ushort)0, (ushort)333);

                foreach (var u in a.Units)
                {
                    Assert.InRange(u.TypeCode, (ushort)0, (ushort)4);
                    Assert.InRange(u.QualityCode, (ushort)5, (ushort)9);
                    // The 100,000-troop army cap is confirmed (docs/design-audit.md §2.13); no single
                    // unit slot has ever been observed anywhere close to it.
                    Assert.InRange(u.Troops, (ushort)1, (ushort)40000);
                    Assert.InRange(u.MercenaryLabel, (ushort)0, (ushort)50);
                }
            }

            foreach (var f in fleets.Fleets)
            {
                fleetRecordsSeen++;

                Assert.InRange(f.X, (ushort)0, (ushort)333);
                Assert.InRange(f.Y, (ushort)0, (ushort)333);
                // T64 (#301, bug #276): a fleet-owner tombstone (0xFFFF, the same bit pattern as the
                // army-table tombstone) DOES appear in this corpus (IP012B.sav, fleet slot 4) — but
                // SaveFleetTable.Parse now skips and reports it in SkippedRecords, the same way
                // SaveArmyTable handles its own tombstones, so it never reaches Fleets and never
                // reaches this assertion. This range stays a real, unwidened 0..15: see
                // FleetTombstoneTests for the skip itself.
                Assert.InRange(f.OwnerCode, (ushort)0, (ushort)15);
                // LaunchedSentinel (0xFFFF) is a real, already-handled sentinel on this same field —
                // exclude it exactly as IsLaunched does, rather than widen the range to cover it.
                if (!f.IsLaunched)
                    Assert.InRange(f.ConstructionCountdown, (ushort)0, (ushort)200);
                Assert.InRange(f.Moves, (ushort)0, (ushort)200);
                Assert.InRange(f.Supplies, (ushort)0, (ushort)2000);
                Assert.InRange(f.Money, (ushort)0, (ushort)1000);
                Assert.InRange(f.ShipCount, (ushort)0, (ushort)2000);
                if (f.BuildCityIndex is { } buildCity)
                    Assert.InRange(buildCity, (ushort)0, (ushort)333);
                if (f.ConditionPercent is { } condition)
                    Assert.InRange(condition, (ushort)0, (ushort)1000);
                if (f.CarriedArmyIndex is { } carried)
                    Assert.InRange(carried, (ushort)0, (ushort)700);
            }
        }

        Assert.True(armyRecordsSeen > 0, "The sweep parsed zero army records — check the corpus configuration.");
        Assert.True(fleetRecordsSeen > 0, "The sweep parsed zero fleet records — check the corpus configuration.");
    }
}
