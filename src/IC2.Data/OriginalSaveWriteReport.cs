namespace IC2.Data;

/// <summary>Byte-operation metadata only. Authenticity, original GUI composition and Windows acceptance
/// are external evidence obligations; neither identity nor tax construction establishes them.</summary>
public sealed class OriginalSaveWriteReport
{
    public string DestinationPath { get; }
    public string TemplateSha256 { get; }
    public string OutputSha256 { get; }
    public int Length { get; }
    public OriginalSaveLayout Layout { get; }
    public string ProfileId { get; }
    public bool IsIdentity { get; }
    public BoundOriginalTaxEdit? TaxEdit { get; }
    public IReadOnlyList<int> ChangedOffsets { get; }
    public string ResearchCommit => TaxEdit is null ? OriginalSaveLayout.ResearchCommit : OriginalSaveLayout.TaxResearchCommit;
    public string RegionPolicyResearchCommit => OriginalSaveLayout.RegionPolicyResearchCommit;
    public string EvidenceLimits => "Byte binding only; source authenticity/loadability, complete GUI/close/pump/save composition and native Windows acceptance unestablished. No native JSON conversion or future RNG-equivalence claim.";
    public string PreservationReason => IsIdentity
        ? "Every byte preserved: identical strategic envelope including opaque bytes and residue."
        : "Bound original current-human tax delta only; every non-tax byte preserved exactly. Candidate awaiting native validation.";

    internal OriginalSaveWriteReport(string path, string sourceHash, string outputHash, int length,
        OriginalSaveLayout layout, string profile, bool identity, BoundOriginalTaxEdit? edit, IReadOnlyList<int> changed)
    {
        DestinationPath = path; TemplateSha256 = sourceHash; OutputSha256 = outputHash; Length = length;
        Layout = layout; ProfileId = profile; IsIdentity = identity; TaxEdit = edit; ChangedOffsets = changed;
    }
}

/// <summary>T184/T185: what <see cref="OriginalSaveWriter.WriteDocument"/> wrote. Construction is not native
/// compatibility proof: opening the file in the original game is a separate gate (T185 Done-when 5).</summary>
public sealed class OriginalSaveDocumentWriteReport
{
    public const string ProfileId = "native-state-canonical-v1";
    public string DestinationPath { get; }
    public string OutputSha256 { get; }
    public int Length { get; }
    public OriginalSaveLayout Layout { get; }
    public string PolicyResearchCommit => "f878614";

    internal OriginalSaveDocumentWriteReport(string path, string outputHash, int length, OriginalSaveLayout layout)
    {
        DestinationPath = path; OutputSha256 = outputHash; Length = length; Layout = layout;
    }
}

/// <summary>The document breaks the gate-2 policy; nothing was written.</summary>
public sealed class OriginalSavePolicyException(IReadOnlyList<OriginalSavePolicyViolation> violations)
    : Exception($"The state cannot be written as an original SAV: {string.Join("; ", violations.Take(8).Select(v => $"{v.Rule} at {v.Where}"))}"
        + (violations.Count > 8 ? $" (and {violations.Count - 8} more)." : "."))
{
    public IReadOnlyList<OriginalSavePolicyViolation> Violations { get; } = violations;
}
