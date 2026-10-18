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
