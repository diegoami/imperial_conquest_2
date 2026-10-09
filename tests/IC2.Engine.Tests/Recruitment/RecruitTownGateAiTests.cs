using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T155 Done-when 3's AI half: the AI's candidate recruitment cities are filtered through
/// <see cref="RecruitmentEligibility.MayTakeOrder"/>, so it never issues a recruitment the engine
/// refuses — the constraint T22's 50-seed soak asserts as "0 rejected" over whole games, pinned here
/// on the awkward towns the toy world never happens to reach (the same shape
/// <c>AiCommandLegalityTests</c> uses for its own battery).
/// </summary>
public sealed class RecruitTownGateAiTests
{
    private const string Nation = "north";
    private const string Capital = "arx";
    private const string NonCapital = "portus";

    private static Ruleset Ruleset => RecruitmentTestbed.Ruleset;

    private static int Threshold => Ruleset.Recruitment.RecruitTownMinFortificationPercent;

    private static GameState StateWithFort(string cityId, int fortificationCode)
    {
        var state = RecruitmentTestbed.InitialState();
        var city = state.CityById(cityId)!;
        return RecruitmentTestbed.WithCity(state, city with { FortificationCode = fortificationCode });
    }

    /// <summary>The economy phase's own recruitment candidates for <paramref name="state"/>.</summary>
    private static List<RecruitStandingUnitCommand> RecruitCandidates(GameState state)
    {
        var view = new AiView(state, Ruleset, RecruitmentTestbed.World, Nation);
        var personality = AiPersonalityProfile.For(state.NationById(Nation)!, Ruleset);
        var candidates = new List<AiCandidate>();
        AiEconomyPhase.Propose(view, personality, candidates);

        var orders = new List<RecruitStandingUnitCommand>();
        foreach (var candidate in candidates.Where(c => string.Equals(c.Kind, "recruit", StringComparison.Ordinal)))
        {
            foreach (var command in candidate.Commands)
            {
                orders.Add(Assert.IsType<RecruitStandingUnitCommand>(command));
            }
        }

        return orders;
    }

    /// <summary>
    /// Done-when 3, the sharp half: across the awkward towns, every recruitment the AI proposes is
    /// one the engine accepts, and none is proposed in a town the gate refuses.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(74, false)]
    [InlineData(75, true)]
    [InlineData(360, true)] // current 60 + a 3-point order pending: the raw word passes.
    public void The_ai_issues_no_recruitment_the_engine_refuses(int portusWord, bool portusEligible)
    {
        var state = StateWithFort(NonCapital, portusWord);
        var orders = RecruitCandidates(state);

        foreach (var order in orders)
        {
            // The gate's one home decides, and the engine agrees — dispatched through the real
            // dispatcher, not a second copy of the rule.
            var city = state.CityById(order.CityId)!;
            Assert.True(
                RecruitmentEligibility.MayTakeOrder(city, state.NationById(Nation)!, Ruleset),
                $"the AI proposed a recruitment at '{order.CityId}', which MayTakeOrder refuses");

            var result = RecruitmentTestbed.Dispatcher().Dispatch(state, order);
            Assert.True(result.IsAccepted, $"the engine refused the AI's own order: {result.Code} {result.Rejection?.Message}");
        }

        // The capital (arx) is eligible in every state here, so the battery always has candidates to
        // check — the ineligible cases are not vacuous either.
        Assert.NotEmpty(orders);

        if (!portusEligible)
        {
            Assert.DoesNotContain(orders, order => string.Equals(order.CityId, NonCapital, StringComparison.Ordinal));
        }
        else
        {
            // Not vacuous: the eligible side really proposes at the town, and the accepted dispatch
            // above really ran.
            Assert.Contains(orders, order => string.Equals(order.CityId, NonCapital, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The capital is always eligible, whatever its fortification — and the AI may recruit there at
    /// any level (the original's AI recruits at its capital by default).
    /// </summary>
    [Fact]
    public void The_ai_may_recruit_at_the_capital_at_any_fortification()
    {
        var state = StateWithFort(Capital, 10);
        var orders = RecruitCandidates(state);

        var atCapital = orders.Where(o => string.Equals(o.CityId, Capital, StringComparison.Ordinal)).ToList();
        foreach (var order in atCapital)
        {
            Assert.True(RecruitmentTestbed.Dispatcher().Dispatch(state, order).IsAccepted);
        }
    }

    /// <summary>
    /// End to end, through the whole turn: a nation with only an ineligible non-capital town (its
    /// capital lost — no city passes the gate) issues no recruitment at all, and the turn reports no
    /// rejected command. T22 Done-when 1's zero, on the state that would break it.
    /// </summary>
    [Fact]
    public void A_full_ai_turn_in_a_state_of_only_ineligible_towns_reports_no_rejected_command()
    {
        // portus at 60% with no pending order, and arx at 60% too, with the capital moved off both
        // (onto meridia, south's own city — a dangling-but-harmless capital pointer for north's own
        // gate, which simply makes both north towns non-capitals).
        var state = StateWithFort(NonCapital, 60);
        state = RecruitmentTestbed.WithCity(state, state.CityById(Capital)! with { FortificationCode = 60 });
        state = RecruitmentTestbed.WithNation(
            state, state.NationById(Nation)! with { CapitalCityId = "meridia" });

        var driven = IC2.Engine.Tests.Ai.AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.DoesNotContain(driven.IssuedKinds, kind => string.Equals(kind, "recruitment.recruit-standing-unit", StringComparison.Ordinal));
    }
}
