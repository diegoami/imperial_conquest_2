using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The honourable-peace branch (DoD 8) — <c>FUN_00450C68</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>:
/// <c>score(n) = (nation[n][+0x430] / 100) * nation[n][+0x440]</c> (population × unity — the source's
/// own inline comment on this line, despite <c>+0x430</c> being named "wealth" three lines earlier in
/// the same report; <c>nation-tax-base-and-city-economy-fields.md</c> settles the field itself as
/// <see cref="NationState.Wealth"/>, <c>Σ population × 3000</c>) and
/// <c>armies(n) = Σ FUN_0044A8CC(army)</c> over <c>n</c>'s armies (total field strength). The honourable
/// branch fires — no reparations paid — when the victor scores lower on either measure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The score comparison needs no ruleset divisor.</strong> The confirmed formula divides
/// <see cref="NationState.Wealth"/> by 100 before multiplying by unity, but
/// <see cref="EconomyRules.WealthPerPopulationThousand"/> — the constant <see cref="NationState.Wealth"/>
/// is always built from (<c>city.PopulationThousands × WealthPerPopulationThousand</c>, summed) — is
/// confirmed as <c>3,000</c>, an exact multiple of 100 in every shipped ruleset a task in this build has
/// produced. <c>Wealth</c> is therefore always an exact multiple of 100, so
/// <c>(Wealth / 100) × Unity</c> orders identically to <c>Wealth × Unity</c> for any two nations being
/// compared (dividing both sides of a strict inequality by the same positive constant, with no
/// remainder on either side, never changes which side is smaller). This lets the comparison avoid a
/// literal <c>100</c> — which is not itself a named field anywhere in <see cref="DiplomacyRules"/>
/// (searched: the whole of <c>Ruleset.cs</c>, which has no such divisor, and this task's Owns list grants
/// only one additive <see cref="RulesetFlags"/> field, not a new one here) — without weakening the
/// formula: the two expressions are exactly equivalent given that one confirmed invariant, not an
/// approximation. <c>long</c> arithmetic avoids the overflow a 334-city nation's <c>Wealth × Unity</c>
/// product could reach in <c>int</c>.
/// </para>
/// </remarks>
public static class HonourablePeaceGate
{
    /// <summary>
    /// Whether the honourable-peace branch fires for this treaty: the victor scores lower than the loser
    /// on population × unity, or on total army field strength.
    /// </summary>
    /// <param name="state">
    /// The state at the moment of the treaty — armies already reflect the battle's own casualties and the
    /// loser's own army already reflects <c>deleteArmy</c>/scatter, exactly as the original's own treaty
    /// routine reads them (it runs after both).
    /// </param>
    /// <param name="ruleset">Supplies every combat constant <see cref="ArmyPower.Compute"/> uses.</param>
    /// <param name="winnerNationId">The battle's winner.</param>
    /// <param name="loserNationId">The battle's loser.</param>
    public static bool Fires(GameState state, Ruleset ruleset, string winnerNationId, string loserNationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var winner = state.NationById(winnerNationId)
                     ?? throw new ArgumentException($"'{winnerNationId}' is not a known nation.", nameof(winnerNationId));
        var loser = state.NationById(loserNationId)
                    ?? throw new ArgumentException($"'{loserNationId}' is not a known nation.", nameof(loserNationId));

        var winnerScore = (long)winner.Wealth * winner.Unity;
        var loserScore = (long)loser.Wealth * loser.Unity;

        var winnerArmyPower = TotalArmyPower(state, ruleset, winnerNationId);
        var loserArmyPower = TotalArmyPower(state, ruleset, loserNationId);

        return winnerScore < loserScore || winnerArmyPower < loserArmyPower;
    }

    /// <summary>The summed field-battle strength of every army a nation currently owns.</summary>
    private static long TotalArmyPower(GameState state, Ruleset ruleset, string nationId)
    {
        long total = 0;
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                total += ArmyPower.Compute(army.Units, army.Morale, ruleset);
            }
        }

        return total;
    }
}
