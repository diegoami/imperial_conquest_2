using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 1's two halves, pinned separately: the ambition half scales with
/// <c>expansionDrive</c>, and the necessity half does not.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Review round 1, F1.</strong> These exist because the reviewer found a comment stating the
/// opposite of the code: <c>ProposeRecruitment</c> claimed "a nation with none still recruits when
/// threatened", while the threat bonus sat <em>inside</em> the <c>expansionDrive</c> multiplication, so
/// an <c>expansionDrive = 0</c> nation produced no recruitment candidate at all — threatened or not. The
/// code was the defect; see that method for the two reasons. Nothing in this project's tests used
/// <c>ExpansionDrive: 0</c> or <c>1.0</c> before this file, which is exactly why the contradiction
/// survived: <strong>the claim was both false and unpinned</strong>. Both ends of the range are now
/// pinned, and the threatened/unthreatened split at each end with them.
/// </para>
/// <para>
/// The sibling path was checked for the same shape and does not have it:
/// <c>ProposeFortification</c>'s multiplier runs from ×2 at <c>expansionDrive = 0</c> down to ×1 at
/// <c>1.0</c> and is never zero, so fortification cannot vanish the way recruitment could.
/// <see cref="Fortification_never_vanishes_at_either_end_of_expansion_drive"/> pins that too, so the
/// absence is asserted rather than asserted-about.
/// </para>
/// </remarks>
public sealed class AiEconomyPhaseTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>Well above the cheapest battalion's cost even at the 10% floor, so affordability never decides these tests.</summary>
    private const int Treasury = 5000;

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void A_threatened_city_draws_a_recruitment_order_at_every_expansion_drive(double expansionDrive)
    {
        var candidates = Economy(expansionDrive, threatened: true);

        Assert.Contains(candidates, c => c.Kind == "recruit");
    }

    /// <summary>
    /// The other half of the same sentence: without a threat, recruiting is ambition, and a nation with
    /// none does not do it. This is the assertion that stops F1's fix being "multiply by nothing".
    /// </summary>
    [Fact]
    public void An_unthreatened_city_draws_no_recruitment_order_from_a_nation_with_no_expansion_drive()
    {
        var candidates = Economy(0.0, threatened: false);

        Assert.DoesNotContain(candidates, c => c.Kind == "recruit");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void An_unthreatened_city_still_draws_one_from_a_nation_that_has_some(double expansionDrive)
    {
        var candidates = Economy(expansionDrive, threatened: false);

        Assert.Contains(candidates, c => c.Kind == "recruit");
    }

    /// <summary>
    /// The threat is worth exactly <see cref="AiWeights.ThreatenedCityBonus"/> more, at every
    /// <c>expansionDrive</c> — which is what "added after the multiplication" means, stated as an
    /// arithmetic identity rather than as a shape.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void The_threat_is_worth_a_flat_bonus_independent_of_expansion_drive(double expansionDrive)
    {
        var threatened = ScoreOfRecruit(Economy(expansionDrive, threatened: true));
        var calm = ScoreOfRecruit(Economy(expansionDrive, threatened: false));

        Assert.Equal(AiWeights.ThreatenedCityBonus, threatened - calm);
    }

    /// <summary>
    /// The identity above cannot be written at <c>expansionDrive = 0</c>, because the unthreatened score
    /// there is zero and a zero-scored candidate is deliberately never produced -- so the same claim is
    /// made directly instead: with no ambition contributing anything, a threatened recruitment scores
    /// exactly the threat bonus and nothing else. This is F1's fix at its sharpest point.
    /// </summary>
    [Fact]
    public void At_no_expansion_drive_a_threatened_recruitment_scores_exactly_the_threat_bonus()
    {
        Assert.Equal(AiWeights.ThreatenedCityBonus, ScoreOfRecruit(Economy(0.0, threatened: true)));
    }

    /// <summary>
    /// The ambition half really does scale: a fully expansionist nation values an unthreatened
    /// recruitment at the full base score, and a half-hearted one at half of it.
    /// </summary>
    [Theory]
    [InlineData(1.0, AiWeights.RecruitBaseScore)]
    [InlineData(0.5, AiWeights.RecruitBaseScore / 2)]
    public void The_ambition_half_scales_with_expansion_drive(double expansionDrive, long expected)
    {
        Assert.Equal(expected, ScoreOfRecruit(Economy(expansionDrive, threatened: false)));
    }

    /// <summary>
    /// The sibling check: fortification's multiplier is never zero, so unlike recruitment it cannot be
    /// switched off by a personality value. Asserted at both ends and with the threat either way.
    /// </summary>
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(1.0, false)]
    public void Fortification_never_vanishes_at_either_end_of_expansion_drive(double expansionDrive, bool threatened)
    {
        var candidates = Economy(expansionDrive, threatened);

        Assert.Contains(candidates, c => c.Kind == "fortify");
    }

    /// <summary>
    /// The budget floor and the score must not contradict each other, which is the internal argument that
    /// settled F1: a nation granted a positive budget at <c>expansionDrive = 0</c> has to be able to spend
    /// it on something. If a later change makes the floor zero, this fails and says so.
    /// </summary>
    [Fact]
    public void A_nation_with_no_expansion_drive_is_granted_a_budget_it_can_actually_spend()
    {
        var budget = AiEconomyPhase.TurnBudget(Treasury, expansionDrivePermille: 0);

        Assert.True(budget > 0, $"TreasuryCommitFloorPermille grants nothing at expansionDrive 0: {budget}");
        Assert.NotEmpty(Economy(0.0, threatened: true));
    }

    private static long ScoreOfRecruit(List<AiCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Kind == "recruit")
            {
                return candidate.Score;
            }
        }

        Assert.Fail("no recruitment candidate was produced");
        return 0;
    }

    /// <summary>Runs the economy phase alone over a one-city state, with or without a hostile army beside it.</summary>
    private static List<AiCandidate> Economy(double expansionDrive, bool threatened)
    {
        var state = EconomyState(expansionDrive, threatened);
        var view = new AiView(state, AiScriptedStates.Ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();

        AiEconomyPhase.Propose(view, AiPersonalityProfile.For(state.NationById(Acting)!), candidates);
        return candidates;
    }

    /// <summary>
    /// One city the acting nation owns, one the other nation owns, and the other nation's army either
    /// standing next to the first (threatened) or in the far corner (not).
    /// </summary>
    /// <remarks>
    /// "Threatened" is not this fixture's own notion: it is
    /// <see cref="Economy.HostileArmyAdjacent.IsThreatened"/>, the original's confirmed nine-cell test,
    /// which needs the two nations to be <em>at war</em> as well as adjacent — so the fixture sets the war
    /// relation rather than only moving an army.
    /// </remarks>
    private static GameState EconomyState(double expansionDrive, bool threatened)
    {
        var personality = new AiPersonality(
            Aggression: 0.5, ExpansionDrive: expansionDrive, LoyaltyToAlliances: 0.5);

        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, personality, treasury: Treasury, capitalCityId: "home"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, treasury: Treasury),
        };

        var cities = new[]
        {
            // Fortification below the maximum and no order pending, so the fortify path is available too.
            CaptureFixtures.City(
                "home", "Home", 2, 2, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 7, 5, Other, Other, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        var raider = CaptureFixtures.Army(
            "raider", Other, threatened ? 3 : 7, threatened ? 2 : 0, morale: 60,
            CaptureFixtures.Unit("light_infantry", 15000));

        var state = BattleCommandTestbed.StateWith(nations, cities, new[] { raider });
        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(state, Acting, Other), Acting);
    }
}
