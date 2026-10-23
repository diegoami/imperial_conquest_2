using System.Globalization;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;

namespace IC2.Slice.UI;

/// <summary>One ordinary mobilize command proposed without mutating state or RNG.</summary>
public sealed record AutoMobilizeProposal(int SlotIndex, string CityId, string CommandLine);

/// <summary>T209: one ready slot of the active human nation, after prior queue compaction.</summary>
public static class AutoMobilizePlanner
{
    public static AutoMobilizeProposal? Next(GameState state, Ruleset ruleset, int slotIndex)
    {
        var nation = state.NationById(state.ActiveNationId);
        if (nation?.Control != SeatControl.Human || slotIndex < 0
            || slotIndex >= nation.RecruitmentSlots.Count) return null;
        var slot = nation.RecruitmentSlots[slotIndex];
        if (!MobilizationReadiness.IsReady(slot.StateCode, nation.Control,
                ruleset.Recruitment, ruleset.Flags.SeatAsymmetry)) return null;
        var armyId = RecruitUnitDialogModel.NextArmyId(state);
        return new(slotIndex, slot.TargetCityId,
            $"mobilize {slotIndex.ToString(CultureInfo.InvariantCulture)} {armyId}");
    }
}
