using System.Security.Cryptography;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>T184 Done-when 5 for <see cref="OriginalSaveWriter.WriteDocument"/>: policy and encoding refuse
/// before anything is written; publication is exclusive and never replaces a file; output is deterministic.</summary>
public sealed class OriginalSaveDocumentWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ic2-doc-writer-{Guid.NewGuid():N}");

    public OriginalSaveDocumentWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_valid_document_is_published_and_identical_documents_give_identical_files()
    {
        var first = OriginalSaveWriter.WriteDocument(OriginalSaveDocumentFixture.Build(), Path.Combine(_dir, "a.sav"));
        var second = OriginalSaveWriter.WriteDocument(OriginalSaveDocumentFixture.Build(), Path.Combine(_dir, "b.sav"));
        var bytes = File.ReadAllBytes(first.DestinationPath);
        Assert.Equal(bytes, File.ReadAllBytes(second.DestinationPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), first.OutputSha256);
        Assert.Equal(bytes.Length, first.Length);
        Assert.Equal(new[] { "a.sav", "b.sav" }, Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void An_existing_destination_is_refused_and_left_unchanged()
    {
        var path = Path.Combine(_dir, "taken.sav");
        File.WriteAllText(path, "mine");
        Assert.Throws<IOException>(() => OriginalSaveWriter.WriteDocument(OriginalSaveDocumentFixture.Build(), path));
        Assert.Equal("mine", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void A_policy_violation_writes_nothing_and_names_the_rule()
    {
        var path = Path.Combine(_dir, "bad.sav");
        var ex = Assert.Throws<OriginalSavePolicyException>(() => OriginalSaveWriter.WriteDocument(
            OriginalSaveDocumentFixture.Build().WithCity(0, c => c with { Allegiance = 16 }), path));
        Assert.Contains(ex.Violations, v => v.Rule == "city_allegiance_in_range" && v.Where == "city[0]");
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void An_unrepresentable_value_writes_nothing()
    {
        var path = Path.Combine(_dir, "long.sav");
        Assert.Throws<OriginalSaveEncodingException>(() => OriginalSaveWriter.WriteDocument(
            OriginalSaveDocumentFixture.Build().WithNation(1, n => n with { Name = "A very long name" }), path));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
