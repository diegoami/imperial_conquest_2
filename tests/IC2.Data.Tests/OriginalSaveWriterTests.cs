using System.Security.Cryptography;
using Xunit;

namespace IC2.Data.Tests;

public sealed class OriginalSaveWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ic2-writer-{Guid.NewGuid():N}");
    public OriginalSaveWriterTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private string Template(byte[] bytes)
    {
        var path = Path.Combine(directory, "template.sav"); File.WriteAllBytes(path, bytes); return path;
    }

    [Theory]
    [InlineData(0, 0, -1)] [InlineData(1, 0, 0)] [InlineData(3, 2, 27)] [InlineData(2, 4, 39)]
    public void IdentityPreservesEveryByteAndCompleteCoverage(int armies, int fleets, int news)
    {
        var expected = OriginalSaveWriterFixtureBuilder.Build(armies, fleets, news);
        var template = Template(expected);
        var plan = OriginalSaveWritePlan.IdentityFromTemplate(template);
        var report = OriginalSaveWriter.Write(plan, Path.Combine(directory, "output.sav"));
        Assert.Equal(expected, File.ReadAllBytes(report.DestinationPath));
        Assert.Equal(expected, File.ReadAllBytes(template));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), report.OutputSha256);
        Assert.Equal(report.TemplateSha256, report.OutputSha256);
        Assert.Equal(armies, plan.Layout.ArmyCount); Assert.Equal(fleets, plan.Layout.FleetCount);
        Assert.Equal(news, plan.Layout.NewsIndex);
        Assert.Equal(120369 + armies * 656 + fleets * 26 + (news + 1) * 61, report.Length);
        var position = 0;
        foreach (var region in plan.Layout.Regions)
        {
            Assert.Equal(position, region.Offset); Assert.True(region.Length > 0); position += region.Length;
        }
        Assert.Equal(expected.Length, position);
        Assert.Equal(100958 + armies * 656 + 2 + fleets * 26,
            plan.Layout.Regions.Single(r => r.Name == "nation[0].name").Offset);
        Assert.Equal(668, plan.Layout.Regions.Single(r => r.Name == "nation[15].live owned-city list +048..2E3").Length);
        Assert.Equal(expected.Length - 9, plan.Layout.Regions.Single(r => r.Name == "window geometry").Offset);
        Assert.Equal(armies, SaveArmyTable.Parse(File.ReadAllBytes(report.DestinationPath)).Armies.Count);
        var second = OriginalSaveWriter.Write(plan, Path.Combine(directory, "again.sav"));
        Assert.Equal(expected, File.ReadAllBytes(second.DestinationPath));
    }

    [Fact]
    public void SnapshotIsIndependentOfSubsequentTemplateChanges()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, 0);
        var path = Template(bytes); var plan = OriginalSaveWritePlan.IdentityFromTemplate(path);
        File.WriteAllText(path, "changed after snapshot");
        var report = OriginalSaveWriter.Write(plan, Path.Combine(directory, "snapshot.sav"));
        Assert.Equal(bytes, File.ReadAllBytes(report.DestinationPath));
        Assert.Equal("changed after snapshot", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(-2)] [InlineData(40)] [InlineData(short.MinValue)] [InlineData(short.MaxValue)]
    public void RejectsIllegalSignedNewsIndex(int index)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, -1);
        OriginalSaveWriterFixtureBuilder.Word(bytes, 120312, index);
        Assert.Throws<InvalidDataException>(() => OriginalSaveWritePlan.IdentityFromTemplate(Template(bytes)));
    }

    [Fact]
    public void RejectsPartialWrongFormatTrailingAndBattleTemplates()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, -1);
        foreach (var invalid in new[] { Array.Empty<byte>(), new byte[140706], bytes[..^1], bytes.Concat(new byte[1]).ToArray() })
            Assert.ThrowsAny<Exception>(() => OriginalSaveWritePlan.IdentityFromTemplate(Template(invalid)));
        bytes[^1] = 1;
        Assert.Throws<InvalidDataException>(() => OriginalSaveWritePlan.IdentityFromTemplate(Template(bytes)));
        Assert.Throws<FileNotFoundException>(() => OriginalSaveWritePlan.IdentityFromTemplate(Path.Combine(directory, "missing.sav")));
    }

    [Fact]
    public void RejectsAmbiguousTrailerEvenWhenStrategicFramingFits()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, 39);
        var tail = bytes.Length - 55;
        // Manufacture a second fully valid calendar candidate at the battle-shape offset.
        // It lies in news slots, which must never be decoded to resolve this ambiguity.
        Array.Copy(bytes, tail, bytes, tail - 2105, 55);
        bytes[tail - 2105 + 54] = 1;
        var error = Assert.Throws<InvalidDataException>(() => OriginalSaveWritePlan.IdentityFromTemplate(Template(bytes)));
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(89600, 0)] // empty city name
    [InlineData(89614, 320)] // X one beyond map
    [InlineData(89616, 140)] // Y one beyond map
    public void RejectsMalformedActualTemplateFields(int offset, int value)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, -1);
        OriginalSaveWriterFixtureBuilder.Word(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => OriginalSaveWritePlan.IdentityFromTemplate(Template(bytes)));
    }

    [Fact]
    public void RefusesExistingDestinationAndTemplateAliasesWithoutChangingFiles()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.Build(0, 0, -1);
        var path = Template(bytes); var plan = OriginalSaveWritePlan.IdentityFromTemplate(path);
        var existing = Path.Combine(directory, "existing.sav"); File.WriteAllText(existing, "keep");
        foreach (var target in new[] { path, Path.Combine(directory, ".", "template.sav"), existing, directory })
            Assert.Throws<IOException>(() => OriginalSaveWriter.Write(plan, target));
        if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => OriginalSaveWriter.Write(plan, path.ToUpperInvariant()));
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal("keep", File.ReadAllText(existing));
        Assert.Empty(Directory.GetFiles(directory, ".ic2-*.tmp"));
    }

    [Fact]
    public void FailedTempCreationLeavesNoPartialOutputOrUnrelatedDeletion()
    {
        var plan = OriginalSaveWritePlan.IdentityFromTemplate(Template(OriginalSaveWriterFixtureBuilder.Build(0, 0, -1)));
        var unrelated = Path.Combine(directory, ".ic2-unrelated.tmp"); File.WriteAllText(unrelated, "keep");
        var output = Path.Combine(directory, "missing-parent", "output.sav");
        Assert.Throws<DirectoryNotFoundException>(() => OriginalSaveWriter.Write(plan, output));
        Assert.False(File.Exists(output)); Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public void GeneralStateChangingPlansRemainExplicitlyUnavailable()
    {
        Assert.Contains(OriginalSaveWritePlan.StateChangeBlockers, s => s.Contains("668 bytes"));
        Assert.Contains(OriginalSaveWritePlan.StateChangeBlockers, s => s.Contains("prototype"));
        Assert.Empty(typeof(OriginalSaveWritePlan).GetConstructors());
        Assert.All(typeof(OriginalSaveWritePlan).GetProperties(), p => Assert.False(p.CanWrite));
    }
}

/// <summary>Independent binary oracles for the bounded tax profile; no writer offsets used for expectations.</summary>
public sealed class BoundOriginalTaxTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ic2-tax-{Guid.NewGuid():N}");
    public BoundOriginalTaxTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private string Source(byte[] bytes)
    {
        var path = Path.Combine(directory, "source.sav"); File.WriteAllBytes(path, bytes); return path;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private BoundOriginalEnvelope Read(byte[] bytes) => BoundOriginalEnvelope.Read(Source(bytes), Hash(bytes));
    private OriginalSaveWriteReport Write(BoundOriginalEnvelope envelope, int tax, string name = "output.sav") =>
        OriginalSaveWriter.Write(envelope.WithCurrentHumanTax(tax), Path.Combine(directory, name));

    public static IEnumerable<object[]> Assignments()
    {
        for (var nation = 0; nation < 16; nation++)
            for (var tax = 0; tax <= 40; tax++) yield return new object[] { nation, tax };
    }

    [Theory]
    [MemberData(nameof(Assignments))]
    public void All656TaxAssignmentsPreserveEveryOtherByte(int nation, int tax)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(nation);
        var envelope = Read(bytes);
        var report = Write(envelope, tax);
        var expected = (byte[])bytes.Clone();
        var offset = 100960 + nation * 1172 + 1098;
        OriginalSaveWriterFixtureBuilder.Word(expected, offset, tax);
        Assert.Equal(expected, File.ReadAllBytes(report.DestinationPath));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "source.sav")));
        Assert.Equal(tax == 19, report.IsIdentity);
        Assert.Equal(tax == 19 ? Array.Empty<int>() : new[] { offset }, report.ChangedOffsets);
        Assert.Equal(nation, report.TaxEdit!.Baseline.CurrentNation);
        Assert.Equal(19, report.TaxEdit.OldTax); Assert.Equal(tax, report.TaxEdit.NewTax);
        Assert.Equal(offset, report.TaxEdit.Baseline.TaxOffset);
        Assert.Equal(BoundOriginalEnvelope.ProfileId, report.ProfileId);
        Assert.Equal(OriginalSaveLayout.TaxResearchCommit, report.ResearchCommit);
        Assert.Equal(Hash(expected), report.OutputSha256);
        Assert.Contains("unestablished", report.EvidenceLimits);
        Assert.Equal(tax, SaveNationTable.Parse(expected).Nations[nation].TaxRatePercent);
    }

    [Theory]
    [InlineData(0,0,-1)] [InlineData(1,0,0)] [InlineData(3,2,27)] [InlineData(2,4,39)]
    public void RelocatedSourceFramingPreservesUnknownHistoryInactiveAndTextResidue(int armies, int fleets, int news)
    {
        var source = OriginalSaveWriterFixtureBuilder.TaxProfile(15, armies, fleets, news);
        var start = 100960 + 656 * armies + 26 * fleets;
        var expected = (byte[])source.Clone();
        OriginalSaveWriterFixtureBuilder.Word(expected, start + 15 * 1172 + 0x44A, 40);
        var envelope = Read(source);
        var result = Write(envelope, 40);
        Assert.Equal(expected, File.ReadAllBytes(result.DestinationPath));
        Assert.Equal(expected, File.ReadAllBytes(Write(envelope, 40, "again.sav").DestinationPath));
        Assert.Equal(armies, result.Layout.ArmyCount); Assert.Equal(fleets, result.Layout.FleetCount);
        Assert.Equal(news, result.Layout.NewsIndex);
        Assert.Equal(Hash(source), Hash(File.ReadAllBytes(Path.Combine(directory, "source.sav"))));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("ABC")] [InlineData("                                                               0")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void RejectsMalformedExpectedHash(string? hash) =>
        Assert.ThrowsAny<ArgumentException>(() => BoundOriginalEnvelope.Read("not-read.sav", hash!));

    [Theory]
    [InlineData(63)] [InlineData(65)]
    public void RejectsAdjacentHashLengthBoundaries(int length) =>
        Assert.Throws<ArgumentException>(() => BoundOriginalEnvelope.Read("not-read.sav", new string('A', length)));

    [Fact]
    public void AlteredPreservedSourceBytesSurviveTheirOwnBoundOperationExactly()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(0, 1, 2, 27);
        var nation = 100960 + 656 + 52;
        foreach (var offset in new[] { 0, 89604, 100958 + 16 + 32 + 6, nation + 0x434,
            nation + 0x44C, nation + 0x450, nation + 0x46D, nation + 0x492, bytes.Length - 9 }) bytes[offset] ^= 0x55;
        var expected = (byte[])bytes.Clone(); OriginalSaveWriterFixtureBuilder.Word(expected, nation + 0x44A, 40);
        var report = Write(Read(bytes), 40);
        Assert.Equal(expected, File.ReadAllBytes(report.DestinationPath));
        Assert.Equal(Hash(bytes), report.TemplateSha256);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "source.sav")));
    }

    [Fact]
    public void HashBindsActualBytesCaseInsensitivelyAndNeverSilentlyRebinds()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(); var path = Source(bytes);
        var hash = Hash(bytes);
        Assert.Equal(hash, BoundOriginalEnvelope.Read(path, hash.ToLowerInvariant()).SourceSha256);
        Assert.Equal(hash, BoundOriginalEnvelope.Read(path, hash.ToUpperInvariant()).SourceSha256);
        Assert.Throws<InvalidDataException>(() => BoundOriginalEnvelope.Read(path, new string('0', 64)));
        bytes[0] ^= 1; File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => BoundOriginalEnvelope.Read(path, hash));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void RetainedSourceAndPlansDoNotFollowFileOrOtherPlanChanges()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(); var envelope = Read(bytes);
        var first = envelope.WithCurrentHumanTax(0); var second = envelope.WithCurrentHumanTax(40);
        File.WriteAllText(Path.Combine(directory, "source.sav"), "changed after import");
        foreach (var (plan, tax, name) in new[] { (first, 0, "first.sav"), (second, 40, "second.sav") })
        {
            var expected = (byte[])bytes.Clone(); OriginalSaveWriterFixtureBuilder.Word(expected, 102058, tax);
            var report = OriginalSaveWriter.Write(plan, Path.Combine(directory, name));
            Assert.Equal(expected, File.ReadAllBytes(report.DestinationPath));
            Assert.Equal(Hash(bytes), report.TemplateSha256);
        }
        Assert.Equal(19, envelope.Snapshot.SourceTax);
        Assert.Equal("changed after import", File.ReadAllText(Path.Combine(directory, "source.sav")));
        foreach (var type in new[] { typeof(BoundOriginalEnvelope), typeof(BoundOriginalSnapshot), typeof(BoundOriginalTaxEdit), typeof(OriginalSaveWritePlan) })
        {
            Assert.True(type.IsSealed); Assert.Empty(type.GetConstructors());
            Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        }
        var editMethod = typeof(BoundOriginalEnvelope).GetMethod("WithCurrentHumanTax")!;
        Assert.Equal(new[] { typeof(int) }, editMethod.GetParameters().Select(p => p.ParameterType));
    }

    [Theory]
    [InlineData(-1)] [InlineData(41)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void RejectsTaxOutsideDomainBeforePublication(int tax)
    {
        var envelope = Read(OriginalSaveWriterFixtureBuilder.TaxProfile());
        Assert.Throws<ArgumentOutOfRangeException>(() => envelope.WithCurrentHumanTax(tax));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(0x490,0)] [InlineData(0x490,2)] [InlineData(0x490,255)]
    [InlineData(0x44A,41)] [InlineData(0x44A,65535)]
    [InlineData(0x47C,270)] [InlineData(0x47C,1296)]
    [InlineData(0x47A,268)] [InlineData(0x47A,1006)]
    [InlineData(0x488,-1)] [InlineData(0x486,-1)] [InlineData(0x488,307)] [InlineData(0x486,127)]
    public void RejectsProtectedProfileFieldsWithoutRepair(int relative, int value)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile();
        OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + relative, value);
        Assert.Throws<InvalidDataException>(() => Read(bytes));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "source.sav")));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(0)] [InlineData(11)] [InlineData(15)] [InlineData(65534)]
    public void RejectsPendingOfferRatherThanClearing(int pending)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile();
        OriginalSaveWriterFixtureBuilder.Word(bytes, bytes.Length - 23, pending);
        Assert.Throws<InvalidDataException>(() => Read(bytes));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "source.sav")));
    }

    [Theory]
    [InlineData(271,269)] [InlineData(1295,1005)]
    public void AcceptsSizeAndViewportBoundariesWithoutChangingGeometry(int width, int height)
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile();
        OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x47C, width);
        OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x47A, height);
        foreach (var maximum in new[] { false, true })
        {
            var x = maximum ? 319 - (width - 15) / 32 : 0;
            var y = maximum ? 139 - (height - 45) / 32 : 0;
            OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x488, x);
            OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x486, y);
            var report = Write(Read(bytes), 0, maximum ? "max.sav" : "min.sav");
            var expected = (byte[])bytes.Clone(); OriginalSaveWriterFixtureBuilder.Word(expected, 102058, 0);
            Assert.Equal(expected, File.ReadAllBytes(report.DestinationPath));
            OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x488, 320 - (width - 15) / 32);
            Assert.Throws<InvalidDataException>(() => Read(bytes));
            OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x488, x);
            OriginalSaveWriterFixtureBuilder.Word(bytes, 100960 + 0x486, 140 - (height - 45) / 32);
            Assert.Throws<InvalidDataException>(() => Read(bytes));
        }
    }

    [Fact]
    public void Opaque491ReaderRestrictionIsRefusalNeverZeroing()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(); bytes[100960 + 0x491] = 0x7E;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "source.sav")));
    }

    [Fact]
    public void BoundImportRejectsMalformedTrailerAndShape()
    {
        var original = OriginalSaveWriterFixtureBuilder.TaxProfile();
        foreach (var relative in new[] { 0,36,38,40,42,44,54 })
        {
            var bytes = (byte[])original.Clone();
            if (relative == 54) bytes[^1] = 1;
            else OriginalSaveWriterFixtureBuilder.Word(bytes, bytes.Length - 55 + relative, 65535);
            Assert.ThrowsAny<Exception>(() => Read(bytes));
        }
        foreach (var invalid in new[] { original[..^1], original.Concat(new byte[1]).ToArray(), new byte[140706] })
            Assert.ThrowsAny<Exception>(() => Read(invalid));
    }

    private string TempName(string output, int attempt)
    {
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(output))))[..16];
        return Path.Combine(directory, $".ic2-{key}-{attempt}.tmp");
    }

    [Fact]
    public void DeterministicTemporaryCollisionsNeverDeleteForeignArtifacts()
    {
        var envelope = Read(OriginalSaveWriterFixtureBuilder.TaxProfile()); var output = Path.Combine(directory, "output.sav");
        for (var i = 0; i < 64; i++) File.WriteAllText(TempName(output, i), $"foreign{i}");
        Assert.Throws<IOException>(() => Write(envelope, 40));
        Assert.False(File.Exists(output));
        for (var i = 0; i < 64; i++) Assert.Equal($"foreign{i}", File.ReadAllText(TempName(output, i)));
        File.Delete(TempName(output, 63));
        Write(envelope, 40);
        Assert.False(File.Exists(TempName(output, 63)));
        Assert.Equal("foreign0", File.ReadAllText(TempName(output, 0)));
    }

    [Fact]
    public async Task ConcurrentPublicationHasExactlyOneWinnerAndNoPartialOrOrphan()
    {
        var envelope = Read(OriginalSaveWriterFixtureBuilder.TaxProfile());
        using var start = new Barrier(2);
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)));
            try { Write(envelope, 40); return true; } catch (IOException) { return false; }
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, success => success);
        var expected = OriginalSaveWriterFixtureBuilder.TaxProfile(); OriginalSaveWriterFixtureBuilder.Word(expected, 102058, 40);
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(directory, "output.sav")));
        Assert.Empty(Directory.GetFiles(directory, ".ic2-*.tmp"));
    }

    [Fact]
    public void ExistingHardlinkAliasIsRefused()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(); var envelope = Read(bytes);
        var source = Path.Combine(directory, "source.sav"); var hardlink = Path.Combine(directory, "hard.sav");
        var linked = OperatingSystem.IsWindows() ? CreateHardLink(hardlink, source, IntPtr.Zero) : Link(source, hardlink) == 0;
        Assert.True(linked, $"Hardlink creation failed with {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        Assert.Throws<IOException>(() => Write(envelope, 0, "hard.sav"));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [SkippableFact]
    public void SupportedSymlinkAndDanglingAliasesAreRefused()
    {
        var bytes = OriginalSaveWriterFixtureBuilder.TaxProfile(); var envelope = Read(bytes);
        var source = Path.Combine(directory, "source.sav");
        try { File.CreateSymbolicLink(Path.Combine(directory, "symbol.sav"), source); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException
            || error is IOException && (error.HResult & 0xFFFF) == 1314)
        { Skip.If(true, $"Symlink unavailable: {error.GetType().Name}"); }
        Assert.Throws<IOException>(() => Write(envelope, 0, "symbol.sav"));
        File.CreateSymbolicLink(Path.Combine(directory, "dangling.sav"), Path.Combine(directory, "absent.sav"));
        Assert.Throws<IOException>(() => Write(envelope, 0, "dangling.sav"));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string path);
}
