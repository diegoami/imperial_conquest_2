using System.Text;
using IC2.Engine.Battle.Candidates.Tournament;

namespace AutoResolveTournament;

/// <summary>Renders the deterministic scorecard as Markdown tables, candidates in the neutral C1–C5 key order.</summary>
internal static class Markdown
{
    public static string Render(IReadOnlyList<CandidateScorecard> cards, IReadOnlyList<string> extraRows)
    {
        var sb = new StringBuilder();
        var ids = cards[0].Clauses.Select(c => c.Id).ToList();

        sb.AppendLine("| Clause | Band | " + string.Join(" | ", cards.Select(c => c.Key)) + " |");
        sb.AppendLine("| --- | --- | " + string.Join(" | ", cards.Select(_ => "---")) + " |");
        foreach (var id in ids)
        {
            var band = cards[0].Clauses.First(c => c.Id == id).Band;
            sb.Append("| ").Append(id).Append(" | ").Append(band).Append(" | ");
            sb.Append(string.Join(" | ", cards.Select(card =>
            {
                var clause = card.Clauses.First(c => c.Id == id);
                return clause.Verdict == "n/a" ? "n/a" : $"{clause.Value} — **{clause.Verdict}**";
            })));
            sb.AppendLine(" |");
        }

        foreach (var row in extraRows)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("Per-composition score s(A), T-scale / P-scale:");
        sb.AppendLine();
        sb.AppendLine("| Composition | " + string.Join(" | ", cards.Select(c => c.Key + " s_T / s_P")) + " |");
        sb.AppendLine("| --- | " + string.Join(" | ", cards.Select(_ => "---")) + " |");
        for (var i = 0; i < TestArmies.Compositions.Count; i++)
        {
            sb.Append("| ").Append(TestArmies.Compositions[i].Label).Append(" | ");
            sb.Append(string.Join(" | ", cards.Select(c => CandidateScorecard.F(c.ScoreT[i], "0.000") + " / " + CandidateScorecard.F(c.ScoreP[i], "0.000"))));
            sb.AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("CD-b clear-top type in the uniform winner, per opponent:");
        sb.AppendLine();
        sb.AppendLine("| Opponent | " + string.Join(" | ", cards.Select(c => c.Key)) + " |");
        sb.AppendLine("| --- | " + string.Join(" | ", cards.Select(_ => "---")) + " |");
        for (var i = 0; i < TestArmies.Compositions.Count; i++)
        {
            sb.Append("| ").Append(TestArmies.Compositions[i].Label).Append(" | ");
            sb.Append(string.Join(" | ", cards.Select(c => c.ClearTops[i])));
            sb.AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("Endings over the P-scale schedule, and mean σ by ending (§8.8 diagnostic, no band):");
        sb.AppendLine();
        sb.AppendLine("| Ending | " + string.Join(" | ", cards.Select(c => c.Key + " share / mean σ")) + " |");
        sb.AppendLine("| --- | " + string.Join(" | ", cards.Select(_ => "---")) + " |");
        foreach (var ending in cards[0].EndingFractions.Keys)
        {
            sb.Append("| ").Append(ending).Append(" | ");
            sb.Append(string.Join(" | ", cards.Select(c =>
            {
                var share = CandidateScorecard.F(c.EndingFractions[ending]);
                return c.SigmaByEnding.TryGetValue(ending, out var s) ? $"{share} / {CandidateScorecard.F(s.MeanSigma)}" : share;
            })));
            sb.AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("Diagnostics (no band):");
        sb.AppendLine();
        sb.AppendLine("| Diagnostic | " + string.Join(" | ", cards.Select(c => c.Key)) + " |");
        sb.AppendLine("| --- | " + string.Join(" | ", cards.Select(_ => "---")) + " |");
        foreach (var key in cards[0].Diagnostics.Keys)
        {
            sb.Append("| ").Append(key).Append(" | ");
            sb.Append(string.Join(" | ", cards.Select(c => c.Diagnostics[key])));
            sb.AppendLine(" |");
        }

        return sb.ToString();
    }
}
