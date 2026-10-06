using System.Text;
using IC2.Data;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T146 Done-when 1: the DAT's leader-name pool (<c>0x2089A</c>, 16 nations × 12 candidates × 26 bytes)
/// parses to 16 lists of 12 non-empty names, and every one of the 192 slots is NUL-terminated within its
/// own 26 bytes — the property <c>FUN_00448aa4</c>'s <c>strcpy(record + 0x0b, leaderPool + i * 0x1a)</c>
/// relies on. Skipped without the original files, like the other DAT tests.
/// </summary>
public class DatLeaderPoolTests
{
    // The report's own confirmed pool offset (docs/investigations/dat-file-layout.md,
    // "leader-name pool, 16 × 12 × 26 bytes"). DatLayout holds it internally; a test is allowed to pin
    // the documented figure directly, the same way the offset is quoted in the report.
    private const int PoolStart = 0x2089A;
    private const int NameRecordLength = 26;

    [SkippableFact]
    public void Parses_16_nations_of_12_non_empty_names()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var pool = DatLeaderPool.Parse(data);

        Assert.Equal(16, pool.Pools.Count);
        for (var nation = 0; nation < pool.Pools.Count; nation++)
        {
            Assert.Equal(12, pool[nation].Count);
            for (var candidate = 0; candidate < pool[nation].Count; candidate++)
            {
                Assert.False(string.IsNullOrWhiteSpace(pool[nation][candidate]),
                    $"nation {nation} candidate {candidate} is empty.");
            }
        }
    }

    /// <summary>
    /// The property the parser enforces, asserted directly on the raw bytes rather than through the
    /// parser's own guard: every 26-byte slot contains a NUL, and no slot is empty.
    /// </summary>
    [SkippableFact]
    public void Every_pool_record_is_NUL_terminated_within_26_bytes()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        for (var nation = 0; nation < 16; nation++)
        {
            for (var candidate = 0; candidate < 12; candidate++)
            {
                var offset = PoolStart + ((nation * 12 + candidate) * NameRecordLength);
                var nul = Array.IndexOf(data, (byte)0, offset, NameRecordLength);
                Assert.True(nul > offset,
                    $"nation {nation} candidate {candidate} at {offset:X} is empty or has no NUL within {NameRecordLength} bytes.");
                Assert.True(nul < offset + NameRecordLength, "the NUL must be inside the record, not the next one.");
            }
        }
    }

    /// <summary>
    /// The parsed names match the DAT's own bytes at the pinned offsets — a name read out of the wrong
    /// stride would differ here even if the pool's overall shape still looked right.
    /// </summary>
    [SkippableFact]
    public void The_parsed_names_match_the_raw_bytes_at_the_pinned_offset()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);
        var pool = DatLeaderPool.Parse(data);

        var offset = PoolStart + (3 * 12 * NameRecordLength) + (5 * NameRecordLength); // Ptolemaic, index 5.
        var nul = Array.IndexOf(data, (byte)0, offset, NameRecordLength);
        var raw = Encoding.ASCII.GetString(data, offset, nul - offset).Trim();

        Assert.Equal("Thutmose", raw);
        Assert.Equal(raw, pool[3][5]);
    }
}
