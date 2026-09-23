using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Strength;

namespace IC2.Engine.Battle;

/// <summary>
/// The original's own instant battle resolver, ported — all three variants, producing one
/// <see cref="BattleResult"/>. <c>docs/game-design.md</c> §Combat (milestone M8),
/// <c>docs/design-audit.md</c> Q1, <c>docs/task-catalogue.md</c> T16.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Three decompiled functions, one file.</strong>
/// <see cref="ResolveField"/> is <c>FUN_0044AEE4</c>, <see cref="ResolveSiege"/> is
/// <c>FUN_0044B27C</c>'s strength comparison, and <see cref="ResolveNaval"/> is <c>FUN_0044B5D0</c> with
/// its winner-damage helper <c>FUN_0044B4F8</c> — all from
/// <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> and
/// <c>decompiled-city-capture-resolution.md</c>. Every number comes from
/// <see cref="Ruleset"/>; every random draw goes through <see cref="IRng"/>.
/// </para>
/// <para>
/// <strong>What the original gates this on, and why this port does not.</strong> The original reaches the
/// instant path only when <em>both</em> nations are computer-controlled and sends any human-involved
/// fight to the tactical grid (<c>tests/fixtures/corpus.json</c>
/// <c>battle.instantResolver.onlyForAiVsAi</c>). This reimplementation drops the tactical shell
/// altogether (<c>design-audit.md</c> Q1), so every battle resolves here regardless of who is playing —
/// a deliberate, recorded design decision, not an oversight. Nothing in this file inspects
/// <see cref="SeatControl"/>.
/// </para>
/// <para>
/// <strong>Research held in reserve, and absent from this file on purpose.</strong> The
/// type-effectiveness matrix, the 40%-of-own-troops melee cap, the per-unit tactical morale array, the
/// per-type shooting-vulnerability weight, and the rout mechanic (<c>FUN_00438fb0</c>: removal below
/// <c>standardBattalionSize / 25</c>, the morale checks, the −6/+5 cascade) all belong to a possible
/// future detailed resolver and appear nowhere here. The instant resolver annihilates the loser wholesale
/// and its only attrition is the confirmed per-unit expression in <see cref="BattleCasualties"/>. The
/// Rome/Gaul per-type numbers (99,882 → 63,282) came from the tactical path and are explicitly not a
/// target for this code.
/// </para>
/// <para>
/// <strong>Diplomacy is never called.</strong> The automatic post-battle treaty is published as
/// <see cref="PeaceTreatyTriggered"/>, not invoked, so this task and T19 do not depend on each other's
/// internals.
/// </para>
/// <para>
/// <strong>The random stream is the caller's.</strong> Like <see cref="FleetPower.Compute"/>, these
/// methods draw from the <see cref="IRng"/> they are handed rather than deriving a sub-stream, so a
/// caller resolving one battle controls the whole draw order. The draw order each method uses is
/// documented on the method, because a fixed seed plus a fixed order is what makes every Done-when
/// assertion exact.
/// </para>
/// </remarks>
public static class InstantBattleResolver
{
    /// <summary>
    /// Army versus army on the strategic map — <c>FUN_0044AEE4</c>.
    /// </summary>
    /// <remarks>
    /// <para>The transcribed sequence, in order:</para>
    /// <code>
    /// attacker.moves = 0;
    /// pA = armyPower(attacker);  pB = armyPower(defender);
    /// winner = (pB &lt; pA) ? attacker : defender;              // ties to the defender
    /// applyCasualties(winner, loserPower * 40 / winnerPower);   // a RATIO: see BattleCasualties
    /// winner.money    += loser.money;
    /// winner.supplies  = min(winner.supplies + loser.supplies, winnerTroops / 100);   // attacker wins
    /// winner.supplies  = winner.supplies + loser.supplies;                            // defender wins
    /// for each surviving unit of the winner:
    ///     quality = max(quality, 6);  if (random(4) == 0) quality = min(quality + 1, 9);
    /// deleteArmy(loser);
    /// unity[loser] -= 25;  unity[winner] = min(990, unity[winner] + 25);
    /// news("&lt;winner&gt; destroys army of &lt;loser&gt;.");
    /// if (random(5) &lt; 2 &amp;&amp; unity[loser] &gt; 500 &amp;&amp; cities[loser] &gt; 7) peaceTreaty(winner, loser);
    /// </code>
    /// <para>
    /// <strong>The supply asymmetry is the original's and is copied exactly, not made consistent</strong>
    /// (<c>supply-capacity-rounding.md</c>, code-read): an instant-battle <em>attacker</em> that wins caps
    /// the absorbed supplies at <c>troops / <see cref="EconomyRules.ArmySupplyTonsPerTroops"/></c>
    /// (<c>FUN_0044AEE4</c> lines 49647-49654), while an instant-battle <em>defender</em> that wins takes a
    /// plain sum with no cap at all (lines 49687-49690). The troops the cap is measured against are the
    /// winner's troops <em>after</em> its own casualties, which is the order the function itself runs in.
    /// </para>
    /// <para>
    /// <strong>Draw order</strong>, fixed: one casualty-divisor draw per winner slot in slot order
    /// (<see cref="BattleCasualties.Apply"/>), then one <c>random(4)</c> per surviving winner unit, then
    /// one <c>random(5)</c> for the peace roll, then — only under <see cref="DefeatOutcome.Scatter"/> —
    /// one casualty-divisor draw per <em>loser</em> slot and, when a survivor exists to relocate, one
    /// draw for the scatter distance. The peace roll is drawn <em>before</em> its unity and city-count
    /// gates are tested, exactly as the original's <c>&amp;&amp;</c> short-circuit order does it, and
    /// every draw the <c>improved</c> ruleset adds comes after every draw the original itself makes, so
    /// the two rulesets share an identical prefix and <c>combat.onDefeat</c> cannot shift any confirmed
    /// outcome.
    /// </para>
    /// <para>
    /// <strong>The peace gate reads post-battle unity</strong>, because the original decrements unity
    /// before testing it.
    /// </para>
    /// </remarks>
    /// <param name="state">The state to resolve against.</param>
    /// <param name="attackerArmyId">The attacking army.</param>
    /// <param name="defenderArmyId">The defending army.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="world">The map, needed only to place a scattered survivor.</param>
    /// <param name="rng">The battle's random stream. See the draw order above.</param>
    /// <param name="events">Where the battle publishes. Never null; pass <see cref="NullEventSink.Instance"/> to discard.</param>
    /// <exception cref="ArgumentException">
    /// An id names no army, an army is embarked (a field battle is fought on land), or both armies belong
    /// to the same nation.
    /// </exception>
    public static BattleResolution ResolveField(
        GameState state,
        string attackerArmyId,
        string defenderArmyId,
        Ruleset ruleset,
        World world,
        IRng rng,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);

        var attacker = RequireArmy(state, attackerArmyId, nameof(attackerArmyId));
        var defender = RequireArmy(state, defenderArmyId, nameof(defenderArmyId));

        if (attacker.IsEmbarked || defender.IsEmbarked)
        {
            throw new ArgumentException(
                $"A field battle is fought on the map: '{attacker.Id}' and '{defender.Id}' must both be ashore.",
                nameof(attackerArmyId));
        }

        if (string.Equals(attacker.Nation, defender.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{attacker.Id}' and '{defender.Id}' both belong to '{attacker.Nation}'.", nameof(defenderArmyId));
        }

        var combat = ruleset.Combat;

        // attacker.moves = 0 -- the attack spends the attacker's whole move, whatever the outcome. This
        // is also what stops a victor giving chase in the same turn (docs/game-design.md, combat.onDefeat).
        attacker = attacker with { Moves = 0 };

        var attackerPower = ArmyPower.Compute(attacker.Units, attacker.Morale, ruleset);
        var defenderPower = ArmyPower.Compute(defender.Units, defender.Morale, ruleset);

        // winner = (pB < pA) ? attacker : defender -- a strict less-than, so an exact tie is a defender win.
        var attackerWon = defenderPower < attackerPower;
        var winner = attackerWon ? attacker : defender;
        var loser = attackerWon ? defender : attacker;
        var winnerPower = attackerWon ? attackerPower : defenderPower;
        var loserPower = attackerWon ? defenderPower : attackerPower;

        var casualtyRatio = BattleCasualties.Ratio(loserPower, winnerPower, combat.WinnerCasualtyNumerator);
        var (reducedUnits, unitLosses, appliedCasualties) =
            BattleCasualties.Apply(winner.Units, casualtyRatio, rng, combat);

        // FUN_0044AE20's second pass (bug #289): delete every surviving unit left below its own
        // small-unit threshold, BEFORE the promotion roll -- a deleted unit gets no roll.
        var survivingUnits = BattleCasualties.DeleteBelowThreshold(reducedUnits, ruleset);
        var (promotedUnits, promotions) = BattleCasualties.Promote(survivingUnits, rng, combat);
        winner = winner with { Units = promotedUnits };

        // Delete sweep (build-process.md §4.2 gate 5): the winner's own deletion pass can empty the
        // winner itself (every unit was small enough to be swept). MergeCombatants below writes `winner`
        // back into Armies either way, so an emptied winner is handled by dropping it from the survivor
        // set entirely rather than persisting a zero-unit army record.
        var winnerEmptied = promotedUnits.Count == 0;

        var absorbedMoney = loser.Money;
        var supplySum = winner.SupplyTons + loser.SupplyTons;
        var absorbedSupplyTons = attackerWon
            ? Math.Min(supplySum, winner.TotalTroops / ruleset.Economy.ArmySupplyTonsPerTroops)
            : supplySum;

        winner = winner with
        {
            Money = winner.Money + absorbedMoney,
            SupplyTons = absorbedSupplyTons,
        };

        var (winnerNation, loserNation, winnerUnityDelta, loserUnityDelta) = ApplyUnitySwing(
            state, winner.Nation, loser.Nation, combat.UnitySwing, ruleset);

        // The loser's baggage is gone either way: a scattered army leaves it behind exactly as a
        // destroyed one does (docs/game-design.md, "The defeated side's fate").
        loser = loser with { Money = 0, SupplyTons = 0 };

        var fate = LoserFate.Destroyed;
        var loserCasualties = loser.TotalTroops;
        ScatterOutcome? scatter = null;
        ArmyState? survivor = null;

        // The peace roll is drawn here, before the loser's fate is decided, so that both rulesets consume
        // the same draws for everything the original itself does.
        var peaceRoll = rng.NextInt(combat.AutoPeaceChanceDenominator) < combat.AutoPeaceChanceNumerator;
        var loserCityCount = state.CountCitiesOwnedBy(loser.Nation);
        var peaceFired = peaceRoll
                         && loserNation.Unity > combat.AutoPeaceLoserUnityThreshold
                         && loserCityCount > combat.AutoPeaceLoserCityThreshold;

        if (ruleset.Flags.CombatOnDefeat == DefeatOutcome.Scatter)
        {
            var mirroredRatio = BattleCasualties.Ratio(
                winnerPower, loserPower, combat.ScatteredDefeat.SurvivorCasualtyNumerator);
            var (loserUnits, _, appliedToLoser) =
                BattleCasualties.Apply(loser.Units, mirroredRatio, rng, combat);

            if (appliedToLoser < loser.TotalTroops)
            {
                scatter = ScatterPlacement.Find(
                    new GridPoint(loser.X, loser.Y),
                    new GridPoint(winner.X, winner.Y),
                    DrawScatterDistance(rng, combat.ScatteredDefeat),
                    forFleet: false,
                    loser.Id,
                    state,
                    world,
                    out var destinationTileCode);

                if (scatter is { } placed)
                {
                    fate = LoserFate.Scattered;
                    loserCasualties = appliedToLoser;
                    survivor = loser with
                    {
                        Units = loserUnits,
                        X = placed.ToX,
                        Y = placed.ToY,
                        // A scatter is a move, so the covered cell is recomputed at the destination
                        // exactly as every other mover in the engine recomputes it -- the code Find
                        // already decoded while searching for this destination (T52 DoD 11).
                        CoveredTileCode = destinationTileCode,
                        // Zeroed for the rest of the turn it lost on, so nobody gets a free pursuit.
                        Moves = 0,
                    };
                }
            }
        }

        // The attacker's spent move survives even when the attacker is the loser and is deleted: both
        // combatants below are the already-updated records, so nothing else needs touching.
        var armies = MergeCombatants(state.Armies, a => a.Id, winner, loser.Id, survivor);

        // Delete sweep: a winner the deletion pass emptied is dropped outright rather than persisted as
        // a zero-unit army -- it was never embarked (a field battle requires both sides ashore), so no
        // fleet's CarriedArmyId can be pointing at it.
        if (winnerEmptied)
        {
            armies = ValueList.From(armies.Where(a => !string.Equals(a.Id, winner.Id, StringComparison.Ordinal)));
        }

        var newState = state with
        {
            Armies = armies,
            Nations = ReplaceNations(state, winnerNation, loserNation),
        };

        if (survivor is null)
        {
            newState = ClearCarrierLinks(newState, loser.Id);
        }


        var result = new BattleResult(
            BattleKind.Field,
            attacker.Id,
            defender.Id,
            attacker.Nation,
            defender.Nation,
            attackerPower,
            defenderPower,
            attackerWon ? BattleSide.Attacker : BattleSide.Defender,
            ruleset.Flags.CombatOnDefeat,
            fate,
            appliedCasualties,
            loserCasualties,
            unitLosses,
            promotions,
            absorbedMoney,
            absorbedSupplyTons,
            winnerUnityDelta,
            loserUnityDelta,
            WinnerShipsLost: 0,
            WinnerConditionLost: 0,
            WinnerUnitsLost: 0,
            peaceFired,
            scatter);

        if (fate == LoserFate.Scattered)
        {
            events.Publish(new ArmyScattered(loser.Id, loser.Nation, scatter!.ToX, scatter.ToY));
        }
        else
        {
            events.Publish(new BattleArmyDestroyed(
                NationName(state, winner.Nation), NationName(state, loser.Nation)));
        }

        if (peaceFired)
        {
            events.Publish(new PeaceTreatyTriggered(
                winner.Nation, loser.Nation, loserNation.Unity, loserCityCount));
        }

        events.Publish(new BattleResolved(result));
        return new BattleResolution(newState, result);
    }

    /// <summary>
    /// Fleet versus fleet — <c>FUN_0044B5D0</c>, with its winner-damage helper <c>FUN_0044B4F8</c>.
    /// </summary>
    /// <remarks>
    /// <para>The transcribed sequence:</para>
    /// <code>
    /// attacker.moves = 0;
    /// base(f)     = ships × condition / 10 + (f carries an army ? siegeStrength(army) / 50 : 0);
    /// strength(f) = base(f) + random(4) × (base(f) / 10);
    /// winner = (strength(defender) &lt; strength(attacker)) ? attacker : defender;   // ties to the defender
    /// unity[loser] -= floor(loserShips / 2);  unity[winner] = min(990, unity[winner] + floor(loserShips / 2));
    /// // FUN_0044B4F8: r = max(1, loserStrength × 100 / winnerStrength);  d = r² / 100;
    /// //               winner loses ships × d / 300 ships and condition × d / 300 condition;
    /// //               a carried army takes casualties AT RATIO d (FUN_0044AE20(army, d), #290 part 2),
    /// //               including its own deletion pass (#289), and if d &gt; 70 also loses
    /// //               unitCount × d / 250 + 1 units, swap-with-last (#290 part 3)
    /// deleteFleet(loser);                    // and any army it carried
    /// news("&lt;winner&gt; sinks fleet of &lt;loser&gt;.");
    /// </code>
    /// <para>
    /// <strong>The loser's carried army dies with the fleet</strong> (DoD 7). Both records are removed
    /// together, and the two-way <c>carriedArmyId</c>/<c>aboardFleetId</c> link they shared goes with them,
    /// so the resulting state still passes <see cref="Serialization.GameDataValidation"/> and still saves
    /// and reloads — <c>docs/build-process.md</c> §4.2 gate 5.
    /// </para>
    /// <para>
    /// <strong>Unity moves by ships here, not by the field battle's flat swing</strong>
    /// (<see cref="NavalCombatRules.UnitySwingShipDivisor"/>), and no automatic peace treaty fires: the
    /// peace roll lives in <c>FUN_0044AEE4</c> alone, and the naval function has no equivalent. Promotions
    /// likewise: the naval function has no promotion step.
    /// </para>
    /// <para>
    /// <strong>Draw order</strong>, fixed: the attacker's random band, then the defender's (both inside
    /// <see cref="FleetPower.Compute"/>), then one casualty-divisor draw per slot of the winner's carried
    /// army (<see cref="BattleCasualties.Apply"/>, folded into
    /// <see cref="BattleCasualties.ApplyToCarriedArmy"/> together with the deletion pass below), then one
    /// draw per whole unit that army loses to the <c>d &gt; 70</c> branch — the deletion pass
    /// (<see cref="BattleCasualties.DeleteBelowThreshold"/>, #289) sits between these two and draws
    /// nothing, so it does not itself shift this order, only the SURVIVOR COUNT the whole-unit-loss branch
    /// then counts against — then, under <see cref="DefeatOutcome.Scatter"/>, one casualty-divisor draw for
    /// the beaten fleet's own hull loss (<see cref="BattleCasualties.ApplyToFleet"/>, unconditional, drawn
    /// before the loss is known), and finally, only when hulls survive to relocate, the scatter distance.
    /// </para>
    /// <para>
    /// <strong>The one place the mirrored figure is read as a count, scaled to the fleet's own size
    /// (T52 DoD 1).</strong> Under <see cref="DefeatOutcome.Scatter"/> a beaten fleet loses
    /// <see cref="BattleCasualties.ApplyToFleet"/>'s hull count, rather than going through
    /// <see cref="BattleCasualties.Apply"/>'s per-unit expression. A fleet has no unit slots and no troops
    /// of its own to divide, and <see cref="BattleCasualties.Apply"/>'s own grouping — divide before
    /// multiply — would be a no-op on hull counts, truncating every fleet at or below the divisor's range
    /// to zero regardless of the ratio; fleets cap at 100 hulls, so that is every fleet the game can have.
    /// <see cref="BattleCasualties.ApplyToFleet"/> instead multiplies the hull count into the ratio before
    /// dividing, which is what makes the loss scale with the fleet's own size rather than annihilating any
    /// fleet of 40 hulls or fewer exactly as <c>classical-faithful</c> does (T16's original hull-count
    /// reading, corrected here) — see its own remarks for the full account. The loser's carried army is
    /// not the fleet's own strength and is not reduced; it scatters aboard.
    /// </para>
    /// </remarks>
    /// <param name="state">The state to resolve against.</param>
    /// <param name="attackerFleetId">The attacking fleet.</param>
    /// <param name="defenderFleetId">The defending fleet.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="world">The map, needed only to place a scattered survivor.</param>
    /// <param name="rng">The battle's random stream. See the draw order above.</param>
    /// <param name="archerUnitTypeId">
    /// The unit type this ruleset calls archers, forwarded to <see cref="SiegeStrength.Attacker"/> for a
    /// carried army's contribution. Passed in rather than read from the ruleset because no
    /// <see cref="Ruleset"/> field names the archer type — <see cref="SiegeStrength.Attacker"/> takes it
    /// as a parameter for exactly that reason (T33), and inventing a field for it would mean editing a
    /// record this task does not own.
    /// </param>
    /// <param name="events">Where the battle publishes.</param>
    /// <exception cref="ArgumentException">
    /// An id names no fleet, a fleet is still under construction, or both fleets belong to one nation.
    /// </exception>
    public static BattleResolution ResolveNaval(
        GameState state,
        string attackerFleetId,
        string defenderFleetId,
        Ruleset ruleset,
        World world,
        IRng rng,
        string archerUnitTypeId,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);

        var attacker = RequireFleet(state, attackerFleetId, nameof(attackerFleetId));
        var defender = RequireFleet(state, defenderFleetId, nameof(defenderFleetId));

        if (attacker.IsUnderConstruction || defender.IsUnderConstruction)
        {
            throw new ArgumentException(
                $"A fleet still under construction is not on the map: '{attacker.Id}', '{defender.Id}'.",
                nameof(attackerFleetId));
        }

        if (string.Equals(attacker.Nation, defender.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{attacker.Id}' and '{defender.Id}' both belong to '{attacker.Nation}'.", nameof(defenderFleetId));
        }

        var combat = ruleset.Combat;
        var naval = combat.Naval;

        attacker = attacker with { Moves = 0 };

        var attackerPower = FleetPower.Compute(
            attacker.Ships, attacker.ConditionPercent, rng, ruleset, CarriedArmy(state, attacker, archerUnitTypeId));
        var defenderPower = FleetPower.Compute(
            defender.Ships, defender.ConditionPercent, rng, ruleset, CarriedArmy(state, defender, archerUnitTypeId));

        // Decision 2 (T63): the original divides by zero at a naval battle whose winner would have zero
        // strength -- both fleets floor to zero power (Strength.FleetPower.Compute's own floor for a
        // low-ship, low-condition fleet). AttackLegality.Check refuses this before the command ever
        // reaches here; this is the same defensive backstop ResolveField/ResolveSiege already throw for
        // their own illegal states, since nothing has been drawn from `rng` yet that a caller could
        // observe as a "partial" battle.
        if (Math.Max(attackerPower, defenderPower) <= 0)
        {
            throw new ArgumentException(
                $"Neither '{attacker.Id}' nor '{defender.Id}' has any combat strength; a naval battle "
                + "cannot resolve (T63 Decision 2).",
                nameof(attackerFleetId));
        }

        var attackerWon = defenderPower < attackerPower;
        var winner = attackerWon ? attacker : defender;
        var loser = attackerWon ? defender : attacker;
        var winnerPower = attackerWon ? attackerPower : defenderPower;
        var loserPower = attackerWon ? defenderPower : attackerPower;

        // FUN_0044B4F8: how close the fight was, squared, drives every loss the winner takes.
        //
        // The inner Math.Max(1, winnerPower) is a division guard of the same family as the one
        // BattleCasualties.Ratio carries, so it is worth saying why it is safe HERE and that one was not:
        // this is the forward comparison, and winnerPower is by construction the greater-or-equal of the
        // two powers a line above, so a zero divisor implies a zero numerator and the guard only ever
        // turns 0/0 into a ratio of 1 -- which squares to a damage of 0, i.e. an untouched winner. The
        // mirrored casualty call has no such guarantee, which is exactly why its own guard saturates. The
        // guard above additionally means winnerPower is now strictly positive here, so Math.Max(1, ·) no
        // longer binds in practice -- kept anyway as the documented, once-provably-safe guard it always
        // was, rather than deleted on the strength of a check that lives in a different method.
        var ratio = Math.Max(1, (loserPower * naval.DamageRatioScale) / Math.Max(1, winnerPower));
        var damage = (ratio * ratio) / naval.DamageRatioScale;

        var shipsLost = Math.Min(winner.Ships, (winner.Ships * damage) / naval.WinnerDamageDivisor);
        var conditionLost = Math.Min(
            winner.ConditionPercent, (winner.ConditionPercent * damage) / naval.WinnerDamageDivisor);

        winner = winner with
        {
            Ships = winner.Ships - shipsLost,
            ConditionPercent = winner.ConditionPercent - conditionLost,
        };

        // The winner's carried army takes the SAME damage figure `d` the ships and condition just did --
        // not the field battle's loserPower x 40 / winnerPower ratio (bug #290 part 2's fix: FUN_0044B4F8
        // calls FUN_0044AE20(carriedArmy, d), the function's own third argument, not a fresh ratio). The
        // shared ApplyToCarriedArmy also runs the deletion pass (#289) before counting survivors for the
        // whole-unit loss, and removes those swap-with-last with the missing "+ 1" restored (bug #290
        // part 3).
        var unitLosses = ValueList<UnitCasualty>.Empty;
        var unitsLost = 0;
        var appliedToCarriedArmy = 0;
        ArmyState? winnerCarriedArmy = null;
        string? emptiedCarriedArmyId = null;

        if (winner.CarriedArmyId is { } carriedId && state.ArmyById(carriedId) is { } carried)
        {
            var carriedResult = BattleCasualties.ApplyToCarriedArmy(
                carried.Units, damage, naval.UnitLossDamageThreshold, naval.UnitLossDivisor, rng, ruleset);

            unitLosses = carriedResult.Losses;
            appliedToCarriedArmy = carriedResult.TroopsLost;
            unitsLost = carriedResult.UnitsLost;

            if (carriedResult.Emptied)
            {
                // Delete sweep: the carried army has no units left. Drop its record and clear the
                // fleet's own link to it, rather than persist a zero-unit army or a dangling
                // CarriedArmyId (build-process.md §4.2 gate 5).
                emptiedCarriedArmyId = carried.Id;
                winner = winner with { CarriedArmyId = null };
            }
            else
            {
                winnerCarriedArmy = carried with { Units = carriedResult.Units };
            }
        }

        var unityStep = loser.Ships / naval.UnitySwingShipDivisor;
        var (winnerNation, loserNation, winnerUnityDelta, loserUnityDelta) = ApplyUnitySwing(
            state, winner.Nation, loser.Nation, unityStep, ruleset);

        var fate = LoserFate.Destroyed;
        var loserCasualties = loser.Ships;
        ScatterOutcome? scatter = null;
        FleetState? survivor = null;

        if (ruleset.Flags.CombatOnDefeat == DefeatOutcome.Scatter)
        {
            // Mirrored, and measured in HULLS rather than routed through the per-unit expression -- see
            // the method remarks. Scaled to the fleet's own size (T52 DoD 1): ApplyToFleet multiplies the
            // hull count into the ratio before dividing, which is the fix for the 40-hull cliff the
            // earlier min(ships, mirroredRatio) reading left in place.
            var mirroredRatio = BattleCasualties.Ratio(
                winnerPower, loserPower, combat.ScatteredDefeat.SurvivorCasualtyNumerator);
            var lost = BattleCasualties.ApplyToFleet(loser.Ships, mirroredRatio, rng, combat);

            if (lost < loser.Ships)
            {
                scatter = ScatterPlacement.Find(
                    new GridPoint(loser.X, loser.Y),
                    new GridPoint(winner.X, winner.Y),
                    DrawScatterDistance(rng, combat.ScatteredDefeat),
                    forFleet: true,
                    loser.Id,
                    state,
                    world,
                    out var destinationTileCode);

                if (scatter is { } placed)
                {
                    fate = LoserFate.Scattered;
                    loserCasualties = lost;
                    survivor = loser with
                    {
                        Ships = loser.Ships - lost,
                        X = placed.ToX,
                        Y = placed.ToY,
                        // Load-bearing for a fleet, not cosmetic: FleetTickSystem decides each turn's
                        // storm-tripling branch from this code, so a stale one would carry the
                        // pre-battle tile's weather with the survivor for the rest of the game. The code
                        // Find already decoded while searching for this destination (T52 DoD 11).
                        CoveredTileCode = destinationTileCode,
                        Moves = 0,
                    };
                }
            }
        }

        var fleets = MergeCombatants(state.Fleets, f => f.Id, winner, loser.Id, survivor);

        var armies = new List<ArmyState>();
        foreach (var army in state.Armies)
        {
            // The loser's carried army goes down with the fleet -- both records leave together, so the
            // two-way carrier link cannot dangle.
            if (survivor is null
                && loser.CarriedArmyId is { } sunkArmyId
                && string.Equals(army.Id, sunkArmyId, StringComparison.Ordinal))
            {
                continue;
            }

            if (winnerCarriedArmy is { } damaged && string.Equals(army.Id, damaged.Id, StringComparison.Ordinal))
            {
                armies.Add(damaged);
                continue;
            }

            // The winner's own carried army, emptied by this battle's casualty and deletion passes --
            // dropped outright (see where emptiedCarriedArmyId is set, above).
            if (emptiedCarriedArmyId is { } emptiedId && string.Equals(army.Id, emptiedId, StringComparison.Ordinal))
            {
                continue;
            }

            // A scattered fleet carries its army with it: an embarked army is off the map, but its own
            // X/Y still shadow its carrier's, exactly as MoveFleetCommandHandler keeps them.
            if (survivor is { } moved
                && moved.CarriedArmyId is { } movedArmyId
                && string.Equals(army.Id, movedArmyId, StringComparison.Ordinal))
            {
                armies.Add(army with { X = moved.X, Y = moved.Y });
                continue;
            }

            armies.Add(army);
        }

        var newState = state with
        {
            Fleets = fleets,
            Armies = ValueList.From(armies),
            Nations = ReplaceNations(state, winnerNation, loserNation),
        };

        var result = new BattleResult(
            BattleKind.Naval,
            attacker.Id,
            defender.Id,
            attacker.Nation,
            defender.Nation,
            attackerPower,
            defenderPower,
            attackerWon ? BattleSide.Attacker : BattleSide.Defender,
            ruleset.Flags.CombatOnDefeat,
            fate,
            appliedToCarriedArmy,
            loserCasualties,
            unitLosses,
            ValueList<UnitPromotion>.Empty,
            AbsorbedMoney: 0,
            AbsorbedSupplyTons: 0,
            winnerUnityDelta,
            loserUnityDelta,
            shipsLost,
            conditionLost,
            unitsLost,
            PeaceTreatyFired: false,
            scatter);

        if (fate == LoserFate.Scattered)
        {
            events.Publish(new FleetScattered(loser.Id, loser.Nation, scatter!.ToX, scatter.ToY));
        }
        else
        {
            events.Publish(new BattleFleetSunk(
                NationName(state, winner.Nation), NationName(state, loser.Nation)));
        }

        events.Publish(new BattleResolved(result));
        return new BattleResolution(newState, result);
    }

    /// <summary>
    /// Army versus city — the strength comparison at the head of <c>FUN_0044B27C</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>attackerStrength = FUN_0044A930(army)</c> (archers tripled) against
    /// <c>defenderStrength = FUN_0044A98C(city)</c> (loyalty, fortification and population, with the
    /// capital and non-allegiant branches), then the siege entry point's own further
    /// <c>× <see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/></c> reduction when the
    /// attacking nation <em>is</em> the city's allegiance. Ties go to the defender, as everywhere else.
    /// The attacking army takes attrition on every attempt, win or lose
    /// (<c>tests/fixtures/corpus.json</c> <c>siege.attritionEveryAttempt</c>).
    /// </para>
    /// <para>
    /// <strong>What this deliberately does not do, and why (DoD 12).</strong> It does not transfer the
    /// city's ownership, clear a garrison, cascade defections, change unity, absorb money or supplies, or
    /// emit the capture news lines: all of that is T17 (<see cref="Cities.Capture.CityCaptureResolver"/>).
    /// It never reads <see cref="RulesetFlags.CombatOnDefeat"/> either — a city garrison has nowhere to
    /// scatter to, so the flag is explicitly out of scope for sieges (<c>docs/game-design.md</c>
    /// §"The defeated side's fate"), and T17 depends on that staying true.
    /// <see cref="BattleResult.AppliedDefeatOutcome"/> is therefore <see langword="null"/> and
    /// <see cref="BattleResult.LoserFate"/> is <see cref="LoserFate.Unaffected"/> for every siege.
    /// <strong>T63 correction (bug #293):</strong> an earlier revision of this remark also said the
    /// resolver "does not... move loyalty", which was the very defect #293 filed — the original folds the
    /// city's own loyalty/fortification/population erosion into this same function, on every attempt, win
    /// or lose, and this method now does too (below). Only the OWNERSHIP transfer and everything that
    /// follows from it stays T17's.
    /// </para>
    /// <para>
    /// <strong>One kind of draw only.</strong> There is no promotion step, no peace roll and no random
    /// band on this path; the only draws are <see cref="BattleCasualties.Apply"/>'s one-per-slot casualty
    /// divisors for the besieging army's own attrition, which is the very same <c>FUN_0044AE20</c> the
    /// field variant calls — the erosion below (#293) makes no <c>Random</c> call at all, so it adds no
    /// draws of its own.
    /// <strong>T63 correction (bug #290 part 1):</strong> an earlier revision of this remark said the
    /// siege call site's own <c>ratio</c> argument was undocumented and reused
    /// <see cref="CombatRules.WinnerCasualtyNumerator"/> as a <c>[derived]</c> stand-in from the field call
    /// site. The siege call site is now read at instruction level
    /// (<c>decompiled-defection-and-siege-attrition.md</c> §"FUN_0044b27c, instruction by instruction",
    /// research 3f6ca09) and has its own, different ratio —
    /// <c>clamp(defenderStrength × 6 / attackerStrength, </c><see cref="SiegeRules.AttritionRatioFloor"/><c>,
    /// </c><see cref="SiegeRules.AttritionRatioCeiling"/><c>)</c>, computed from the raw attacker/defender
    /// strengths below, never from a <see cref="BattleResult.WinnerPower"/>/<see cref="BattleResult.LoserPower"/>-style
    /// framing — so this method no longer reads <see cref="CombatRules.WinnerCasualtyNumerator"/> at all.
    /// </para>
    /// <para>
    /// <strong>The garrison term (T17 DoD 7, a narrow grant on this file).</strong>
    /// <see cref="SiegeStrength.Defender"/> stops at the weighted sum and the two scaling branches,
    /// because it is a pure function that does not take per-nation recruitment-slot state as input — see
    /// its own class remarks. But the garrison addend is <c>FUN_0044A98C</c>'s own last line
    /// (<c>docs/investigations/siege-defender-strength.md</c>), so every caller of that function gets it,
    /// this one included: leaving it out here would resolve the siege's own win/loss against an
    /// <em>incomplete</em> defender strength, which is exactly what DoD 7 exists to prevent. This method
    /// therefore sums, over the city owner's <see cref="NationState.RecruitmentSlots"/>, each qualifying
    /// slot's own <c>troops / <see cref="SiegeRules.DefenderGarrisonTroopDivisor"/></c> — divided per slot
    /// <em>before</em> the sum, not the total divided once after (the two differ under truncation whenever
    /// more than one slot targets the city) — and adds it to <see cref="SiegeStrength.Defender"/>'s own
    /// result. <strong>Order matters and is settled by the decompilation, not a choice</strong>: the
    /// addend is the last line <em>inside</em> <c>FUN_0044A98C</c>, strictly after both scaling branches
    /// that function itself applies and strictly before <c>FUN_0044B27C</c>'s own, separate
    /// <c>× 9/10</c> reduction below — two different functions, applied in that order: weighted sum →
    /// × 5/3 → × 4/5 → + garrison → × 9/10.
    /// </para>
    /// </remarks>
    /// <param name="state">The state to resolve against.</param>
    /// <param name="attackerArmyId">The besieging army.</param>
    /// <param name="cityId">The besieged city.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="rng">The battle's random stream: one casualty-divisor draw per besieging slot.</param>
    /// <param name="archerUnitTypeId">See <see cref="ResolveNaval"/>'s own parameter.</param>
    /// <param name="fortifyOrderId">
    /// The ruleset's fortification order, whose <see cref="CityOrderRule.MaxPercent"/> and
    /// <see cref="CityOrderRule.InProgressEncodingRadix"/> decode the city's stored fortification word.
    /// Passed in for the same reason as <paramref name="archerUnitTypeId"/>: <see cref="CityOrderRules"/>
    /// is a plain list keyed by id and names no order as "the" fortification order, so the alternative
    /// would be a C# literal.
    /// </param>
    /// <param name="events">Where the battle publishes.</param>
    /// <exception cref="ArgumentException">
    /// An id names no army, no city or no city order, or the army is embarked.
    /// </exception>
    public static BattleResolution ResolveSiege(
        GameState state,
        string attackerArmyId,
        string cityId,
        Ruleset ruleset,
        IRng rng,
        string archerUnitTypeId,
        string fortifyOrderId,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);

        var attacker = RequireArmy(state, attackerArmyId, nameof(attackerArmyId));
        var city = state.CityById(cityId)
                   ?? throw new ArgumentException($"'{cityId}' is not a known city.", nameof(cityId));

        if (attacker.IsEmbarked)
        {
            throw new ArgumentException(
                $"Army '{attacker.Id}' is aboard a fleet and cannot besiege '{city.Id}'.", nameof(attackerArmyId));
        }

        var siege = ruleset.Siege;
        attacker = attacker with { Moves = 0 };

        var attackerPower = SiegeStrength.Attacker(attacker.Units, attacker.Morale, ruleset, archerUnitTypeId);

        // Decision 2 (T63): the original divides by zero at atk = 0. AttackLegality.Check refuses this
        // before the command ever reaches here; this is the defensive backstop, before any state change
        // below -- the same discipline ResolveField and ResolveNaval already apply for their own illegal
        // states.
        if (attackerPower <= 0)
        {
            throw new ArgumentException(
                $"Army '{attacker.Id}' has no siege strength; a siege attempt cannot resolve "
                + "(T63 Decision 2).",
                nameof(attackerArmyId));
        }

        var fortifyOrder = RequireCityOrder(ruleset, fortifyOrderId);

        // Step 1 (#293): strip a pending fortification order on every attempt. FUN_0044A98C decodes the
        // word through the same FinishedPercent guard either way, so this does not itself change `def`
        // below -- it only changes what the city's own stored word is from here on.
        var strippedFortificationCode = FortificationCode.AfterSiegeAttempt(city.FortificationCode, fortifyOrder);

        var owner = state.NationById(city.Owner);
        var isControllerCapital = owner?.CapitalCityId is { } capital
                                  && string.Equals(capital, city.Id, StringComparison.Ordinal);

        // Step 2: def.
        var defenderPower = SiegeStrength.Defender(
            strippedFortificationCode,
            fortifyOrder,
            city.Loyalty,
            city.PopulationThousands,
            isControllerCapital,
            !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal),
            ruleset);

        // FUN_0044A98C's own last line (T17 DoD 7): += troops / DefenderGarrisonTroopDivisor for each of
        // the owner's recruitment slots targeting this city -- inside the same function, strictly AFTER
        // both scaling branches SiegeStrength.Defender already applied above, and strictly BEFORE
        // FUN_0044B27C's own separate x9/10 reduction below (a different function). Each qualifying
        // slot's own troops is divided before the sum, not the running total divided once after: the two
        // give a different (larger-or-equal) result under truncation whenever more than one slot targets
        // the city, exactly the class of bug caught in IC2.Engine.Cities.Capture.CompleteDefenderStrength
        // (T17), which implements this same addend for its own, separate cascading-defection call sites.
        if (owner is not null)
        {
            var garrisonDivisor = siege.DefenderGarrisonTroopDivisor;
            foreach (var slot in owner.RecruitmentSlots)
            {
                if (string.Equals(slot.TargetCityId, city.Id, StringComparison.Ordinal))
                {
                    defenderPower += slot.Troops / garrisonDivisor;
                }
            }
        }

        // FUN_0044B27C's own further reduction, outside FUN_0044A98C: the besieger is the population's
        // own nation, so the walls are held less willingly against it.
        //
        // Written as a direct x 9/10, the same (x * num) / den shape as the two sibling adjustments
        // inside SiegeStrength.Defender (x 5/3 and x 4/5) and the shape every source states -- NOT as
        // "subtract ten percent". The two differ under truncation: on this task's own Meridia fixture,
        // a pre-reduction 83,666 gives 75,299 the documented way and 75,300 the subtract way.
        if (string.Equals(attacker.Nation, city.Allegiance, StringComparison.Ordinal))
        {
            var remainingPercent = 100 - siege.AttackerIsAllegianceDefenderReductionPercent;
            defenderPower = (defenderPower * remainingPercent) / 100;
        }

        var attackerWon = defenderPower < attackerPower;

        // Decision 1, extended by the user (D-A): the original reads certain intermediate values as a
        // SIGNED 16-bit register -- FUN_00448FD0/FUN_00448FD8's own comparisons are JG/JL (signed),
        // never JA/JB (unsigned) -- decompiled-defection-and-siege-attrition.md
        // §"FUN_0044b27c, instruction by instruction", research 3f6ca09. classical-faithful reproduces
        // this for BOTH the attrition ratio's own clamp (below) and the erosion's own field x def / atk
        // term; improved computes both in ordinary 32-bit arithmetic. A single policy switch, since both
        // are the same original bug class at the same call site's own local variables.
        int ApplySixteenBitPolicy(int value) =>
            ruleset.Flags.BugPolicySiegeRatioClamp == SiegeRatioClampPolicy.Reproduce16BitClamp
                ? unchecked((int)(short)value)
                : value;

        // Step 3 (#293): erosion -- loyalty, then fortification, then population, each
        // field = max(field x 3/4, min(field x 19/20 + 1, field x def / atk)), with the SAME def/atk
        // pair above (after every adjustment, before erosion changes anything). No random term. The
        // ratio term itself (field x def / atk) is where D-A's 16-bit wrap applies, exactly as it does
        // to the attrition ratio below -- both are the same original local, read the same way.
        int Erode(int field) => Math.Max(
            (field * siege.ErosionFloorNumerator) / siege.ErosionFloorDenominator,
            Math.Min(
                ((field * siege.ErosionCeilingNumerator) / siege.ErosionCeilingDenominator)
                    + siege.ErosionCeilingAddend,
                ApplySixteenBitPolicy((field * defenderPower) / attackerPower)));

        var loyaltyBefore = city.Loyalty;
        var fortificationPercentBefore = FortificationCode.FinishedPercent(city.FortificationCode, fortifyOrder);
        var populationBefore = city.PopulationThousands;

        var erodedLoyalty = Erode(loyaltyBefore);
        // The fortification field erosion reads the STRIPPED word's finished percent -- step 1 already
        // discarded any pending order, so this is always a plain percent, never an in-progress encoding.
        var erodedFortificationPercent =
            Erode(FortificationCode.FinishedPercent(strippedFortificationCode, fortifyOrder));
        var erodedPopulation = Erode(populationBefore);

        // Step 4: population floor, after all three erosions.
        var flooredPopulation = Math.Max(
            erodedPopulation,
            (city.MaxPopulationThousands / siege.PopulationFloorDivisor) + siege.PopulationFloorAddend);

        var erodedCity = city with
        {
            Loyalty = erodedLoyalty,
            // Erode() never raises a value above its own ceiling term, and the ceiling of an input
            // already <= fortifyOrder.MaxPercent stays <= MaxPercent under truncation, so writing this
            // straight back as FortificationCode is safe: it reads back as a plain finished percent,
            // never as an in-progress encoding.
            FortificationCode = erodedFortificationPercent,
            PopulationThousands = flooredPopulation,
        };

        // Step 5 (#290): the attacker's own casualties, ratio = clamp(def x 6 / atk, 1, 15), applied on a
        // success or a failure alike -- def and atk here are the RAW attacker/defender strengths, not a
        // "loser/winner" framing (unlike the field and naval call sites). Decision 1 (T63): the original
        // compares this clamp as a SIGNED 16-bit value (see ApplySixteenBitPolicy above), so a
        // near-empty besieger's ratio can wrap to anywhere in [-32768, 32767] -- not merely to a small
        // unsigned remainder -- instead of saturating at the ceiling; classical-faithful reproduces the
        // wrap, improved clamps in ordinary 32-bit arithmetic.
        var rawAttritionRatio = (defenderPower * siege.AttritionRatioMultiplier) / attackerPower;
        var clampInput = ApplySixteenBitPolicy(rawAttritionRatio);
        var casualtyRatio = Math.Max(
            siege.AttritionRatioFloor, Math.Min(siege.AttritionRatioCeiling, clampInput));

        var (reducedUnits, losses, applied) =
            BattleCasualties.Apply(attacker.Units, casualtyRatio, rng, ruleset.Combat);

        // FUN_0044AE20's second pass (#289), same as the field and naval call sites.
        var survivingUnits = BattleCasualties.DeleteBelowThreshold(reducedUnits, ruleset);
        var attackerEmptied = survivingUnits.Count == 0;
        attacker = attacker with { Units = survivingUnits };

        var armies = new List<ArmyState>();
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Id, attacker.Id, StringComparison.Ordinal))
            {
                // Delete sweep: a besieger the deletion pass emptied is dropped outright -- it was never
                // embarked (guarded above), so no fleet's CarriedArmyId can point at it.
                if (!attackerEmptied)
                {
                    armies.Add(attacker);
                }

                continue;
            }

            armies.Add(army);
        }

        var cities = new List<CityState>();
        foreach (var candidate in state.Cities)
        {
            cities.Add(string.Equals(candidate.Id, city.Id, StringComparison.Ordinal) ? erodedCity : candidate);
        }

        var newState = state with { Armies = ValueList.From(armies), Cities = ValueList.From(cities) };

        var result = new BattleResult(
            BattleKind.Siege,
            attacker.Id,
            city.Id,
            attacker.Nation,
            city.Owner,
            attackerPower,
            defenderPower,
            attackerWon ? BattleSide.Attacker : BattleSide.Defender,
            AppliedDefeatOutcome: null,
            LoserFate.Unaffected,
            attackerWon ? applied : 0,
            attackerWon ? 0 : applied,
            losses,
            ValueList<UnitPromotion>.Empty,
            AbsorbedMoney: 0,
            AbsorbedSupplyTons: 0,
            WinnerUnityDelta: 0,
            LoserUnityDelta: 0,
            WinnerShipsLost: 0,
            WinnerConditionLost: 0,
            WinnerUnitsLost: 0,
            PeaceTreatyFired: false,
            Scatter: null,
            CityLoyaltyBefore: loyaltyBefore,
            CityLoyaltyAfter: erodedLoyalty,
            CityFortificationPercentBefore: fortificationPercentBefore,
            CityFortificationPercentAfter: erodedFortificationPercent,
            CityPopulationThousandsBefore: populationBefore,
            CityPopulationThousandsAfter: flooredPopulation);

        events.Publish(new BattleResolved(result));
        return new BattleResolution(newState, result);
    }

    /// <summary>
    /// The <c>improved</c> ruleset's scatter distance, drawn once per scattering survivor.
    /// </summary>
    /// <remarks>
    /// The <c>+ 1</c> makes <see cref="ScatteredDefeatRules.ScatterTilesMax"/> inclusive, because
    /// <see cref="IRng.NextInt(int, int)"/>'s own upper bound is exclusive. It is a range adapter, not a
    /// gameplay number — the two tunables are the min and the max, both ruleset data.
    /// </remarks>
    private static int DrawScatterDistance(IRng rng, ScatteredDefeatRules rules) =>
        rng.NextInt(rules.ScatterTilesMin, rules.ScatterTilesMax + 1);

    /// <summary>
    /// Rebuilds a combatant list around one battle: the winner replaced by its updated record, the loser
    /// replaced by <paramref name="survivor"/> or dropped outright when there is none, everyone else left
    /// alone and in place.
    /// </summary>
    /// <remarks>
    /// Shared by the field variant's armies and the naval variant's fleets, which had the same fifteen
    /// lines written out twice. The naval variant's <em>army</em> list is deliberately not routed through
    /// here: it is not the same shape, because it also has to sink a destroyed carrier's cargo, damage a
    /// surviving carrier's cargo, and move a scattered carrier's cargo with it.
    /// </remarks>
    private static ValueList<T> MergeCombatants<T>(
        ValueList<T> all,
        Func<T, string> idOf,
        T winner,
        string loserId,
        T? survivor)
        where T : class
    {
        var merged = new List<T>(all.Count);
        foreach (var item in all)
        {
            var id = idOf(item);

            if (string.Equals(id, idOf(winner), StringComparison.Ordinal))
            {
                merged.Add(winner);
                continue;
            }

            if (string.Equals(id, loserId, StringComparison.Ordinal))
            {
                if (survivor is { } kept)
                {
                    merged.Add(kept);
                }

                // Otherwise the loser is deleted outright -- deleteArmy / deleteFleet.
                continue;
            }

            merged.Add(item);
        }

        return ValueList.From(merged);
    }

    private static ArmyState RequireArmy(GameState state, string armyId, string parameterName) =>
        state.ArmyById(armyId)
        ?? throw new ArgumentException($"'{armyId}' is not a known army.", parameterName);

    private static FleetState RequireFleet(GameState state, string fleetId, string parameterName) =>
        state.FleetById(fleetId)
        ?? throw new ArgumentException($"'{fleetId}' is not a known fleet.", parameterName);

    private static CityOrderRule RequireCityOrder(Ruleset ruleset, string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, orderId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        throw new ArgumentException(
            $"Ruleset '{ruleset.Id}' declares no '{orderId}' city order, which the siege defender's "
            + "fortification term decodes through.",
            nameof(orderId));
    }

    private static FleetPower.CarriedArmyStrength? CarriedArmy(
        GameState state, FleetState fleet, string archerUnitTypeId)
    {
        if (fleet.CarriedArmyId is not { } armyId || state.ArmyById(armyId) is not { } army)
        {
            return null;
        }

        return new FleetPower.CarriedArmyStrength(army.Units, army.Morale, archerUnitTypeId);
    }

    /// <summary>
    /// The unity swing both the field and the naval variant apply: the loser loses <paramref name="step"/>,
    /// the winner gains it up to <see cref="EconomyRules.UnityCap"/>. The loser's side is deliberately not
    /// floored — the original's <c>unity[loserNation] -= 25</c> has no floor either, and inventing one here
    /// would silently diverge from a value another system may be relying on.
    /// </summary>
    private static (NationState Winner, NationState Loser, int WinnerDelta, int LoserDelta) ApplyUnitySwing(
        GameState state,
        string winnerNationId,
        string loserNationId,
        int step,
        Ruleset ruleset)
    {
        var winner = state.NationById(winnerNationId)
                     ?? throw new ArgumentException($"'{winnerNationId}' is not a known nation.", nameof(state));
        var loser = state.NationById(loserNationId)
                    ?? throw new ArgumentException($"'{loserNationId}' is not a known nation.", nameof(state));

        var winnerUnity = Math.Min(ruleset.Economy.UnityCap, winner.Unity + step);
        var loserUnity = loser.Unity - step;

        return (
            winner with { Unity = winnerUnity },
            loser with { Unity = loserUnity },
            winnerUnity - winner.Unity,
            loserUnity - loser.Unity);
    }

    private static ValueList<NationState> ReplaceNations(GameState state, NationState winner, NationState loser)
    {
        var nations = new List<NationState>();
        foreach (var nation in state.Nations)
        {
            if (string.Equals(nation.Id, winner.Id, StringComparison.Ordinal))
            {
                nations.Add(winner);
            }
            else if (string.Equals(nation.Id, loser.Id, StringComparison.Ordinal))
            {
                nations.Add(loser);
            }
            else
            {
                nations.Add(nation);
            }
        }

        return ValueList.From(nations);
    }

    /// <summary>
    /// Clears any fleet's claim on an army that has just been deleted. A field battle is fought ashore, so
    /// this can never fire on that path today — it is here because the class of bug it prevents (a deleted
    /// entity still referenced from somewhere else, producing a save that will not reload) is the one
    /// <c>docs/build-process.md</c> §4.2 gate 5 exists for, and the guard costs one pass over the fleets.
    /// </summary>
    private static GameState ClearCarrierLinks(GameState state, string deletedArmyId)
    {
        var fleets = new List<FleetState>();
        var changed = false;
        foreach (var fleet in state.Fleets)
        {
            if (fleet.CarriedArmyId is { } carried && string.Equals(carried, deletedArmyId, StringComparison.Ordinal))
            {
                fleets.Add(fleet with { CarriedArmyId = null });
                changed = true;
                continue;
            }

            fleets.Add(fleet);
        }

        return changed ? state with { Fleets = ValueList.From(fleets) } : state;
    }

    private static string NationName(GameState state, string nationId) =>
        state.NationById(nationId)?.Name ?? nationId;
}
