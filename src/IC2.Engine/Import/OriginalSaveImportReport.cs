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
/// parses." Always exactly <see cref="OriginalSaveFieldMapping.UnmappedFieldNames"/> — the qualified
/// names of every <see cref="OriginalSaveFieldMapping.FieldMappingKind.DeclaredUnmapped"/> entry in the
/// declared mapping (review B1, PR #319 round 1: this was a hard-coded empty list nobody re-checked, so
/// it stayed "correct" even after a field <c>IC2.Data</c> parses went unread). Today that is exactly
/// <c>MercenaryRecord.X</c>, <c>MercenaryRecord.Y</c> and <c>WorldPrefix.Cells</c> — three narrow user
/// waivers of this Done-when line (docs/tasks/T21.md "Mercenary position" and "The map grid's overlay")
/// — and nothing else; a new gap in the mapping fails <c>OriginalSaveFieldMappingTests</c>
/// (<c>tests/IC2.Engine.Tests/Import/</c>) before it can ever silently widen this list.
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
