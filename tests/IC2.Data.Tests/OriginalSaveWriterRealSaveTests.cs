using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Data.Tests;

public sealed class OriginalSaveWriterRealSaveTests(ITestOutputHelper output)
{
    // Partial news and three different nations/turns; actual shapes are printed for justification.
    [SkippableTheory]
    [InlineData("1.sav")]
    [InlineData("1_thracia_271_spring_1.sav")]
    [InlineData("1_rome_270_winter_7.sav")]
    [InlineData("11.sav")]
    public void IdentityReadbackPrivateTemplate(string name)
    {
        var source = FixtureResolver.TryResolve(name);
        Skip.If(source is null, $"Private template unavailable: {name}; identity release coverage unavailable.");
        var before = File.ReadAllBytes(source!);
        var hash = Convert.ToHexString(SHA256.HashData(before));
        var plan = OriginalSaveWritePlan.IdentityFromTemplate(source!);
        var directory = Path.Combine(Path.GetTempPath(), $"ic2-real-writer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var result = OriginalSaveWriter.Write(plan, Path.Combine(directory, "identity.sav"));
            Assert.Equal(before, File.ReadAllBytes(result.DestinationPath));
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source!))));
            Assert.Equal(hash, result.OutputSha256);
            output.WriteLine($"PASS {name} SHA256={hash} armies={plan.Layout.ArmyCount} fleets={plan.Layout.FleetCount} newsIndex={plan.Layout.NewsIndex}; every byte identical. General native-state changed-plan coverage remains outside the bounded tax profile.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

public sealed class BoundOriginalTaxRealSaveTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData("1.sav", "D20971D5C73A39C82BB9A6C76413394D1D6FC8174BF5AE857A8161EABA5D7C10", 0, 10, 0)]
    [InlineData("1_thracia_271_spring_1.sav", "C548B724B2D9988AF42D0595436548CD6B29A4021E942F4E61238339F0DC63E3", 15, 9, 40)]
    [InlineData("1_rome_270_winter_7.sav", "7979E22AC6971EEDD36C889DA08B9B1AF27FD4661945F1CE58EAA9B72029BF9A", 0, 20, 30)]
    public void BoundTaxReadbackPrivateTemporaryOutput(string name, string hash, int nation, int oldTax, int newTax)
    {
        var source = FixtureResolver.TryResolve(name);
        Skip.If(source is null, $"Private bound-tax source unavailable: {name}.");
        var before = File.ReadAllBytes(source!); Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(before)));
        var envelope = BoundOriginalEnvelope.Read(source!, hash);
        Assert.Equal(nation, envelope.Snapshot.CurrentNation); Assert.Equal(oldTax, envelope.Snapshot.SourceTax);
        var directory = Path.Combine(Path.GetTempPath(), $"ic2-real-tax-{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
        try
        {
            // Independently locate source records, never use writer Layout or Snapshot for oracle offsets.
            int Word(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan(offset, 2));
            var armies = Word(100956); var fleets = Word(100958 + armies * 656);
            var taxOffset = 100960 + armies * 656 + fleets * 26 + nation * 1172 + 0x44A;
            var expected = (byte[])before.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(taxOffset, 2), (ushort)newTax);
            var report = OriginalSaveWriter.Write(envelope.WithCurrentHumanTax(newTax), Path.Combine(directory, "changed.sav"));
            var actual = File.ReadAllBytes(report.DestinationPath);
            Assert.Equal(expected, actual); Assert.False(report.IsIdentity); Assert.Equal(new[] { taxOffset }, report.ChangedOffsets);
            Assert.Equal(newTax, SaveNationTable.Parse(actual).Nations[nation].TaxRatePercent);
            Assert.Equal(nation, SaveTurnState.Parse(actual).CurrentNationCode); Assert.False(SavePendingOffer.Parse(actual).HasOffer);
            Assert.Equal(armies, SaveArmyTable.Parse(actual).Armies.Count); Assert.Equal(fleets, SaveFleetTable.Parse(actual).Fleets.Count);
            SaveNewsLog.Parse(actual);
            var noOp = OriginalSaveWriter.Write(envelope.WithCurrentHumanTax(oldTax), Path.Combine(directory, "same.sav"));
            Assert.True(noOp.IsIdentity); Assert.Empty(noOp.ChangedOffsets); Assert.Equal(before, File.ReadAllBytes(noOp.DestinationPath));
            Assert.Equal(before, File.ReadAllBytes(source!)); Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source!))));
            output.WriteLine($"PASS TEMPORARY {name} sourceSHA256={hash} outputSHA256={report.OutputSha256} tax={oldTax}->{newTax} nation={nation} taxOffset={taxOffset} armies={armies} fleets={fleets} newsIndex={report.Layout.NewsIndex}; all non-tax bytes exact; source unchanged; native validation UNESTABLISHED. Output removed after test, not retained candidate delivery.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
