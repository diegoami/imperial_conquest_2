using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Strength;

/// <summary>
/// The original's naval-strength formula — <c>docs/game-design.md</c> milestone 5,
/// <c>docs/build-orchestration-plan.md</c> "T07 Strength functions" Done-when 2 and 3.
/// </summary>
/// <remarks>
/// <para>
/// <c>FUN_0044AA54</c> (<c>0x0044AA54</c>), read directly from the local decompiled dump
/// (<c>%LOCALAPPDATA%\ReTools\all_app_functions.txt:49296-49313</c>):
/// </para>
/// <code>
/// int FUN_0044aa54(fleet)
/// {
///     base = (fleet.ships * fleet.condition) / 10;
///     if (fleet.carriedArmy != none)
///         base = base + FUN_0044a930(fleet.carriedArmy) / 50;   // NOTE: the SIEGE formula, archers ×3
///     draw = random(4);
///     return base + draw * (base / 10);                          // NOT base * (1 + draw / 10)
/// }
/// </code>
/// <para>
/// <strong>Three details worth flagging explicitly, confirmed from the raw decompiled function rather
/// than from the shorter prose paraphrase in <c>docs/game-design.md</c>
/// ("<c>fleetPower = ships × condition / 10 + (carriedArmy ? armyPower/50 : 0)</c> then
/// <c>× (1 + random(4)/10)</c>"):</strong>
/// </para>
/// <list type="number">
/// <item><description>
/// The carried-army term calls <c>FUN_0044A930</c> — <see cref="SiegeStrength.Attacker"/>, the
/// archers-tripled siege-strength formula — not the field-battle <see cref="ArmyPower.Compute"/>. The
/// game-design.md prose just says "armyPower", which is ambiguous between the two; the code is not.
/// </description></item>
/// <item><description>
/// The random bonus is <c>base + draw × (base / 10)</c>, i.e. <c>base / 10</c> is truncated <em>first</em>
/// and then multiplied by the integer draw — not <c>base × (1 + draw / 10)</c> evaluated as one
/// expression. Under integer truncation the two are not interchangeable: reading <c>draw / 10</c> as an
/// integer division on its own would truncate to 0 for every draw in <c>[0, 4)</c> and silently delete
/// the entire bonus. <c>tests/IC2.Engine.Tests/Strength/FleetPowerTests.cs</c> pins the correct order.
/// </description></item>
/// <item><description>
/// <strong>Round-1 correction:</strong> the shipped constant is named <see cref="NavalCombatRules.RandomBandPercent"/>
/// (its provenance: "each band is 10% of the base") but the original code's literal divisor is 10, and
/// the first round of this file computed the bonus as <c>baseValue / rules.RandomBandPercent</c> —
/// arithmetically correct only by coincidence, because the shipped value happens to be 10 and
/// <c>100 / 10 == 10</c>. A ruleset that set this to 20, meaning "20% bands", would silently produce 5%
/// bands instead: <c>base / 20</c> is a fifth of base, not a fifth of the way to doubling it. The
/// expression below computes the percentage the field actually names —
/// <c>baseValue * rules.RandomBandPercent / 100</c> — which reduces to the same
/// <c>floor(baseValue / 10)</c> for the shipped value of 10 (10 and 100 share the factor that makes
/// <c>10x/100</c> and <c>x/10</c> the exact same rational number, so no shipped test result changes) but
/// behaves correctly if the ruleset value is ever changed.
/// </description></item>
/// </list>
/// <para>
/// The random draw goes through <see cref="IRng.NextInt(int)"/> — <c>docs/game-design.md</c> design
/// principle 4 — never <c>System.Random</c>. <c>random(4)</c> in the original (<c>FUN_0040284c</c>,
/// Delphi's <c>Random(N)</c>) draws from <c>[0, N)</c>, exactly <see cref="IRng.NextInt(int)"/>'s
/// documented range.
/// </para>
/// </remarks>
public static class FleetPower
{
    /// <summary>
    /// Computes a fleet's naval combat strength, including the carried-army term (if any) and the
    /// random 0/10/20/30% bonus.
    /// </summary>
    /// <param name="ships">The fleet's ship count.</param>
    /// <param name="conditionPercent">The fleet's condition, 0-100 (<see cref="Model.FleetState.ConditionPercent"/>).</param>
    /// <param name="rng">
    /// The stream to draw the random band from. Callers are expected to pass a stream scoped to this
    /// battle (<see cref="IRng.ForStream"/>), not the root generator, per the engine-wide RNG-stream
    /// convention — this function does not derive a sub-stream itself so that a caller resolving both
    /// sides of one battle controls the draw order.
    /// </param>
    /// <param name="ruleset">Supplies every constant: <see cref="NavalCombatRules"/> under <see cref="CombatRules.Naval"/>.</param>
    /// <param name="carriedArmy">The embarked army's strength inputs, or <see langword="null"/> if the fleet carries no army.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> or <paramref name="ruleset"/> is null.</exception>
    public static int Compute(
        int ships,
        int conditionPercent,
        IRng rng,
        Ruleset ruleset,
        CarriedArmyStrength? carriedArmy = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.Combat.Naval;
        var baseValue = (ships * conditionPercent) / rules.ConditionDivisor;

        if (carriedArmy is { } army)
        {
            var siegeStrength = SiegeStrength.Attacker(army.Units, army.Morale, ruleset, army.ArcherUnitTypeId);
            baseValue += siegeStrength / rules.CarriedArmyPowerDivisor;
        }

        var draw = rng.NextInt(rules.RandomBandCount);

        // baseValue * RandomBandPercent / 100, computed as one integer division so it reduces to
        // exactly floor(baseValue / 10) for the shipped RandomBandPercent of 10 -- see class remarks,
        // point 3, for why the earlier "baseValue / RandomBandPercent" was a latent bug rather than an
        // equivalent rewrite.
        var bandAmount = (baseValue * rules.RandomBandPercent) / 100;
        return baseValue + (draw * bandAmount);
    }

    /// <summary>
    /// The inputs <see cref="Compute"/> needs to fold an embarked army's own strength into the fleet's,
    /// via <see cref="SiegeStrength.Attacker"/> (the original's own choice of formula for this term — see
    /// class remarks, point 1).
    /// </summary>
    /// <remarks>
    /// A sealed reference type, not a <c>struct</c>: <see cref="Compute"/> takes this as
    /// <c>CarriedArmyStrength?</c>, and a value-type record's own <c>default</c> is itself a valid
    /// non-null value (with a null <see cref="Units"/>) that would satisfy a "has a value" pattern match
    /// and only fail, opaquely, once <see cref="SiegeStrength.Attacker"/> tries to enumerate a null
    /// sequence. A reference type's <see langword="null"/> is the only way to mean "no carried army".
    /// </remarks>
    /// <param name="Units">The carried army's unit slots.</param>
    /// <param name="Morale">The carried army's strategic morale.</param>
    /// <param name="ArcherUnitTypeId">See <see cref="SiegeStrength.Attacker"/>.</param>
    public sealed record CarriedArmyStrength(IEnumerable<UnitSlot> Units, int Morale, string ArcherUnitTypeId);
}
