using IC2.Engine.Model;

namespace IC2.Engine.Import;

/// <summary>
/// What <see cref="OriginalSaveImporter.Import"/> produced: the live state, wrapped in a
/// <see cref="SaveGame"/> ready for <see cref="IC2.Engine.Persistence.SaveManager"/>, plus a report of
/// what the import actually did.
/// </summary>
public sealed record OriginalSaveImportResult(SaveGame Save, OriginalSaveImportReport Report);

/// <summary>
/// Records what one import run did, so the claims <c>docs/tasks/T21.md</c>'s Done-when list makes are
/// checkable at runtime rather than only in a code review.
/// </summary>
/// <param name="UnmappedFields">
/// Done-when 2: "an import report lists zero unmapped fields for every table <c>IC2.Data</c> already
/// parses." <see cref="OriginalSaveImporter"/> reads every public field of every record
/// <c>SaveNationTable</c>, <c>SaveArmyTable</c>, <c>SaveFleetTable</c>, <c>SaveRecruitmentTable</c>,
/// <c>SaveMercenaryTable</c>, <c>SaveTurnState</c> and <c>SavePendingOffer</c> expose (the PR body lists
/// the mapping field by field) — so this is always empty. Kept as a real, populated-if-ever-true list
/// rather than a bare code comment, so a future field <c>IC2.Data</c> starts parsing shows up here as a
/// gap a test can catch, not a claim nobody re-checks.
/// </param>
/// <param name="SkippedArmies">
/// Done-when 1 and 10: every army-table record the parser skipped as an owner-<c>0xFFFF</c> tombstone
/// (<c>SaveArmyTable.SkippedRecords</c>), reported rather than silently dropped.
/// </param>
/// <param name="SkippedFleets">
/// Done-when 10: every fleet-table record the parser skipped as an owner-<c>0xFFFF</c> tombstone
/// (<c>SaveFleetTable.SkippedRecords</c>), the same way <see cref="SkippedArmies"/> reports the army
/// side.
/// </param>
/// <param name="ArmiesWithClampedMoves">
/// Done-when 6: the ids of every imported army whose original <c>moves</c> word was negative (the
/// signed-underflow bug T44 documents) and was therefore clamped to 0 rather than carried through as a
/// negative move count or reinterpreted as 65535. Empty on every save that has no such army — the
/// corpus has exactly one, at <c>Army 9</c> in <c>11.sav</c> and its siblings.
/// </param>
public sealed record OriginalSaveImportReport(
    ValueList<string> UnmappedFields,
    ValueList<SkippedRecordReport> SkippedArmies,
    ValueList<SkippedRecordReport> SkippedFleets,
    ValueList<string> ArmiesWithClampedMoves,
    int NationsImported,
    int CitiesImported,
    int ArmiesImported,
    int FleetsImported,
    int RecruitmentSlotsImported,
    int MercenarySlotsImported);

/// <summary>One record a table parser skipped rather than returning — mirrors
/// <c>IC2.Data.SkippedArmyRecord</c> / <c>IC2.Data.SkippedFleetRecord</c>, kept as this module's own
/// type so <c>IC2.Engine.Import</c>'s public surface does not leak <c>IC2.Data</c> internals.</summary>
public sealed record SkippedRecordReport(int Index, int X, int Y);
