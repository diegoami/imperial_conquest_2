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
/// <strong>The <c>/100</c> is transcribed literally, not dropped.</strong> An earlier revision of this
/// file argued that <c>(Wealth / 100) × Unity</c> orders identically to <c>Wealth × Unity</c>, on the
/// premise that <see cref="NationState.Wealth"/> is always an exact multiple of 100 (built from
/// <c>city.PopulationThousands × WealthPerPopulationThousand</c>, and <c>WealthPerPopulationThousand</c>
/// is confirmed <c>3,000</c> in the shipped toy ruleset). Review caught that premise failing twice over:
/// <see cref="GameStateFactory.CreateInitial"/> seeds <see cref="NationState.Wealth"/> straight from
/// scenario data (<c>data/worlds/toy-3city.json</c>'s own <c>"wealth": 360</c>, not a multiple of 100
/// derived from any population figure), and <c>WealthPerPopulationThousand</c> is itself moddable
/// ruleset data with no guarantee of being a multiple of 100 in a ruleset this engine hasn't shipped
/// yet. On the shipped toy world's own figures (winner 400/80, loser 360/100) the two forms disagree
/// outright: <c>(400/100)×80 = 320</c> vs <c>(360/100)×100 = 300</c> says the honourable branch does
/// <em>not</em> fire, while <c>400×80 = 32,000</c> vs <c>360×100 = 36,000</c> says it does —
/// <see cref="HonourablePeaceGateDivergenceTests"/> pins this exact disagreement so the dropped divisor
/// can never be silently reintroduced. The <c>100</c> below is transcribed directly from the confirmed
/// formula (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md:63</c>), not a new gameplay
/// constant, so — per review — it needs no <see cref="Ruleset"/> field and no Owns grant: it is the same
/// kind of literal as the formula's own <c>/4</c> and <c>×10</c> would be if this task were transcribing
/// them as bare numbers rather than through <see cref="DiplomacyRules"/>, except that no ruleset field
/// for it exists anywhere to transcribe through instead. <c>long</c> arithmetic avoids the overflow a
/// 334-city nation's <c>Wealth × Unity</c> product could reach in <c>int</c>, and the division happens
/// <em>before</em> the multiplication, exactly as the source's own parenthesisation reads.
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

        // score(n) = (Wealth / 100) * Unity -- the division happens first, exactly as the source's own
        // "(nation[n][+0x430] / 100) * nation[n][+0x440]" reads. Do not simplify this to Wealth * Unity:
        // see this type's remarks for the shipped-data divergence that proves the two are not equivalent.
        var winnerScore = (long)(winner.Wealth / 100) * winner.Unity;
        var loserScore = (long)(loser.Wealth / 100) * loser.Unity;

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
