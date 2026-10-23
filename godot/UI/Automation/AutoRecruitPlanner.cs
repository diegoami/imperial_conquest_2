using System.Globalization;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;

namespace IC2.Slice.UI;

/// <summary>One city's read-only auto-recruit decision and its ordinary command or skip reason.</summary>
public sealed record AutoRecruitDecision(string CityId, string? CommandLine, string? Reason, int CostTalents);

/// <summary>T210: globally configured city plans evaluated against one campaign's live state.</summary>
public static class AutoRecruitPlanner
{
    public static AutoRecruitDecision ForCity(GameState state, World world, Ruleset ruleset,
        UserSettings settings, AutoRecruitCityPlan plan)
    {
        AutoRecruitDecision Skip(string reason) => new(plan.CityId, null, reason, 0);
        if (!settings.AutoRecruitEnabled) return Skip("auto recruit is off");
        if (!string.Equals(plan.WorldId, world.Id, StringComparison.Ordinal)
            || !world.Cities.Any(city => city.Id == plan.CityId)) return Skip("plan belongs to another world or unknown city");
        var nation = state.NationById(state.ActiveNationId);
        if (nation?.Control != SeatControl.Human) return Skip("active seat is not human");
        var cityState = state.CityById(plan.CityId);
        if (cityState is null || cityState.Owner != nation.Id) return Skip("city is not owned by the active nation");
        var queued = nation.RecruitmentSlots.Count(slot => slot.TargetCityId == plan.CityId);
        if (queued >= plan.TargetQueued) return Skip("city queue target reached");
        if (ruleset.UnitTypeById(plan.UnitTypeId) is null) return Skip("unit type is unavailable in this ruleset");
        var bounds = RecruitTroopBounds.For(ruleset, plan.UnitTypeId);
        if (plan.Troops < bounds.Minimum || plan.Troops > bounds.Maximum
            || (plan.Troops - bounds.Minimum) % bounds.Step != 0) return Skip("troop count is outside this type's step and bounds");
        if (nation.RecruitmentSlots.Count >= ruleset.Recruitment.MaxSlots) return Skip("nation training queue is full");
        if (nation.MobilizedPercent >= ruleset.Recruitment.MobilizationCapPercent) return Skip("mobilization rate is at its cap");
        if (!RecruitmentEligibility.MayTakeOrder(cityState, nation, ruleset))
            return Skip(RecruitmentEligibility.RefusalReason(cityState, nation, ruleset));
        var cost = StandingRecruitmentCost.InitialCost(plan.Troops, plan.UnitTypeId, ruleset);
        if (cost > Math.Max(0, nation.Treasury - settings.AutoTreasuryReserveTalents))
            return Skip("treasury reserve prevents this order");
        return new(plan.CityId,
            $"recruit-standing {plan.CityId} {plan.UnitTypeId} {plan.Troops.ToString(CultureInfo.InvariantCulture)}",
            null, cost);
    }
}
