using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly tick's nation loop, wired against real <see cref="GameState"/> —
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 6 and 10.
/// </summary>
/// <remarks>
/// <para>
/// Ordered after <see cref="QuarterlyCityEconomySystem"/> (registered at 100; this one at 200), so it
/// reads the tax base and wealth that system's <see cref="NationTaxBaseRebuild"/> call just rebuilt from
/// this quarter's grown population — never the value stored from before the tick
/// <strong>[confirmed: city-population-growth.md's step order]</strong>.
/// </para>
/// <para>
/// <c>city-population-growth.md</c>: "for every nation with unity &gt; 0" gates the <em>entire</em>
/// nation-loop body — mobilization decay, the treasury credit, and the unity update alike — not only the
/// unity line; a nation already at unity 0 is left completely untouched this quarter.
/// </para>
/// <para>
/// Mobilization is decayed <em>before</em> the treasury credit is computed, matching the source order,
/// even though the credit itself does not read mobilization at all — only the unity update does, and it
/// reads the now-decayed value. The AI debt/deposition check that follows unity in the original is not
/// this task's: T39's (docs/task-catalogue.md, "T35 → T39 → T13, T22").
/// </para>
/// </remarks>
[QuarterBoundaryHandler("economy.quarterly-nation-tick", Order = 200)]
public sealed class QuarterlyNationEconomySystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var beforeThisHandler = context.State;
        var ruleset = context.Ruleset;
        var economy = ruleset.Economy;

        var updatedNations = beforeThisHandler.Nations.Select(nation =>
        {
            if (nation.Unity <= 0)
            {
                return nation;
            }

            var decayedMobilization = NationUnityUpdate.DecayMobilization(nation.MobilizedPercent, economy);
            var withDecayedMobilization = nation with { MobilizedPercent = decayedMobilization };

            var credit = NationTreasuryCredit.Compute(withDecayedMobilization, beforeThisHandler, ruleset);
            var newUnity = NationUnityUpdate.Compute(nation.Unity, nation.TaxRatePercent, decayedMobilization, economy);

            return withDecayedMobilization with
            {
                Treasury = withDecayedMobilization.Treasury + credit,
                Unity = newUnity,
            };
        });

        return beforeThisHandler with { Nations = ValueList.From(updatedNations) };
    }
}
