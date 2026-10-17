using IC2.Engine.Battle.Tactical;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T127.md</c> Done-when 1: the <em>Battle ended</em> window's table and wording, on the report T125's
/// session produced from a scripted battle, equal the engine's values.
/// </summary>
public sealed class BattleEndedViewModelTests
{
    private static (TacticalBattleReport Report, GameSession Session) Ended()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        var output = TacticalSessionTestbed.PlayUntilEnded(session, TacticalSessionTestbed.WonBattleScript);
        return (Assert.IsType<TacticalBattleReport>(output.EndedBattleReport), session);
    }

    [Fact]
    public void The_table_has_one_row_per_unit_type_and_a_total_equal_to_the_reports_numbers()
    {
        var (report, session) = Ended();
        var model = BattleEndedViewModel.FromReport(report, session.State, session.Ruleset);

        Assert.Equal("Battle ended", model.Title);
        Assert.Equal(session.Ruleset.UnitTypes.Count + 1, model.Rows.Count);
        for (var t = 0; t < session.Ruleset.UnitTypes.Count; t++)
        {
            Assert.Equal(
                new BattleEndedRow(
                    session.Ruleset.UnitTypes[t].Name,
                    report.WinnerStartByType[t],
                    report.WinnerFinishByType[t],
                    report.LoserStartByType[t]),
                model.Rows[t]);
        }

        Assert.Equal(new BattleEndedRow("Total", report.WinnerStartTotal, report.WinnerFinishTotal, report.LoserStartTotal), model.Rows[^1]);
        Assert.Equal(report.WinnerStartTotal, model.Rows.Take(model.Rows.Count - 1).Sum(r => r.WinnerStart));
        Assert.Equal(report.WinnerFinishTotal, model.Rows.Take(model.Rows.Count - 1).Sum(r => r.WinnerFinish));
        Assert.Equal(report.LoserStartTotal, model.Rows.Take(model.Rows.Count - 1).Sum(r => r.LoserStart));
    }

    [Fact]
    public void The_winner_line_and_the_goods_lines_use_the_originals_words()
    {
        var (report, session) = Ended();
        var winner = session.State.NationById(report.WinnerNationId)!.Name;
        var loser = session.State.NationById(report.LoserNationId)!.Name;
        var model = BattleEndedViewModel.FromReport(report, session.State, session.Ruleset);

        Assert.Equal($"{winner}'s  army  defeats  {loser}'s  army.", model.WinnerLine);
        Assert.True(report.CapturedMoney > 0, "the scripted loser carried money");
        Assert.Equal($"The army of {winner} captured {report.CapturedMoney} talents.", model.MoneyLine);
        Assert.True(report.CapturedSupplyTons > 0, "the scripted loser carried supplies");
        Assert.Equal($"The army of {winner} captured {report.CapturedSupplyTons} tons of supplies.", model.SuppliesLine);
    }

    [Fact]
    public void Nothing_captured_reads_no_money_and_the_originals_misspelt_no_suuplies()
    {
        var (report, session) = Ended();
        var winner = session.State.NationById(report.WinnerNationId)!.Name;
        var model = BattleEndedViewModel.FromReport(report with { CapturedMoney = 0, CapturedSupplyTons = 0 }, session.State, session.Ruleset);

        Assert.Equal($"The army of {winner} captured no money.", model.MoneyLine);
        Assert.Equal($"The army of {winner} captured no suuplies.", model.SuppliesLine);
    }
    [Fact]
    public void Explanation_uses_captured_unity_and_keeps_casualties_beside_distinct_route_and_cascade_counts()
    {
        var (report, session) = Ended();
        var facts = new TacticalBattleAftermath(
            new(42, 2, 1, 300, 1200, false, 988, 990),
            new(123, 3, 2, 0, 400, false, 400, 375), true, false, 200);
        var model = BattleEndedViewModel.FromReport(report with { Aftermath = facts }, session.State, session.Ruleset);
        Assert.Contains(model.AftermathLines, line => line.Contains("42 exchange casualties; 2 units routed, 1 more removed by morale cascade"));
        Assert.Contains(model.AftermathLines, line => line.Contains("988 → 990 (+2)"));
        Assert.Contains(model.AftermathLines, line => line.Contains("300 routed troops rejoined"));
        Assert.Contains(model.AftermathLines, line => line.Contains("surviving remnant of 400 troops"));
        Assert.Contains(model.AftermathLines, line => line.Contains("pursuit losses: 200"));
    }

    [Fact]
    public void Legacy_report_without_capture_does_not_infer_a_unity_swing_from_final_state()
    {
        var (report, session) = Ended();
        Assert.Empty(BattleEndedViewModel.FromReport(report with { Aftermath = null }, session.State, session.Ruleset).AftermathLines);
    }}

