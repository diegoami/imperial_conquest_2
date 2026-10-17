namespace IC2.Data;

/// <summary>Non-proprietary publication metadata. Identity is not a working domain-export claim.</summary>
public sealed record OriginalSaveWriteReport(string DestinationPath, string TemplateSha256,
    string OutputSha256, int Length, OriginalSaveLayout Layout)
{
    public bool IsIdentity => true;
    public string PreservationReason =>
        "Every byte preserved: identity plan retains the identical strategic state, including all opaque runtime bytes and string residue. This reason is invalid after state changes.";
}
