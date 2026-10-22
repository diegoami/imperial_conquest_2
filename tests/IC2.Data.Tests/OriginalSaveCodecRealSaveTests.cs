using System.Buffers.Binary;
using IC2.Inspect;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Data.Tests;

/// <summary>
/// T184 Done-when 4 over the whole private corpus: each strategic original SAV decodes, and encoding the
/// decoded document gives exactly the gate-2 canonical rewrite of the original bytes — computed here by an
/// independent canonicaliser with its own literal offsets (after research's
/// <c>scripts/gate2-policy-adversarial.py</c>), never by the codec's own helpers.
/// </summary>
public sealed class OriginalSaveCodecRealSaveTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void Every_strategic_corpus_save_reencodes_to_its_canonical_rewrite()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var names = CorpusFileLocator.DiscoverFileNames(settings).Keys
            .Where(n => n.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Skip.If(names.Count == 0, "No .sav files found in the configured corpus.");

        int equal = 0, battle = 0, refused = 0, policyClean = 0;
        var violations = new Dictionary<string, int>();
        foreach (var name in names)
        {
            var path = CorpusFileLocator.TryResolve(settings, name);
            if (path is null) continue;
            var original = File.ReadAllBytes(path);
            if (original[^1] != 0 || HasBattleBlock(original)) { battle++; continue; }

            OriginalSaveDocument document;
            try { document = OriginalSaveCodec.Decode(original); }
            catch (InvalidDataException ex) { refused++; output.WriteLine($"REFUSED {name}: {ex.Message}"); continue; }

            // With the source's INERT bytes carried, everything else is canonical.
            Assert.Equal(Canonical(original, zeroInert: false), OriginalSaveCodec.Encode(document));
            // From scratch: the INERT bytes are zero.
            var scratch = document with { Nations = document.Nations.Select(n => n with { Inert = null }).ToArray() };
            Assert.Equal(Canonical(original, zeroInert: true), OriginalSaveCodec.Encode(scratch));
            equal++;

            var found = OriginalSaveExportPolicy.Check(scratch, fromScratch: false);
            if (found.Count == 0) policyClean++;
            foreach (var rule in found.Select(f => f.Rule).Distinct()) violations[rule] = violations.GetValueOrDefault(rule) + 1;
        }

        output.WriteLine($"{names.Count} files: {equal} re-encode to their canonical rewrite, {battle} battle saves skipped, {refused} refused by the decoder.");
        output.WriteLine($"policy (source mode): {policyClean} clean; files per broken rule: {string.Join(", ", violations.Select(kv => $"{kv.Key} {kv.Value}"))}");
        Assert.True(equal > 0);
    }

    private static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o, 2));

    private static bool HasBattleBlock(byte[] b)
    {
        var o = 89600 + 334 * 34;
        o += 2 + I16(b, o) * 656;
        o += 2 + I16(b, o) * 26;
        o += 16 * 1172 + 50 * 12;
        o += 2 + (I16(b, o) + 1) * 61;
        return b.Length != o + 55;
    }

    /// <summary>The gate-2 canonical rewrite, from literal offsets: text tails zero, empty unit slots zero,
    /// city lists −1 after their terminator, and (optionally) the INERT nation bytes zero.</summary>
    private static byte[] Canonical(byte[] source, bool zeroInert)
    {
        var b = (byte[])source.Clone();
        void ZeroTail(int o, int n)
        {
            var k = Array.IndexOf(b, (byte)0, o, n);
            if (k >= 0) Array.Clear(b, k, o + n - k);
        }

        var cities = 89600;
        for (var c = 0; c < 334; c++) ZeroTail(cities + 34 * c, 14);
        var armyCount = cities + 334 * 34;
        var armies = I16(b, armyCount);
        for (var k = 0; k < armies; k++)
            for (var s = 0; s < 20; s++)
            {
                var so = armyCount + 2 + 656 * k + 16 + 32 * s;
                if (I16(b, so + 4) > 0) ZeroTail(so + 8, 24);
                else Array.Clear(b, so, 32);
            }

        var fleetCount = armyCount + 2 + 656 * armies;
        var nations = fleetCount + 2 + 26 * I16(b, fleetCount);
        for (var n = 0; n < 16; n++)
        {
            var r = nations + 1172 * n;
            ZeroTail(r, 11);
            ZeroTail(r + 0x0B, 27);
            var ended = false;
            for (var e = 0; e < 334; e++)
            {
                ended |= I16(b, r + 0x48 + 2 * e) == -1;
                if (ended) BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(r + 0x48 + 2 * e, 2), -1);
            }

            if (zeroInert)
            {
                Array.Clear(b, r + 0x450, 27);
                b[r + 0x46D] = 0;
                Array.Clear(b, r + 0x491, 3);
            }
        }

        var newsIndex = nations + 16 * 1172 + 50 * 12;
        for (var i = 0; i <= I16(b, newsIndex); i++) ZeroTail(newsIndex + 2 + 61 * i, 61);
        return b;
    }
}
