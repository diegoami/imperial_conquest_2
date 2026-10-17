using System.Globalization;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;

namespace IC2.Slice.Screens;

/// <summary>One row of the <em>Battle ended</em> table: a unit type's (or the total's) troops.</summary>
/// <param name="Label">The unit type's name, or <c>Total</c>.</param>
/// <param name="WinnerStart">The winner's troops at the start.</param>
/// <param name="WinnerFinish">The winner's survivors.</param>
/// <param name="LoserStart">The loser's troops at the start.</param>
public sealed record BattleEndedRow(string Label, int WinnerStart, int WinnerFinish, int LoserStart);

/// <summary>
/// The contents of the <em>Battle ended</em> window (<c>TBattleOver_InitializeForm</c>), Godot-free (T127), from T125's
/// <see cref="TacticalBattleReport"/>: the winner line, the per-type and total table, and the money and supplies lines
/// as the original words them <strong>[confirmed: code, static, report §9; Wine candidate: probe, item 1]</strong>.
/// </summary>
/// <param name="Title">The window's title.</param>
/// <param name="WinnerLine">The winner line.</param>
/// <param name="WinnerName">The winner's nation name.</param>
/// <param name="LoserName">The loser's nation name.</param>
/// <param name="Rows">One row per unit type, then the total.</param>
/// <param name="MoneyLine">The captured-money line.</param>
/// <param name="SuppliesLine">The captured-supplies line.</param>
public sealed record BattleEndedViewModel(
    string Title,
    string WinnerLine,
    string WinnerName,
    string LoserName,
    IReadOnlyList<BattleEndedRow> Rows,
    string MoneyLine,
    string SuppliesLine)
{
    /// <summary>Observed losses, routing and actual fate, separate from start/end totals.</summary>
    public IReadOnlyList<string> AftermathLines { get; init; } = Array.Empty<string>();

    /// <summary>The table's column headers.</summary>
    public static IReadOnlyList<string> Headers { get; } = new[] { string.Empty, "Winner at start", "Winner at end", "Loser at start" };

    /// <summary>Builds the window's contents.</summary>
    /// <param name="report">The report the write-back produced.</param>
    /// <param name="state">The game after the write-back (for the nations' names).</param>
    /// <param name="ruleset">The ruleset (for the unit types' names).</param>
    /// <returns>The window's contents.</returns>
    public static BattleEndedViewModel FromReport(TacticalBattleReport report, GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var winner = state.NationById(report.WinnerNationId)?.Name ?? report.WinnerNationId;
        var loser = state.NationById(report.LoserNationId)?.Name ?? report.LoserNationId;

        var rows = new List<BattleEndedRow>();
        for (var t = 0; t < ruleset.UnitTypes.Count; t++)
        {
            rows.Add(new BattleEndedRow(
                ruleset.UnitTypes[t].Name,
                At(report.WinnerStartByType, t),
                At(report.WinnerFinishByType, t),
                At(report.LoserStartByType, t)));
        }

        rows.Add(new BattleEndedRow("Total", report.WinnerStartTotal, report.WinnerFinishTotal, report.LoserStartTotal));

        // The original's strings, typo and doubled spaces included (game-design.md, "Write-back"): "no suuplies."
        var money = report.CapturedMoney > 0
            ? $"The army of {winner} captured {report.CapturedMoney.ToString(CultureInfo.InvariantCulture)} talents."
            : $"The army of {winner} captured no money.";
        var supplies = report.CapturedSupplyTons > 0
            ? $"The army of {winner} captured {report.CapturedSupplyTons.ToString(CultureInfo.InvariantCulture)} tons of supplies."
            : $"The army of {winner} captured no suuplies.";

        return new BattleEndedViewModel(
            "Battle ended",
            report.Capped ? $"{loser}'s army withdraws at the half-round cap; {winner}'s army holds."
                : $"{winner}'s  army  defeats  {loser}'s  army.",
            winner,
            loser,
            rows,
            money,
            supplies) { AftermathLines = Explain(report, winner, loser) };
    }

    private static IReadOnlyList<string> Explain(TacticalBattleReport report, string winner, string loser)
    {
        if (report.Aftermath is not { } facts) return Array.Empty<string>();
        var lines = new List<string>();
        void Side(string name, TacticalArmyAftermath side)
        {
            lines.Add($"{name}: {side.ExchangeCasualties} exchange casualties; {side.RoutedUnits} units routed, {side.CascadeUnits} more removed by morale cascade.");
            lines.Add($"{name} unity: {side.UnityBefore} → {side.UnityAfter} ({side.UnityAfter - side.UnityBefore:+0;-0;0}).");
        }
        Side(winner, facts.Winner);
        Side(loser, facts.Loser);
        lines.Add("A routed unit leaves the battle; routing does not mean every troop was killed.");
        if (facts.Winner.RejoinedTroops > 0)
            lines.Add($"{winner}: {facts.Winner.RejoinedTroops} routed troops rejoined the winning army.");
        if (!facts.Loser.ArmyRemoved)
            lines.Add($"{loser}: surviving remnant of {facts.Loser.SurvivingTroops} troops scattered to safety.");
        else if (!facts.RetreatEnabled && !report.Capped)
            lines.Add($"{loser}: defeated army removed. This ruleset disables retreat and rejoining.");
        else if (facts.ScatterFailed)
            lines.Add($"{loser}: army removed; no valid scatter location was available.");
        else
            lines.Add($"{loser}: army removed; no surviving remnant remained.");
        if (facts.PursuitLosses > 0) lines.Add($"Retreat and pursuit losses: {facts.PursuitLosses} troops.");
        if (report.Capped) lines.Add("Cap withdrawal: no ordinary victory rewards or unity change.");
        return lines;
    }

    private static int At(ValueList<int> byType, int index) => index < byType.Count ? byType[index] : 0;
}

