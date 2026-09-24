using System.Text.RegularExpressions;
using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// T73 Done-when line 5 (bug #321/#322): every file in the configured corpus — every <c>.sav</c>
/// across the three save folders (and every <c>releases/&lt;tag&gt;/</c> subtree), plus the DAT — runs
/// through <see cref="SaveNationTable"/>, <see cref="SaveTurnState"/> and <see cref="SaveNewsLog"/>,
/// checking the invariants Done-when lines 1–4 already validate internally (the relation matrix's
/// diagonal/range/symmetry, the turn order's permutation/index/current-nation agreement, and the
/// news log's layout/NUL/ASCII-range), plus one cross-check those parsers don't make themselves: the
/// news log's last date header names the same week, season and year as the 55-byte trailer.
///
/// Discovers files by directory through <see cref="CorpusFileLocator"/> (bug #57's path-independent
/// resolution) rather than the committed <c>expected-corpus-outcomes.json</c> fixture — that fixture,
/// and the sweep that reads it (<c>CorpusSweepTests</c>), belong to T30/T34's Owns list, not this
/// task's. A save that breaks an invariant here is a stop-and-report (a real defect in either the
/// research or a corpus file), never a loosened assertion or a widened bound.
/// </summary>
public class SaveTailSweepTests
{
    // "Week  1      Spring      270BC" (an EXE-generated header) or "Week 1      Spring      270 BC"
    // (the DAT's own scripted seed header, still present verbatim in a handful of early saves whose
    // log has never been appended to since) — see
    // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
    // §Q1 and §Q3. Both variants carry the same week/season/year digits; \s+ / \s* absorb the
    // spacing difference between them.
    private static readonly Regex DateHeaderPattern =
        new(@"^Week\s+(?<week>\d+)\s+(?<season>[A-Za-z]+)\s+(?<year>\d+)\s*BC$", RegexOptions.Compiled);

    private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

    [SkippableFact]
    public void Every_corpus_file_satisfies_the_relation_turn_order_and_news_log_invariants()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var names = CorpusFileLocator.DiscoverFileNames(settings).Keys.ToList();
        Assert.True(names.Count > 0, "The configured corpus has no files to sweep.");

        var checkedCount = 0;
        foreach (var name in names)
        {
            string? path;
            try
            {
                path = CorpusFileLocator.TryResolve(settings, name);
            }
            catch (InvalidOperationException)
            {
                // A name ambiguous across locations (ic2-test-fixtures' T64 hazard, #245) is not this
                // sweep's concern — CorpusFileLocatorTests and CorpusSweepTests own that check.
                continue;
            }
            if (path is null) continue;

            var data = File.ReadAllBytes(path);
            CheckOneFile(name, data);
            checkedCount++;
        }

        Assert.True(checkedCount > 0, "Every discovered corpus file was skipped; nothing was actually checked.");
    }

    private static void CheckOneFile(string name, byte[] data)
    {
        var format = SaveFormat.Detect(data);

        // Done-when 1 (relation matrix) and 2 (the narrowed 27-byte leader): SaveNationTable.Parse
        // validates the diagonal, range and symmetry itself, and throws InvalidDataException if any
        // corpus file violates them — that thrown exception IS this test's failure for this file.
        var nations = SaveNationTable.Parse(data);
        Assert.True(nations.Nations.Count == 16, $"{name}: expected 16 nation records, got {nations.Nations.Count}.");

        // Done-when 3 (turn order): SAV only. SaveTurnState.Parse validates the permutation, the
        // index range and TurnOrder[TurnOrderIndex] == CurrentNationCode itself. The DAT has no
        // trailer at all.
        SaveTurnState? turn = null;
        if (format == SaveFileFormat.Dat)
        {
            Assert.Throws<DatDataNotPresentException>(() => SaveTurnState.Parse(data));
        }
        else
        {
            turn = SaveTurnState.Parse(data);
        }

        // Done-when 4 (news log): layout-against-file-length, NUL-within-61-bytes and
        // ASCII-range are validated inside SaveNewsLog.Parse itself.
        var news = SaveNewsLog.Parse(data);

        // Done-when 5's own cross-check, SAV only (a DAT has no trailer to compare against): the
        // log's LAST date header names the same week/season/year as the trailer — "wherever the log
        // holds a header" (the report finds 54 of 54 in its own sample; every corpus file with any
        // header at all is checked here, not just a fixed 54).
        if (turn is not null)
        {
            string? lastHeader = null;
            foreach (var slot in news.Slots)
                if (DateHeaderPattern.IsMatch(slot))
                    lastHeader = slot;

            if (lastHeader is not null)
            {
                var match = DateHeaderPattern.Match(lastHeader);
                var week = ushort.Parse(match.Groups["week"].Value);
                var year = ushort.Parse(match.Groups["year"].Value);
                var seasonName = match.Groups["season"].Value;
                var seasonCode = Array.IndexOf(SeasonNames, seasonName);

                Assert.True(seasonCode >= 0, $"{name}: news header names unknown season '{seasonName}'.");
                Assert.True(turn.Week == week,
                    $"{name}: news log's last header week {week} != trailer week {turn.Week}.");
                Assert.True(turn.YearBc == year,
                    $"{name}: news log's last header year {year} != trailer year {turn.YearBc}.");
                Assert.True(turn.SeasonCode == seasonCode,
                    $"{name}: news log's last header season '{seasonName}' != trailer season {turn.SeasonName}.");
            }
        }
    }
}
