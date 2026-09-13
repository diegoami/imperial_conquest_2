namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// One row of the committed expected-outcome table (<c>expected-corpus-outcomes.json</c>,
/// docs/build-orchestration-plan.md T30 Done-when line 4). Generated once from the configured
/// original assets directory by running every <c>IC2.Data</c> parser over every file and recording
/// what a clean parse actually returns; committed so the regression sweep has something to compare
/// against without needing the original files to already know what "correct" looks like.
/// <see cref="MercenaryRecords"/> and <see cref="TurnWeek"/> are null exactly when that table has no
/// DAT-shaped equivalent at all (<see cref="DatDataNotPresentException"/> is the expected outcome for
/// that row), never a fabricated zero.
/// </summary>
public sealed record CorpusEntry(
    string RelativePath,
    string Format,
    int Bytes,
    int Cities,
    int Armies,
    int SkippedArmies,
    int Fleets,
    int Nations,
    int NationCitiesSum,
    int RecruitmentEntries,
    int? MercenaryRecords,
    int? TurnWeek)
{
    public override string ToString() => RelativePath;
}
