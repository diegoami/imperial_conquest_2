using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Strength;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 2: "<em>evaluate each border city's threat level (visible enemy
/// strength within N tiles) against its own garrison; reinforce, hold, or — if <c>aggression</c> and a
/// favorable strength ratio both clear a threshold — attack.</em>"
/// </summary>
/// <remarks>
/// <para>
/// <strong>The strength ratio is computed with the engine's own strength functions, not with a second
/// model of combat.</strong> A field battle's winner is <c>defenderPower &lt; attackerPower</c> with both
/// sides measured by <see cref="ArmyPower.Compute"/>, and a siege's winner is
/// <c>defenderPower &lt; attackerPower</c> with the attacker measured by
/// <see cref="SiegeStrength.Attacker"/> and the defender by
/// <see cref="Cities.Capture.CompleteDefenderStrength.Compute"/> — both are pure functions of the state,
/// with no random draw anywhere in them (<see cref="Battle.InstantBattleResolver"/>'s only draws are the
/// casualty and promotion rolls, which happen after the winner is known). So for those two the AI's ratio
/// is not an estimate at all: at a ratio over 1000 it knows it wins, and under 1000 it knows it loses.
/// The one thing it deliberately leaves out of its siege estimate is
/// <c>SiegeRules.AttackerIsAllegianceDefenderReductionPercent</c>, the further ×9/10 the resolver applies
/// when the besieger is the city's own allegiance: including it would mean reproducing the resolver's
/// composition order here, and leaving it out only ever makes the AI's own estimate
/// <em>conservative</em> — it can be pleasantly surprised, never unpleasantly.
/// </para>
/// <para>
/// <strong>A naval attack is the exception, and is scored accordingly.</strong>
/// <see cref="FleetPower.Compute"/> draws a random 0/10/20/30% band, so no caller can know a naval
/// battle's outcome in advance. The AI samples it instead: it asks for one draw from a
/// <em>named, derived</em> stream (<see cref="IRng.ForStream"/>, which does not advance the caller's own
/// generator) whose name is fixed by the two fleet ids, so the same state always produces the same
/// estimate and the estimate never perturbs the battle's own rolls. That is a sample of a distribution,
/// not a prediction, which is why <see cref="AiWeights.AttackFleetBaseScore"/> sits below both land
/// attacks.
/// </para>
/// <para>
/// <strong>T82 (#359, bug #357): attacking no longer declares war.</strong> Every attack and siege below
/// targets only a nation this AI is <em>already</em> at war with —
/// <c>decompiled-ai-offers-to-human-seats.md</c> §4/§5 <strong>[confirmed]</strong>: "AI armies and
/// fleets never attack a nation they are not at war with... there is no implicit declaration by attack".
/// War itself is a separate decision, <see cref="ProposeOwnWarDeclaration"/>, reproducing
/// <c>FUN_0044FB7C</c>'s own war-target search (§1a) — busy, not protected, a
/// <see cref="Diplomacy.NeighbourGeography"/> neighbour, the best power ratio, gated by a
/// <c>Random(10)</c> roll. This replaces the earlier declare-then-attack pairing entirely: it used to
/// declare war on <em>any</em> adjacent foreign city or army, allies included, which is exactly what bug
/// #357 found wrong (T65's #256 fixture: the AI declaring war on its own new ally the next turn).
/// </para>
/// </remarks>
public static class AiMilitaryPhase
{
    /// <summary>Adds every military candidate this state offers to <paramref name="into"/>.</summary>
    /// <param name="view">The shared reads.</param>
    /// <param name="personality">The acting nation's personality.</param>
    /// <param name="rng">The turn's generator, used only to derive named estimate streams.</param>
    /// <param name="armiesAlreadyMarched">
    /// Army <em>and fleet</em> ids that have already been given a march order this turn, and are
    /// therefore offered no second one. See <see cref="ProposeMarches"/> for why the ration exists.
    /// </param>
    /// <param name="into">The collecting list.</param>
    /// <param name="siegeGates">
    /// Optional diagnostics. When supplied, <see cref="ProposeSieges"/> records what each of its gates
    /// did into it — see <see cref="AiSiegeGateTally"/> for why that measurement exists and why it
    /// cannot change what the AI decides.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument but <paramref name="siegeGates"/> is null.</exception>
    public static void Propose(
        AiView view,
        AiPersonalityProfile personality,
        IRng rng,
        IReadOnlyList<string> armiesAlreadyMarched,
        List<AiCandidate> into,
        AiSiegeGateTally? siegeGates = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(armiesAlreadyMarched);
        ArgumentNullException.ThrowIfNull(into);

        var requiredRatio = AiView.RequiredAttackRatioPermille(personality.AggressionPermille);
        var progress = view.VictoryProgressPermille();

        ProposeOwnWarDeclaration(view, rng, into);

        foreach (var army in view.OwnArmies())
        {
            if (army.IsEmbarked || army.Moves <= 0)
            {
                // Both are AttackLegality gates and a MoveArmyCommand gate; an army in either state has
                // nothing to contribute to this phase at all.
                continue;
            }

            ProposeSieges(view, army, requiredRatio, progress, into, siegeGates);
            ProposeArmyAttacks(view, army, requiredRatio, into);
            if (!Contains(armiesAlreadyMarched, army.Id))
            {
                ProposeMarches(view, army, progress, into);
            }

        }

        foreach (var fleet in view.OwnFleets())
        {
            if (fleet.IsUnderConstruction || fleet.Moves <= 0)
            {
                continue;
            }

            ProposeFleetAttacks(view, fleet, requiredRatio, rng, into);
            if (!Contains(armiesAlreadyMarched, fleet.Id))
            {
                ProposeFleetMarches(view, fleet, into);
            }
        }
    }

    /// <summary>
    /// T82 (#359, bug #357): the AI's own war-target search — <c>FUN_0044FB7C</c>'s war half
    /// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong>. The busy gate, the
    /// "protected" test, the neighbour test and the power-ratio comparison are all
    /// <see cref="Diplomacy.AiOwnDiplomacyRule.BestWarTarget"/>'s own, deterministic given the current
    /// state; this method's only job is the <c>Random(10) == 0</c> roll and proposing the one command it
    /// gates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Given the very highest score of any candidate this AI can offer, deliberately.</strong>
    /// The original's own routine runs this decision <em>before</em> its military phase, once, as one
    /// atomic step; this engine's greedy "propose everything, score it, take the best, look again" loop
    /// (<see cref="AiTurn"/>) has no equivalent single atomic pass to put it in. A war declaration
    /// therefore has to outscore every attack, siege, march, recruitment and trade candidate this AI
    /// could otherwise offer the same turn, so that — whenever the roll says to declare — it is chosen
    /// on the very first action it is offered, before any other candidate involving the same target
    /// (most importantly, a trade candidate for that same nation:
    /// <see cref="Diplomacy.AiOwnDiplomacyRule"/>'s own remarks explain why the original's own loop order
    /// never trades with a nation and then declares war on it the same turn, which this score ordering
    /// preserves).
    /// </para>
    /// <para>
    /// <strong>The roll is drawn once per seat-turn, not once per proposal.</strong> This method is
    /// called again after every action this seat takes (<see cref="Propose"/> is re-run each time), so a
    /// naive draw here would ask the dice again and again while the target remains eligible. <c>rng</c>
    /// is the one <see cref="IRng"/> instance <see cref="AiTurn.Run"/> constructs for this seat's whole
    /// turn and threads through every one of this phase's proposal passes unchanged, so
    /// <see cref="IRng.ForStream"/> on it, keyed by the seat and the current turn, "returns two
    /// generators that produce the same sequence" (that method's own words) no matter how many times
    /// this runs before the roll is acted on or the target stops being eligible.
    /// </para>
    /// </remarks>
    private static void ProposeOwnWarDeclaration(AiView view, IRng rng, List<AiCandidate> into)
    {
        var target = AiOwnDiplomacyRule.BestWarTarget(view.State, view.Ruleset, view.World, view.NationId);
        if (target is null)
        {
            return;
        }

        var denominator = view.Ruleset.Diplomacy.AiOwnDiplomacy.WarDeclareRollDenominator;
        var roll = rng.ForStream(Inv("ai.ownDiplomacy.warRoll:{0}:{1}", view.NationId, view.State.Calendar.TurnIndex));
        if (!roll.NextChance(1, denominator))
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Military,
            "declare-war",
            new DeclareWarCommand(view.NationId, target),
            OwnWarDeclarationScore,
            Inv("declare war on {0}: FUN_0044FB7C's own war-target search picked it, and the Random({1}) roll hit", target, denominator)));
    }

    /// <summary>
    /// Comfortably above every other score this AI can produce (<see cref="AiWeights.BesiegeCityBaseScore"/>,
    /// its highest, is 6000 before any ratio bonus) — see <see cref="ProposeOwnWarDeclaration"/>'s own
    /// remarks for why that dominance is load-bearing, not just a preference.
    /// </summary>
    /// <remarks>
    /// Rework round 1, N5: this constant lives outside <see cref="AiWeights"/>, so it is not covered by
    /// T79's own Done-when 2 ("nothing reads a C# constant") the way <c>AiWeights.cs</c>'s own fields are
    /// — left here rather than moved (out of T82's Owns list to relocate), flagged so T79 (#355) finds it
    /// when that task widens its own sweep.
    /// </remarks>
    private const long OwnWarDeclarationScore = 10_000_000;

    /// <summary>
    /// Sails a fleet at the nearest enemy fleet. The naval half of "<em>reinforce, hold, or attack</em>":
    /// a fleet that never moves can never reach the fleet it would attack, and
    /// <see cref="ProposeFleetAttacks"/> would then be a branch that can only fire on a map where two
    /// fleets happen to start adjacent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The destination is the target fleet's own tile, which <see cref="MoveFleetCommandHandler"/>'s
    /// blocked predicate refuses to enter, so the walk stops in the adjacent cell —
    /// <see cref="AttackLegality"/>'s adjacency gate satisfied, exactly as an army's march at a city
    /// works.
    /// </para>
    /// <para>
    /// <strong>There is no hold rule, for fleets or for armies.</strong> An earlier revision of this
    /// remark claimed "the same hold rule applies: a fleet already adjacent to an enemy fleet stays put
    /// rather than being pulled at a second one". That described a rule this file does not contain and
    /// never did — the only guard is per target (<c>distance &lt;= 1</c> skips the fleet you are already
    /// touching), so a fleet in contact with a <em>stronger</em> enemy it has declined to attack will
    /// happily sail at a weaker one further off. The code is right and the remark was wrong, on three
    /// grounds. <strong>(1)</strong> There is no "same" hold rule to apply: the army hold was removed
    /// earlier in this very task, because parking units produced turns that issued no command and
    /// changed no state — the stall <c>docs/task-catalogue.md</c> T22 Done-when 1 fails a soak for.
    /// <strong>(2)</strong> Adjacency confers nothing in this engine. There is no zone of control and no
    /// attack of opportunity, so standing next to an enemy you will not fight buys no advantage, and
    /// leaving may even break the adjacency that enemy needs to attack <em>you</em>.
    /// <strong>(3)</strong> The inline claim that sailing away from a fleet you are touching "is never
    /// the better move" is false on its face: if you cannot beat the one you are touching and can beat
    /// another, moving is strictly better. Pinned by
    /// <c>AiMilitaryPhaseTests.A_fleet_beside_a_stronger_enemy_sails_at_a_weaker_one_further_off</c>, so
    /// the remark cannot drift back.
    /// </para>
    /// <para>
    /// <strong>Every gate <see cref="MoveFleetCommandHandler"/> applies is checked first</strong>: the
    /// fleet is ours (it came from <see cref="AiView.OwnFleets"/>), it is launched, it has moves, and the
    /// destination is on the map. The handler additionally refuses nothing else — a walk that makes no
    /// progress is accepted, not refused — so there is no further refusal to anticipate.
    /// </para>
    /// </remarks>
    private static void ProposeFleetMarches(AiView view, FleetState fleet, List<AiCandidate> into)
    {
        foreach (var target in view.State.Fleets)
        {
            if (string.Equals(target.Nation, fleet.Nation, StringComparison.Ordinal) || target.IsUnderConstruction)
            {
                continue;
            }

            var distance = AiView.Distance(fleet.X, fleet.Y, target.X, target.Y);
            if (distance <= 1)
            {
                // Already in contact with THIS one: ProposeFleetAttacks decides whether to fight it, and
                // a march onto a tile the walk cannot enter anyway would achieve nothing. Deliberately
                // per target and not a hold: being in contact with one enemy does not stop a march at a
                // different one. See this method's remarks.
                continue;
            }

            if ((uint)target.X >= (uint)view.World.Width || (uint)target.Y >= (uint)view.World.Height)
            {
                continue;
            }

            // The army march's rule, mirrored: a sail whose very first traced step is water the fleet
            // cannot enter achieves nothing. MoveFleetCommandHandler accepts it -- a walk that makes no
            // progress is not a refusal -- so the AI declines it rather than spending a command on it.
            var path = BresenhamPath.Trace(new GridPoint(fleet.X, fleet.Y), new GridPoint(target.X, target.Y));
            if (path.Count < 2 || !view.IsFleetPassable(path[1]))
            {
                continue;
            }

            var score = AiWeights.SailAtFleetBaseScore - (AiWeights.DistancePenaltyPerTile * distance);
            if (score < AiWeights.MinimumActionScore)
            {
                continue;
            }

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                AiCandidate.SailKind,
                new MoveFleetCommand(view.NationId, fleet.Id, target.X, target.Y),
                score,
                Inv(
                    "sail {0} at fleet {1} ({2}): {3} tiles away",
                    fleet.Id, target.Id, target.Nation, distance),
                fleet.Id));
        }
    }

    private static void ProposeSieges(
        AiView view,
        ArmyState army,
        long requiredRatio,
        long progress,
        List<AiCandidate> into,
        AiSiegeGateTally? siegeGates)
    {
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset);
        var fortifyOrder = FortifyOrder(view.Ruleset);
        if (archerUnitTypeId is null || fortifyOrder is null)
        {
            // AttackLegality refuses every siege under such a ruleset, so proposing one would be a
            // guaranteed rejection.
            siegeGates?.RecordRulesetCannotSiege();
            return;
        }

        foreach (var city in view.State.Cities)
        {
            if (string.Equals(city.Owner, army.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            // T82 (#359, bug #357): "AI armies and fleets never attack a nation they are not at war
            // with... there is no implicit declaration by attack" (decompiled-ai-offers-to-human-seats.md
            // §4/§5 [confirmed]). War itself is ProposeOwnWarDeclaration's own decision now; sieging no
            // longer declares it. This early exit is redundant with, but cheaper than, the besiege
            // command's own AttackLegality.IsLegal check below (the actual authority -- its WarCheck
            // already refused a siege against a nation not at war, even before this task, which is
            // exactly why the old code paired a DeclareWarCommand ahead of the siege to begin with): this
            // just skips the power/ratio computation for a target the legality check would refuse anyway.
            if (!view.IsAtWar(view.NationId, city.Owner))
            {
                continue;
            }

            if (!AttackLegality.AreAdjacent(army.X, army.Y, city.X, city.Y))
            {
                continue;
            }

            if (view.State.NationById(city.Owner) is not { } owner)
            {
                continue;
            }

            var besiege = new BesiegeCityCommand(view.NationId, army.Id, city.Id);
            if (!AttackLegality.IsLegal(view.State, view.Ruleset, besiege))
            {
                siegeGates?.RecordLegalityRejection();
                continue;
            }

            var attackerPower = SiegeStrength.Attacker(army.Units, army.Morale, view.Ruleset, archerUnitTypeId);
            var defenderPower = Cities.Capture.CompleteDefenderStrength.Compute(
                city,
                fortifyOrder,
                IsControllerCapital(owner, city),
                !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal),
                owner,
                view.Ruleset);

            var ratio = AiView.RatioPermille(attackerPower, defenderPower);
            siegeGates?.RecordRatioGate(
                ratio >= requiredRatio, army.Id, city.Id, attackerPower, defenderPower, ratio, requiredRatio);
            if (ratio < requiredRatio)
            {
                continue;
            }

            var score = AiView.WithVictoryAwareness(
                AiWeights.BesiegeCityBaseScore + AiView.RatioScoreContribution(ratio), progress);

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                "besiege",
                besiege,
                score,
                Inv(
                    "besiege {0} with {1}: siege strength {2} vs defender {3} "
                    + "(ratio {4} permille, need {5}), victory progress {6} permille",
                    city.Id, army.Id, attackerPower, defenderPower, ratio, requiredRatio, progress),
                army.Id));
        }
    }

    private static void ProposeArmyAttacks(
        AiView view, ArmyState army, long requiredRatio, List<AiCandidate> into)
    {
        var attackerPower = ArmyPower.Compute(army.Units, army.Morale, view.Ruleset);

        foreach (var target in view.State.Armies)
        {
            if (string.Equals(target.Nation, army.Nation, StringComparison.Ordinal) || target.IsEmbarked)
            {
                continue;
            }

            // T84 (bug #366): every diplomatic targeting path in the original gates on the target's
            // unity > 0 (report §5 [confirmed], the war/treaty picks, the Politics screen and the
            // turn-start offer roll) -- the report does not establish the same for military targeting.
            // Extending the gate to this AI's own attack proposers is [derived] (task entry's own tag):
            // after this task an eliminated nation's armies/fleets no longer exist anyway, but a
            // hand-built state could still name one, so this filter is explicit rather than relying on
            // the list being empty.
            if (view.State.NationById(target.Nation)?.Eliminated == true)
            {
                continue;
            }

            // T82 (#359, bug #357): attack only a nation already at war -- see ProposeSieges' own
            // remark (including why this is a redundant-but-cheaper early exit, not the actual
            // authority); the same report citation applies to every attack command in this file.
            if (!view.IsAtWar(view.NationId, target.Nation))
            {
                continue;
            }

            if (!AttackLegality.AreAdjacent(army.X, army.Y, target.X, target.Y))
            {
                continue;
            }

            var attack = new AttackArmyCommand(view.NationId, army.Id, target.Id);
            if (!AttackLegality.IsLegal(view.State, view.Ruleset, attack))
            {
                continue;
            }

            var defenderPower = ArmyPower.Compute(target.Units, target.Morale, view.Ruleset);
            var ratio = AiView.RatioPermille(attackerPower, defenderPower);
            if (ratio < requiredRatio)
            {
                continue;
            }

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                "attack-army",
                attack,
                AiWeights.AttackArmyBaseScore + AiView.RatioScoreContribution(ratio),
                Inv(
                    "attack {0} ({1}) with {2}: field strength {3} vs {4} "
                    + "(ratio {5} permille, need {6})",
                    target.Id, target.Nation, army.Id, attackerPower, defenderPower, ratio, requiredRatio),
                army.Id));
        }
    }

    private static void ProposeFleetAttacks(
        AiView view, FleetState fleet, long requiredRatio, IRng rng, List<AiCandidate> into)
    {
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset);
        if (archerUnitTypeId is null)
        {
            return;
        }

        foreach (var target in view.State.Fleets)
        {
            if (string.Equals(target.Nation, fleet.Nation, StringComparison.Ordinal) || target.IsUnderConstruction)
            {
                continue;
            }

            // T84 (bug #366): see ProposeArmyAttacks's own remark -- [derived], not the report §5
            // diplomacy gate itself, that this AI never targets an eliminated nation's fleets either.
            if (view.State.NationById(target.Nation)?.Eliminated == true)
            {
                continue;
            }

            // T82 (#359, bug #357): attack only a nation already at war -- see ProposeSieges' own remark.
            if (!view.IsAtWar(view.NationId, target.Nation))
            {
                continue;
            }

            if (!AttackLegality.AreAdjacent(fleet.X, fleet.Y, target.X, target.Y))
            {
                continue;
            }

            var attack = new AttackFleetCommand(view.NationId, fleet.Id, target.Id);
            if (!AttackLegality.IsLegal(view.State, view.Ruleset, attack))
            {
                continue;
            }

            // One sample each, from a stream named by the pair: stable for a given state, and derived
            // rather than drawn, so the battle's own rolls are untouched. See this class's remarks.
            var estimate = rng.ForStream($"ai.fleet-estimate:{fleet.Id}:{target.Id}");
            var attackerPower = FleetPower.Compute(
                fleet.Ships, fleet.ConditionPercent, estimate, view.Ruleset,
                CarriedArmyStrengthOf(view, fleet, archerUnitTypeId));
            var defenderPower = FleetPower.Compute(
                target.Ships, target.ConditionPercent, estimate, view.Ruleset,
                CarriedArmyStrengthOf(view, target, archerUnitTypeId));

            var ratio = AiView.RatioPermille(attackerPower, defenderPower);
            if (ratio < requiredRatio)
            {
                continue;
            }

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                "attack-fleet",
                attack,
                AiWeights.AttackFleetBaseScore + AiView.RatioScoreContribution(ratio),
                Inv(
                    "attack fleet {0} ({1}) with {2}: sampled naval strength {3} vs {4} "
                    + "(ratio {5} permille, need {6})",
                    target.Id, target.Nation, fleet.Id, attackerPower, defenderPower, ratio, requiredRatio),
                fleet.Id));
        }
    }

    /// <summary>
    /// The "reinforce, hold" half: march at an enemy city worth taking, or at one of this nation's own
    /// cities that a hostile army is standing next to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The destination is the target's own tile, and that is deliberate.</strong> A city, an army
    /// and a fleet each occupy a marker cell the movement walk refuses to enter
    /// (<see cref="MoveArmyCommandHandler"/>'s blocked predicate, and
    /// <c>terrain-move-cost-table-in-dat.md</c>'s "codes ≥ 12 are not steppable at all by this walk"), so
    /// ordering a march <em>onto</em> a city walks the army up to the city and stops it in the adjacent
    /// cell — precisely where <see cref="AttackLegality"/> needs it to stand to besiege next turn. The AI
    /// does not compute that stopping point; the walker does.
    /// </para>
    /// <para>
    /// <strong>One march per army per turn, and why that rule is here rather than emergent.</strong> The
    /// score of a march is its objective's value minus a distance penalty, so an army standing between
    /// two objectives is pulled toward whichever it is currently nearer — and, having stepped toward it,
    /// is now nearer the other. Left alone the greedy loop oscillates: an army with ten moves spends all
    /// ten walking back and forth between two cities and ends the turn where it started. That is not a
    /// stall by <c>docs/task-catalogue.md</c> T22 Done-when 1's definition (commands were issued and the
    /// state did change), which is exactly why it has to be designed out rather than caught: a test that
    /// only watched for stalls would have passed on it. So an army is given one destination per turn and
    /// the walker spends its moves getting there, which is also what a human seat does with one click.
    /// Attacks are <em>not</em> rationed this way and do not need to be — an attack zeroes its attacker's
    /// moves (<see cref="Battle.InstantBattleResolver"/>), so the engine already allows exactly one.
    /// </para>
    /// <para>
    /// <strong>What is checked before proposing.</strong> Every gate
    /// <see cref="MoveArmyCommandHandler"/> applies: the army exists and is ours (it came from
    /// <see cref="AiView.OwnArmies"/>), it has moves, and the destination is on the map. Plus one the
    /// handler does not apply and the AI wants anyway — that the first step of the traced path is a tile
    /// an army can stand on (<see cref="AiView.IsArmyPassable"/>). That is not a legality check and is not
    /// treated as one: it is the AI declining to walk into the sea. See this task's PR for the upstream
    /// note.
    /// </para>
    /// </remarks>
    private static void ProposeMarches(AiView view, ArmyState army, long progress, List<AiCandidate> into)
    {
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset);
        var fortifyOrder = FortifyOrder(view.Ruleset);

        foreach (var city in view.State.Cities)
        {
            var isOwn = string.Equals(city.Owner, view.NationId, StringComparison.Ordinal);
            var distance = AiView.Distance(army.X, army.Y, city.X, city.Y);
            if (distance <= 1)
            {
                // Already in place: besieging it, or garrisoning it, is another candidate's job.
                continue;
            }

            long baseScore;
            long weakness = 0;
            string why;
            if (isOwn)
            {
                if (!view.IsThreatened(city))
                {
                    continue;
                }

                baseScore = AiWeights.ReinforceCityBaseScore + AiWeights.ThreatenedCityBonus;
                why = "reinforce threatened";
            }
            else
            {
                baseScore = AiWeights.ApproachCityBaseScore;
                weakness = SiegeRatioAgainst(view, army, city, archerUnitTypeId, fortifyOrder);
                why = "march at";
            }

            var command = new MoveArmyCommand(view.NationId, army.Id, city.X, city.Y);
            if (!IsProposableMove(view, army, command))
            {
                continue;
            }

            var score = baseScore + weakness - (AiWeights.DistancePenaltyPerTile * distance);
            if (score < AiWeights.MinimumActionScore)
            {
                continue;
            }

            if (!isOwn)
            {
                score = AiView.WithVictoryAwareness(score, progress);
            }

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                isOwn ? AiCandidate.ReinforceKind : AiCandidate.ApproachKind,
                command,
                score,
                Inv(
                    "{0} {1} with {2}: {3} tiles away, base score {4}, siege ratio {5} permille",
                    why, city.Id, army.Id, distance, baseScore, weakness),
                army.Id));
        }
    }

    /// <summary>
    /// How close this army is to being able to take this city, on the same permille scale the siege gate
    /// uses and computed from the same two engine functions — so the city an army marches at is the one
    /// it is nearest to taking, not merely the one it is nearest to.
    /// </summary>
    /// <remarks>
    /// This is the term that stops the march scoring being a pure distance contest between two equally
    /// unreachable objectives, which is what made an army oscillate between them: the weaker city keeps
    /// the higher score from wherever the army happens to be standing. Zero when the ruleset does not
    /// declare the two ids the siege path needs, or when the city's owner does not resolve — in which
    /// case no siege could ever be proposed there either.
    /// </remarks>
    private static long SiegeRatioAgainst(
        AiView view, ArmyState army, CityState city, string? archerUnitTypeId, CityOrderRule? fortifyOrder)
    {
        if (archerUnitTypeId is null || fortifyOrder is null || view.State.NationById(city.Owner) is not { } owner)
        {
            return 0;
        }

        var attackerPower = SiegeStrength.Attacker(army.Units, army.Morale, view.Ruleset, archerUnitTypeId);
        var defenderPower = Cities.Capture.CompleteDefenderStrength.Compute(
            city,
            fortifyOrder,
            IsControllerCapital(owner, city),
            !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal),
            owner,
            view.Ruleset);

        return AiView.RatioScoreContribution(AiView.RatioPermille(attackerPower, defenderPower));
    }

    /// <summary>
    /// The <see cref="MoveArmyCommandHandler"/> gates, checked in advance, plus the passability of the
    /// first traced step.
    /// </summary>
    private static bool IsProposableMove(AiView view, ArmyState army, MoveArmyCommand command)
    {
        if (army.Moves <= 0)
        {
            return false;
        }

        if ((uint)command.X >= (uint)view.World.Width || (uint)command.Y >= (uint)view.World.Height)
        {
            return false;
        }

        // BresenhamPath.Trace is the walker's own path function; [0] is the origin, so [1] is the first
        // cell the army would enter. A march whose very first step is impassable achieves nothing.
        var path = BresenhamPath.Trace(new GridPoint(army.X, army.Y), new GridPoint(command.X, command.Y));
        return path.Count >= 2 && view.IsArmyPassable(path[1]);
    }

    private static FleetPower.CarriedArmyStrength? CarriedArmyStrengthOf(
        AiView view, FleetState fleet, string archerUnitTypeId)
    {
        if (fleet.CarriedArmyId is not { } carriedId || view.State.ArmyById(carriedId) is not { } carried)
        {
            return null;
        }

        return new FleetPower.CarriedArmyStrength(carried.Units, carried.Morale, archerUnitTypeId);
    }

    /// <summary>Ordinal membership over a small list — no <c>HashSet</c>, which the determinism guard bans.</summary>
    private static bool Contains(IReadOnlyList<string> ids, string id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControllerCapital(NationState owner, CityState city) =>
        owner.CapitalCityId is { } capital && string.Equals(capital, city.Id, StringComparison.Ordinal);

    private static CityOrderRule? FortifyOrder(Ruleset ruleset)
    {
        if (BattleCommandRuleset.FortificationOrderIdIn(ruleset) is not { } orderId)
        {
            return null;
        }

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, orderId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        return null;
    }
}
