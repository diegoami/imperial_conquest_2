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
        Assert.Equal(668, plan.Layout.Regions.Single(r => r.Name == "nation[15].unmapped runtime +048..2E3").Length);
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
    public void StateChangingPlansRemainExplicitlyUnavailable()
    {
        Assert.Contains(OriginalSaveWritePlan.StateChangeBlockers, s => s.Contains("668 bytes"));
        Assert.Contains(OriginalSaveWritePlan.StateChangeBlockers, s => s.Contains("prototype"));
        Assert.Empty(typeof(OriginalSaveWritePlan).GetConstructors());
        Assert.All(typeof(OriginalSaveWritePlan).GetProperties(), p => Assert.False(p.CanWrite));
    }
}
