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
        var (promotedUnits, promotions) = BattleCasualties.Promote(reducedUnits, rng, combat);
        winner = winner with { Units = promotedUnits };

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
                var distance = rng.NextInt(
                    combat.ScatteredDefeat.ScatterTilesMin,
                    // Inclusive upper bound: NextInt's own bound is exclusive. Not a gameplay number.
                    combat.ScatteredDefeat.ScatterTilesMax + 1);

                scatter = ScatterPlacement.Find(
                    new GridPoint(loser.X, loser.Y),
                    new GridPoint(winner.X, winner.Y),
                    distance,
                    forFleet: false,
                    loser.Id,
                    state,
                    world);

                if (scatter is { } placed)
                {
                    fate = LoserFate.Scattered;
                    loserCasualties = appliedToLoser;
                    survivor = loser with
                    {
                        Units = loserUnits,
                        X = placed.ToX,
                        Y = placed.ToY,
                        // Zeroed for the rest of the turn it lost on, so nobody gets a free pursuit.
                        Moves = 0,
                    };
                }
            }
        }

        var armies = new List<ArmyState>();
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Id, winner.Id, StringComparison.Ordinal))
            {
                armies.Add(winner);
                continue;
            }

            if (string.Equals(army.Id, loser.Id, StringComparison.Ordinal))
            {
                if (survivor is { } kept)
                {
                    armies.Add(kept);
                }

                // Otherwise the army is deleted outright -- deleteArmy(loser).
                continue;
            }

            // The attacker's spent move must survive even when the attacker is the loser and is deleted;
            // both branches above already carry it, so nothing else needs touching here.
            armies.Add(army);
        }

        var newState = state with
        {
            Armies = ValueList.From(armies),
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
    /// //               a carried army takes casualties, and if d &gt; 70 also loses unitCount × d / 250 units
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
    /// army, then one draw per whole unit that army loses to the <c>d &gt; 70</c> branch, then — under
    /// <see cref="DefeatOutcome.Scatter"/>, and only when ships survive to relocate — the scatter
    /// distance.
    /// </para>
    /// <para>
    /// <strong>The one place the mirrored figure is read as a count, not a ratio.</strong> Under
    /// <see cref="DefeatOutcome.Scatter"/> a beaten fleet loses <c>min(ships, mirroredRatio)</c>
    /// <em>hulls</em>, rather than going through <see cref="BattleCasualties.Apply"/>'s per-unit
    /// expression. A fleet has no unit slots and no troops of its own to divide, and the per-unit
    /// expression would be a no-op on hulls anyway — <c>ships / divisor</c> truncates to zero for any
    /// fleet below the divisor base, so every beaten fleet would come through untouched.
    /// <c>docs/task-catalogue.md</c> T16 DoD 10 records the intended behaviour directly: because the
    /// mirrored figure is never below the numerator, "a fleet survives an <c>improved</c> defeat only
    /// above 40 hulls", which is a statement about hull <em>counts</em>. The loser's carried army is not
    /// the fleet's own strength and is not reduced; it scatters aboard.
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

        var attackerWon = defenderPower < attackerPower;
        var winner = attackerWon ? attacker : defender;
        var loser = attackerWon ? defender : attacker;
        var winnerPower = attackerWon ? attackerPower : defenderPower;
        var loserPower = attackerWon ? defenderPower : attackerPower;

        // FUN_0044B4F8: how close the fight was, squared, drives every loss the winner takes.
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

        // The winner's carried army takes the same casualty figure the field battle applies, and above the
        // damage threshold also loses whole slots at random.
        var carriedCasualtyRatio = BattleCasualties.Ratio(loserPower, winnerPower, combat.WinnerCasualtyNumerator);
        var unitLosses = ValueList<UnitCasualty>.Empty;
        var unitsLost = 0;
        var appliedToCarriedArmy = 0;
        ArmyState? winnerCarriedArmy = null;

        if (winner.CarriedArmyId is { } carriedId && state.ArmyById(carriedId) is { } carried)
        {
            var (reduced, losses, appliedCarried) =
                BattleCasualties.Apply(carried.Units, carriedCasualtyRatio, rng, combat);
            unitLosses = losses;
            appliedToCarriedArmy = appliedCarried;

            if (damage > naval.UnitLossDamageThreshold)
            {
                var toLose = Math.Min(reduced.Count, (reduced.Count * damage) / naval.UnitLossDivisor);
                var remaining = new List<UnitSlot>(reduced);
                for (var i = 0; i < toLose && remaining.Count > 0; i++)
                {
                    var dropped = rng.NextInt(remaining.Count);
                    appliedToCarriedArmy += remaining[dropped].Troops;
                    remaining.RemoveAt(dropped);
                    unitsLost++;
                }

                reduced = ValueList.From(remaining);
            }

            winnerCarriedArmy = carried with { Units = reduced };
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
            // the method remarks for why this one path reads the figure as a count.
            var mirroredRatio = BattleCasualties.Ratio(
                winnerPower, loserPower, combat.ScatteredDefeat.SurvivorCasualtyNumerator);
            var lost = Math.Min(loser.Ships, mirroredRatio);

            if (lost < loser.Ships)
            {
                var distance = rng.NextInt(
                    combat.ScatteredDefeat.ScatterTilesMin,
                    combat.ScatteredDefeat.ScatterTilesMax + 1);

                scatter = ScatterPlacement.Find(
                    new GridPoint(loser.X, loser.Y),
                    new GridPoint(winner.X, winner.Y),
                    distance,
                    forFleet: true,
                    loser.Id,
                    state,
                    world);

                if (scatter is { } placed)
                {
                    fate = LoserFate.Scattered;
                    loserCasualties = lost;
                    survivor = loser with
                    {
                        Ships = loser.Ships - lost,
                        X = placed.ToX,
                        Y = placed.ToY,
                        Moves = 0,
                    };
                }
            }
        }

        var fleets = new List<FleetState>();
        foreach (var fleet in state.Fleets)
        {
            if (string.Equals(fleet.Id, winner.Id, StringComparison.Ordinal))
            {
                fleets.Add(winner);
                continue;
            }

            if (string.Equals(fleet.Id, loser.Id, StringComparison.Ordinal))
            {
                if (survivor is { } kept)
                {
                    fleets.Add(kept);
                }

                continue;
            }

            fleets.Add(fleet);
        }

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
            Fleets = ValueList.From(fleets),
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
    /// city, clear a garrison, move loyalty, cascade defections, change unity, absorb money or supplies,
    /// or emit the capture news lines: all of that is T17. And it never reads
    /// <see cref="RulesetFlags.CombatOnDefeat"/> — a city garrison has nowhere to scatter to, so the flag
    /// is explicitly out of scope for sieges (<c>docs/game-design.md</c> §"The defeated side's fate"),
    /// and T17 depends on that staying true. <see cref="BattleResult.AppliedDefeatOutcome"/> is therefore
    /// <see langword="null"/> and <see cref="BattleResult.LoserFate"/> is
    /// <see cref="LoserFate.Unaffected"/> for every siege.
    /// </para>
    /// <para>
    /// <strong>One kind of draw only.</strong> There is no promotion step, no peace roll and no random
    /// band on this path; the only draws are <see cref="BattleCasualties.Apply"/>'s one-per-slot casualty
    /// divisors for the besieging army's own attrition, which is the very same <c>FUN_0044AE20</c> the
    /// field variant calls.
    /// </para>
    /// <para>
    /// <strong>The garrison term is still omitted</strong> from the defender's strength, exactly as
    /// <see cref="SiegeStrength.Defender"/> leaves it: it needs per-nation recruitment-slot state, and
    /// <c>docs/task-catalogue.md</c> T17 DoD 7 adds it, after both scaling branches.
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

        var owner = state.NationById(city.Owner);
        var isControllerCapital = owner?.CapitalCityId is { } capital
                                  && string.Equals(capital, city.Id, StringComparison.Ordinal);

        var defenderPower = SiegeStrength.Defender(
            city.FortificationCode,
            RequireCityOrder(ruleset, fortifyOrderId),
            city.Loyalty,
            city.PopulationThousands,
            isControllerCapital,
            !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal),
            ruleset);

        // FUN_0044B27C's own further reduction, outside FUN_0044A98C: the besieger is the population's
        // own nation, so the walls are held less willingly against it.
        if (string.Equals(attacker.Nation, city.Allegiance, StringComparison.Ordinal))
        {
            defenderPower -= (defenderPower * siege.AttackerIsAllegianceDefenderReductionPercent) / 100;
        }

        var attackerWon = defenderPower < attackerPower;
        var winnerPower = attackerWon ? attackerPower : defenderPower;
        var loserPower = attackerWon ? defenderPower : attackerPower;

        var casualtyRatio = BattleCasualties.Ratio(
            loserPower, winnerPower, ruleset.Combat.WinnerCasualtyNumerator);
        var (reduced, losses, applied) =
            BattleCasualties.Apply(attacker.Units, casualtyRatio, rng, ruleset.Combat);
        attacker = attacker with { Units = reduced };

        var armies = new List<ArmyState>();
        foreach (var army in state.Armies)
        {
            armies.Add(
                string.Equals(army.Id, attacker.Id, StringComparison.Ordinal) ? attacker : army);
        }

        var newState = state with { Armies = ValueList.From(armies) };

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
            Scatter: null);

        events.Publish(new BattleResolved(result));
        return new BattleResolution(newState, result);
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
