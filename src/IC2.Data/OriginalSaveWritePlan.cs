using System.Security.Cryptography;

namespace IC2.Data;

/// <summary>Immutable identity-only groundwork for T184. This is deliberately not a domain export
/// API: complete typed live-field plans require evaluated research issue 37. No arbitrary byte
/// patches, user-supplied binary records, or invented defaults are accepted.</summary>
public sealed class OriginalSaveWritePlan
{
    private readonly byte[] template;
    internal string TemplatePath { get; }
    public string TemplateSha256 { get; }
    public OriginalSaveLayout Layout { get; }
    public bool IsIdentity => true;

    /// <summary>Precise known blockers for future state-changing plans. Unknown does not mean static.</summary>
    public static IReadOnlyList<string> StateChangeBlockers { get; } = Array.AsReadOnly(new[]
    {
        "Each nation +0x048..0x2E3 (668 bytes): runtime meaning/preservation validity unproven.",
        "Each nation +0x434..437, +0x43C..43F, +0x448..449, +0x44E..44F: bookkeeping policy unresolved.",
        "Each nation +0x450..0x493: preferences/control preservation and supplied-field policy unresolved.",
        "New army/fleet/unit/mercenary/news records: validated prototype/default and residue policies required.",
        "Complete checked live-field schema and relocation preservation masks await evaluated research #37."
    });

    private OriginalSaveWritePlan(string path, byte[] bytes)
    {
        TemplatePath = path;
        template = bytes;
        Layout = OriginalSaveLayout.Validate(template);
        TemplateSha256 = Convert.ToHexString(SHA256.HashData(template));
    }

    /// <summary>Reads an explicit private SAV path once into an operation-owned snapshot. The input
    /// is never written. No JSON, embedded fallback, DAT conversion or zero-filled template.</summary>
    public static OriginalSaveWritePlan IdentityFromTemplate(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        var path = Path.GetFullPath(templatePath);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // The largest legal framing is two UInt16-count tables and 40 news slots.
        const int maximumLength = 100956 + 4 + 65535 * (656 + 26) + 18752 + 600 + 2 + 2440 + 55;
        if (input.Length > maximumLength) throw new InvalidDataException("Template exceeds maximum SAV framing.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return new(path, bytes);
    }

    internal byte[] CopyBytes() => (byte[])template.Clone();
}
