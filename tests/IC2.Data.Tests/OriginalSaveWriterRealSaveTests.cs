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
            output.WriteLine($"PASS {name} SHA256={hash} armies={plan.Layout.ArmyCount} fleets={plan.Layout.FleetCount} newsIndex={plan.Layout.NewsIndex}; every byte identical. Changed-plan coverage BLOCKED by research #37.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
