using System.Buffers.Binary;
using System.Security.Cryptography;

namespace IC2.Data;

/// <summary>Immutable identity or bound-current-human tax plan. No general native-state exporter.</summary>
public sealed class OriginalSaveWritePlan
{
    private readonly byte[] template;
    internal string TemplatePath { get; }
    public string TemplateSha256 { get; }
    public OriginalSaveLayout Layout { get; }
    public BoundOriginalTaxEdit? TaxEdit { get; }
    public bool IsIdentity => TaxEdit is null || TaxEdit.OldTax == TaxEdit.NewTax;
    public string ProfileId => TaxEdit is null ? "original-strategic-identity-v1" : BoundOriginalEnvelope.ProfileId;

    /// <summary>Remaining blockers for general native-state export, outside the bounded tax profile.</summary>
    public static IReadOnlyList<string> StateChangeBlockers { get; } = Array.AsReadOnly(new[]
    {
        "Each nation +0x048..0x2E3 (668 bytes): live owned-city list needs consistent native mapping.",
        "Each nation +0x434..437, +0x43C..43F, +0x448..449, +0x44E..44F: authentic historical applicability required.",
        "31 UNKNOWN nation bytes +0x450..46A/+46D/+491..493: general changed-operation conservation unproved; tax-only exception is source-bound.",
        "Preferences/control +0x46B..0x490 need a validated live-state/profile mapping outside the bounded tax profile.",
        "New army/fleet/unit/mercenary/news records: validated prototype/default and residue policies required.",
        "Complete checked native live-field schema and relocation preservation remain outside this milestone."
    });

    private OriginalSaveWritePlan(string path, byte[] bytes, OriginalSaveLayout layout,
        string hash, BoundOriginalTaxEdit? edit = null)
    {
        TemplatePath = path; template = bytes; Layout = layout; TemplateSha256 = hash; TaxEdit = edit;
    }

    /// <summary>Reads an explicit private SAV once. Retains its own snapshot; never writes the source.</summary>
    public static OriginalSaveWritePlan IdentityFromTemplate(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        var path = Path.GetFullPath(templatePath);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        const int maximumLength = 100956 + 4 + 65535 * (656 + 26) + 18752 + 600 + 2 + 2440 + 55;
        if (input.Length > maximumLength) throw new InvalidDataException("Template exceeds maximum SAV framing.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return new(path, bytes, OriginalSaveLayout.Validate(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal OriginalSaveWritePlan WithTax(BoundOriginalSnapshot snapshot, int tax) =>
        new(TemplatePath, template, Layout, TemplateSha256, new(snapshot, tax));

    internal BoundOriginalSnapshot ReadTaxSnapshot() => BoundOriginalSnapshot.Read(template, Layout, TemplateSha256);

    internal byte[] Materialize()
    {
        var bytes = (byte[])template.Clone();
        if (TaxEdit is not null)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(TaxEdit.Baseline.TaxOffset, 2), checked((ushort)TaxEdit.NewTax));
        ValidateOutput(bytes);
        return bytes;
    }

    // This mask is fixed by the internally minted operation, never supplied by a caller.
    internal void ValidateOutput(byte[] bytes)
    {
        if (bytes.Length != template.Length) throw new InvalidDataException("Output length changed.");
        var taxOffset = TaxEdit?.Baseline.TaxOffset ?? -2;
        for (var i = 0; i < template.Length; i++)
            if (i != taxOffset && i != taxOffset + 1 && bytes[i] != template[i])
                throw new InvalidDataException($"Unowned byte changed at 0x{i:X}.");
        if (TaxEdit is not null && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(taxOffset, 2)) != TaxEdit.NewTax)
            throw new InvalidDataException("Tax output mismatch.");
        if (IsIdentity && !bytes.AsSpan().SequenceEqual(template))
            throw new InvalidDataException("Identity snapshot mismatch.");
        OriginalSaveLayout.Validate(bytes);
    }
}

/// <summary>Original bytes plus the only supported typed edit. Byte binding is not source authenticity
/// or GUI/native acceptance proof, and does not bind an unrelated engine session or native JSON.</summary>
public sealed class BoundOriginalEnvelope
{
    public const string ProfileId = "bound-original-current-human-tax-v1";
    private readonly OriginalSaveWritePlan baseline;
    public string SourceSha256 => baseline.TemplateSha256;
    public OriginalSaveLayout Layout => baseline.Layout;
    public BoundOriginalSnapshot Snapshot { get; }
    private BoundOriginalEnvelope(OriginalSaveWritePlan baseline)
    {
        this.baseline = baseline;
        Snapshot = baseline.ReadTaxSnapshot();
    }

    public static BoundOriginalEnvelope Read(string sourcePath, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(expectedSha256);
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 must have exactly 64 hexadecimal digits.", nameof(expectedSha256));
        var plan = OriginalSaveWritePlan.IdentityFromTemplate(sourcePath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(plan.TemplateSha256, expectedSha256))
            throw new InvalidDataException("Source SHA-256 mismatch; refusing to rebind.");
        return new(plan);
    }

    /// <summary>No nation argument, editable journal, other mutation or progression is accepted.</summary>
    public OriginalSaveWritePlan WithCurrentHumanTax(int newTax)
    {
        if (newTax is < 0 or > 40) throw new ArgumentOutOfRangeException(nameof(newTax), "Tax must be 0..40.");
        return baseline.WithTax(Snapshot, newTax);
    }
}

/// <summary>Getter-only metadata derived from the same retained bytes as the operation.</summary>
public sealed class BoundOriginalSnapshot
{
    public string SourceSha256 { get; }
    public int CurrentNation { get; }
    public int SourceTax { get; }
    public int TaxOffset { get; }
    public int Controller { get; }
    public int PendingOfferSource { get; }
    public int Width { get; }
    public int Height { get; }
    public int ViewportX { get; }
    public int ViewportY { get; }
    public int TurnOrderIndex { get; }
    public int Week { get; }
    public int YearBc { get; }
    public int Season { get; }

    private BoundOriginalSnapshot(byte[] bytes, OriginalSaveLayout layout, string hash)
    {
        int Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        int Signed(int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
        SourceSha256 = hash;
        var tail = layout.TrailerOffset;
        CurrentNation = Word(tail + 36);
        if (CurrentNation > 15) throw new InvalidDataException("Current nation outside 0..15.");
        var nation = checked(layout.NationTableOffset + 1172 * CurrentNation);
        Controller = bytes[nation + 0x490];
        if (Controller != 1) throw new InvalidDataException("Tax profile requires current controller BYTE 1.");
        TaxOffset = nation + 0x44A;
        SourceTax = Word(TaxOffset);
        if (SourceTax > 40) throw new InvalidDataException("Source tax outside conservative 0..40 profile.");
        PendingOfferSource = Signed(tail + 32);
        if (PendingOfferSource != -1) throw new InvalidDataException("Tax profile requires pending-offer source -1.");
        Width = Signed(nation + 0x47C); Height = Signed(nation + 0x47A);
        ViewportX = Signed(nation + 0x488); ViewportY = Signed(nation + 0x486);
        if (Width is < 271 or > 1295 || Height is < 269 or > 1005)
            throw new InvalidDataException("Saved size outside conservative tax research profile (not original maximum dimensions).");
        var x = Math.Max(0, Math.Min(ViewportX, 319 - (Width - 15) / 32));
        var y = Math.Max(0, Math.Min(ViewportY, 139 - (Height - 45) / 32));
        if (x != ViewportX || y != ViewportY)
            throw new InvalidDataException("Tax refresh would change viewport; refusing UI repair.");
        TurnOrderIndex = Word(tail + 38); Week = Word(tail + 40); YearBc = Word(tail + 42); Season = Word(tail + 44);
    }

    internal static BoundOriginalSnapshot Read(byte[] bytes, OriginalSaveLayout layout, string hash) => new(bytes, layout, hash);
}

/// <summary>Internally minted tax delta and its exact immutable source baseline. No public constructor.</summary>
public sealed class BoundOriginalTaxEdit
{
    public BoundOriginalSnapshot Baseline { get; }
    public string SourceSha256 => Baseline.SourceSha256;
    public int OldTax => Baseline.SourceTax;
    public int NewTax { get; }
    internal BoundOriginalTaxEdit(BoundOriginalSnapshot baseline, int tax) { Baseline = baseline; NewTax = tax; }
}
