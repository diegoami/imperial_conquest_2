using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// Advances every nation's active standing-recruitment slots by one week's <c>StateCode</c> step —
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Scope: "advancing each slot's state
/// code through T06's <see cref="CityUnitStateCode"/>".
/// </summary>
/// <remarks>
/// Runs in <see cref="TurnPhase.CityTick"/>, exactly where that phase's own declared remarks name this
/// task as a consumer ("Round scope. Every city: weekly supply production and famine unrest, and the
/// city-unit <c>StateCode</c> step. Consumers: T37 economy, T13 recruitment, T18 city orders."). Every
/// slot on every nation is advanced, every round, regardless of whose turn ended it — the original's
/// global weekly tick loops all state, not only the ending nation's, the same convention
/// <see cref="Economy.WeeklyCitySupplySystem"/> already documents for its own city loop.
/// <para>
/// What happens once a slot's <c>StateCode</c> reaches <see cref="CalendarRules.CityUnitStateCodeCap"/> —
/// the original's unnamed mobilization helper, <c>FUN_0044a4e0</c> — is not decompiled and is therefore
/// not implemented here; see <see cref="Commands.RecruitStandingUnitCommand"/>'s remarks. This system only
/// steps the counter and holds it at the cap, exactly as <see cref="CityUnitStateCode.Advance"/> itself
/// documents.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.CityTick, "recruitment.slot-readiness")]
public sealed class RecruitmentSlotReadinessSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var calendar = context.Ruleset.Calendar;

        var updatedNations = new List<NationState>(state.Nations.Count);
        foreach (var nation in state.Nations)
        {
            if (nation.RecruitmentSlots.Count == 0)
            {
                updatedNations.Add(nation);
                continue;
            }

            var updatedSlots = new List<RecruitmentSlot>(nation.RecruitmentSlots.Count);
            foreach (var slot in nation.RecruitmentSlots)
            {
                updatedSlots.Add(slot with { StateCode = CityUnitStateCode.Advance(slot.StateCode, calendar) });
            }

            updatedNations.Add(nation with { RecruitmentSlots = ValueList.From(updatedSlots) });
        }

        return state with { Nations = ValueList.From(updatedNations) };
    }
}
