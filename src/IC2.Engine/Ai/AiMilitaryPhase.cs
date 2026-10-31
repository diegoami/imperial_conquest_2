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
/// <strong>This claim needed #425 to become true</strong>: <see cref="IsControllerCapital"/> used to read
/// only the current owner's own capital, so a city another nation's stale capital still named (T90/#409 —
/// a defected or eliminated nation's capital never moves) could score its defender strength without the
/// resolver's own ×5/3 capital multiplier, disagreeing with what <see cref="Battle.InstantBattleResolver"/>
/// would actually compute for the identical siege. #425 makes the two calls the same predicate
/// (<see cref="Cities.Capture.CapitalOwnership.IsAnyNationsCapital"/>), so the AI's ratio and the
/// resolver's real outcome can no longer disagree.
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
/// not a prediction, which is why <see cref="Model.AiWeightsRules.AttackFleetBaseScore"/> sits below both
/// land attacks.
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
    /// therefore offered no second one (an army greedily re-marching between two destinations would spend
    /// its moves oscillating); an army that has marched is still offered the attack its target allows.
    /// </param>
    /// <param name="into">The collecting list.</param>
    /// <param name="siegeGates">
    /// Optional diagnostics. When supplied, <see cref="ProposeArmyTree"/> records what the tree selected
    /// into it — see <see cref="AiSiegeGateTally"/> for why that measurement exists and why it
    /// cannot change what the AI decides.
    /// </param>
    /// <param name="boxedIn">
    /// Optional. Receives the id of every army the tree could give no command and whose garrison fallback
    /// and emergency move found no reachable tile (Hazards step 4); the turn driver logs
    /// <c>no reachable move</c> for each.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument but <paramref name="siegeGates"/> and <paramref name="boxedIn"/> is null.</exception>
    public static void Propose(
        AiView view,
        AiPersonalityProfile personality,
        IRng rng,
        IReadOnlyList<string> armiesAlreadyMarched,
        List<AiCandidate> into,
        AiSiegeGateTally? siegeGates = null,
        ICollection<string>? boxedIn = null)
        => ProposeCore(view, personality, rng, armiesAlreadyMarched, into, siegeGates, boxedIn, null);

    internal static AiView ProposeInOriginalArmyOrder(AiView view, AiPersonalityProfile personality, IRng rng,
        IReadOnlyList<string> armiesAlreadyMarched, List<AiCandidate> into, AiSiegeGateTally siegeGates,
        ICollection<string> boxedIn, OriginalArmyOrder armyOrder, Func<AiView, ArmyState, AiView>? prepare = null)
        => ProposeCore(view, personality, rng, armiesAlreadyMarched, into, siegeGates, boxedIn, armyOrder, prepare);

    private static AiView ProposeCore(AiView view, AiPersonalityProfile personality, IRng rng,
        IReadOnlyList<string> armiesAlreadyMarched, List<AiCandidate> into, AiSiegeGateTally? siegeGates,
        ICollection<string>? boxedIn, OriginalArmyOrder? armyOrder, Func<AiView, ArmyState, AiView>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(armiesAlreadyMarched);
        ArgumentNullException.ThrowIfNull(into);

        var requiredRatio = AiView.RequiredAttackRatioPermille(personality.AggressionPermille, view.Ruleset);
        var progress = view.VictoryProgressPermille();

        ProposeOwnWarDeclaration(view, rng, into);

        if (armyOrder is not null)
        {
            view = armyOrder.Propose(view, progress, armiesAlreadyMarched, into, siegeGates, boxedIn, prepare);
        }
        else
        {
            foreach (var army in view.OwnArmies())
            {
                ProposeArmy(view, army, progress, armiesAlreadyMarched, into, siegeGates, boxedIn);
            }
        }

        // F608 runs once after army requests. Do not also offer the designed sailing/attack candidates.
        if (ClassicalFleetNavigation.Applies(view)) return view;
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
        return view;
    }

    internal static void ProposeArmy(AiView view, ArmyState army, long progress,
        IReadOnlyList<string> marched, List<AiCandidate> into, AiSiegeGateTally? siegeGates,
        ICollection<string>? boxedIn)
    {
        if (army.Nation != view.NationId || army.Moves <= 0
            || (army.IsEmbarked && !ClassicalArmyNavigation.Applies(view))) return;
        ProposeArmyTree(view, army, progress, marched, into, siegeGates, boxedIn);
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
            view.Ruleset.Ai.OwnWarDeclarationScore,
            Inv("declare war on {0}: FUN_0044FB7C's own war-target search picked it, and the Random({1}) roll hit", target, denominator)));
    }

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

            // Mirror MoveFleetCommandHandler.IsBlocked: a launched fleet at the first step blocks the
            // sail, just as IsProposableMove mirrors MoveArmyCommandHandler.IsBlocked for armies.
            // The passability check above handles terrain; this handles occupancy only.
            var firstStep = path[1];
            var blockedByFleetAtFirstStep = false;
            foreach (var otherFleet in view.State.Fleets)
            {
                if (!string.Equals(otherFleet.Id, fleet.Id, StringComparison.Ordinal)
                    && !otherFleet.IsUnderConstruction
                    && otherFleet.X == firstStep.X && otherFleet.Y == firstStep.Y)
                {
                    blockedByFleetAtFirstStep = true;
                    break;
                }
            }

            if (blockedByFleetAtFirstStep)
            {
                continue;
            }

            var score = view.Ruleset.Ai.SailAtFleetBaseScore - (view.Ruleset.Ai.DistancePenaltyPerTile * distance);
            if (score < view.Ruleset.Ai.MinimumActionScore)
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

    /// <summary>
    /// T156 (issue #925): the original's per-army target tree (<c>2026-10-07-strategic-ai-turn.md</c>
    /// §3.3) decides each army's action in place of the designed siege-ratio gate and march weights. The
    /// tree picks the target and the existing siege, attack and move commands carry it out; an army that
    /// has already marched this turn is offered only the attack its target allows, never a second march.
    /// </summary>
    private static void ProposeArmyTree(
        AiView view,
        ArmyState army,
        long progress,
        IReadOnlyList<string> armiesAlreadyMarched,
        List<AiCandidate> into,
        AiSiegeGateTally? siegeGates,
        ICollection<string>? boxedIn)
    {
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset);
        var fortifyOrder = BattleCommandRuleset.FortificationOrderIdIn(view.Ruleset);
        if (archerUnitTypeId is null || fortifyOrder is null)
        {
            // AttackLegality refuses every siege under such a ruleset, so proposing one would be a
            // guaranteed rejection. A ruleset that cannot describe a siege cannot be scored by the tree.
            siegeGates?.RecordRulesetCannotSiege();
            return;
        }

        var atWar = AnyAtWar(view);
        var decision = AiArmyTargetTree.DecideUnlogged(view, army, atWar, out var decisionRecord);
        var mayMarch = !Contains(armiesAlreadyMarched, army.Id);
        var candidatesBefore = into.Count;

        if (army.IsEmbarked && ClassicalArmyNavigation.Applies(view))
        {
            if (!mayMarch) return;
            var target = decision.Selected switch
            {
                AiArmyTargetTree.Kind.AttackCity when decision.TargetCity is { } c => new GridPoint(c.X, c.Y),
                AiArmyTargetTree.Kind.AttackArmy when decision.TargetArmy is { } a => new GridPoint(a.X, a.Y),
                AiArmyTargetTree.Kind.MercenaryRun when decision.After == AiArmyTargetTree.Continuation.MarchAtCityTarget
                    && decision.TargetCity is { } c => new GridPoint(c.X, c.Y),
                _ when decision.Resupply?.City is { } c => new GridPoint(c.X, c.Y),
                _ => (GridPoint?)null,
            };
            if (target is { } point)
                AddMarchCandidate(view, army, point.X, point.Y, AiCandidate.ApproachKind, "request carried-army landing",
                    view.Ruleset.Ai.ApproachCityBaseScore, progress, into);
            if (into.Count > candidatesBefore) AiArmyTargetTree.LogDecision(decisionRecord);
            return;
        }

        if (siegeGates is not null)
        {
            var cityScore = AiArmyTargetTree.ScoreCityTarget(view, army);
            var armyScore = AiArmyTargetTree.ScoreArmyTarget(view, army);
            foreach (var city in view.State.Cities)
            {
                if (string.Equals(city.Owner, army.Nation, StringComparison.Ordinal)
                    || AiView.Distance(army.X, army.Y, city.X, city.Y) != 1)
                {
                    continue;
                }

                siegeGates.RecordAdjacentPair(army.Id, city.Id, cityScore.Score, armyScore.Score);
            }
        }

        switch (decision.Selected)
        {
            case AiArmyTargetTree.Kind.AttackCity:
                if (decision.TargetCity is { } targetCity)
                {
                    AddCityTargetCandidate(view, army, targetCity, progress, mayMarch, into, siegeGates);
                }

                break;

            case AiArmyTargetTree.Kind.AttackArmy:
                if (decision.TargetArmy is { } targetArmy)
                {
                    AddArmyTargetCandidate(view, army, targetArmy, progress, mayMarch, into,
                        originalTargetTreeRoute: true);
                }

                break;

            case AiArmyTargetTree.Kind.MercenaryRun:
                // The run itself (FUN_0044e84c): toward the nearest live offer. The hire happens in the
                // next turn's start-of-turn pass, once the army is within the hire range. The
                // continuation is the destination call the report lists after it. Faithful dispatches
                // it immediately after the run using the retained scorer target; Improved keeps its
                // existing one-march policy while an offer is available.
                if (mayMarch && AiArmyTargetTree.MercenaryRunDestination(view, army) is { } offerCity)
                {
                    var beforeRun = into.Count;
                    AddMarchCandidate(view, army, offerCity.X, offerCity.Y, AiCandidate.ApproachKind,
                        "mercenary run", view.Ruleset.Ai.ApproachCityBaseScore, progress, into);
                    if (ClassicalArmyNavigation.Applies(view) && into.Count > beforeRun)
                    {
                        GridPoint? continuation = decision.After switch
                        {
                            AiArmyTargetTree.Continuation.MarchAtCityTarget when decision.TargetCity is { } c => new(c.X, c.Y),
                            AiArmyTargetTree.Continuation.ChaseArmy when decision.TargetArmy is { } a => new(a.X, a.Y),
                            _ => null,
                        };
                        into[^1] = into[^1] with { OriginalMercenaryContinuation = continuation };
                    }
                    break;
                }

                switch (decision.After)
                {
                    case AiArmyTargetTree.Continuation.MarchAtCityTarget:
                        if (mayMarch && decision.TargetCity is { } cityTarget)
                        {
                            AddMarchCandidate(view, army, cityTarget.X, cityTarget.Y, AiCandidate.ApproachKind,
                                "march at the enemy-city target", view.Ruleset.Ai.ReinforceCityBaseScore, progress, into);
                        }

                        break;

                    case AiArmyTargetTree.Continuation.DefendResupplyCity:
                        if (mayMarch && decision.Resupply?.City is { } defendCity)
                        {
                            AddMarchCandidate(view, army, defendCity.X, defendCity.Y, AiCandidate.ApproachKind,
                                "defend the resupply city", view.Ruleset.Ai.ReinforceCityBaseScore, progress, into);
                        }

                        break;

                    case AiArmyTargetTree.Continuation.ChaseArmy:
                        if (decision.TargetArmy is { } chaseArmy)
                        {
                            AddArmyTargetCandidate(view, army, chaseArmy, progress, mayMarch, into,
                                originalTargetTreeRoute: false);
                        }

                        break;
                }

                break;

            case AiArmyTargetTree.Kind.MoveToResupplyCity:
                if (mayMarch && decision.Resupply?.City is { } resupplyCity)
                {
                    AddMarchCandidate(view, army, resupplyCity.X, resupplyCity.Y, AiCandidate.ApproachKind,
                        "move to the resupply city", view.Ruleset.Ai.ReinforceCityBaseScore, progress, into);
                }

                break;
        }

        // Keep the original tree record only when that branch actually offered a command. A fallback
        // has its own candidate rationale; it must not inherit a rejected attack's decision record.
        var originalActionOffered = into.Count > candidatesBefore;

        // The original's tree always ends in an action for an army with moves: when the chosen branch
        // produced no executable command (nothing proposable to march at, an adjacent target whose
        // attack is not legal), the garrison fallback (FUN_0044ebe8) picks the destination. The
        // post-execution fallback in AiTurn covers an accepted command that moved nothing; this covers
        // the army that never got a command at all. Hazards' three-tier fallback (reachable garrison
        // candidates first, then the emergency move the walker can actually reach, then "stay put"
        // with the log message) handles the case the review proved: an army boxed in by obstacles
        // behind both the resupply and the garrison destination.
        if (mayMarch && into.Count == candidatesBefore)
        {
            var garrison = AiArmyTargetTree.GarrisonFallback(view, army, view.State);
            var fallbackDest = garrison ?? (ClassicalArmyNavigation.Applies(view) ? null
                : AiArmyTargetTree.EmergencyMoveDestination(view, army, view.State));
            if (fallbackDest is { } dest)
            {
                var why = garrison is null
                    ? "emergency move (no garrison destination is reachable)"
                    : "garrison fallback";
                AddMarchCandidate(view, army, dest.X, dest.Y, AiCandidate.ApproachKind,
                    why, view.Ruleset.Ai.ReinforceCityBaseScore, progress, into);
            }
            else
            {
                // Boxed in: no resupply, garrison or emergency destination is reachable. The turn driver
                // writes the "no reachable move" line from this list, once per army per turn.
                boxedIn?.Add(army.Id);
            }
        }

        // This also retains attacks offered after an army has already marched, while omitting
        // rejected branches and boxed-in armies that offered no original action.
        if (originalActionOffered)
        {
            // The original target may be reached through a first waypoint that
            // initially increases geometric distance to the enemy. Carry the
            // actual accepted march goal in diagnostic records so the strict
            // decision/action test checks that route without relaxing attacks.
            if (decision.Selected == AiArmyTargetTree.Kind.AttackArmy)
            {
                var route = into.Skip(candidatesBefore).SelectMany(candidate => candidate.Commands)
                    .OfType<MoveArmyCommand>().FirstOrDefault(command => command.ArmyId == army.Id);
                if (route is not null)
                    decisionRecord = decisionRecord with { RouteX = route.X, RouteY = route.Y };
            }
            AiArmyTargetTree.LogDecision(decisionRecord);
        }
    }

    /// <summary>True when <paramref name="view"/>'s nation is at war with any other living nation.</summary>
    private static bool AnyAtWar(AiView view)
    {
        foreach (var nation in view.State.Nations)
        {
            if (string.Equals(nation.Id, view.NationId, StringComparison.Ordinal) || nation.Eliminated)
            {
                continue;
            }

            if (view.IsAtWar(view.NationId, nation.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The tree's <c>AttackCity</c> command: a <see cref="BesiegeCityCommand"/> when adjacent, a
    /// <see cref="Movement.Commands.MoveArmyCommand"/> toward the city otherwise (when a march is allowed).
    /// </summary>
    private static void AddCityTargetCandidate(
        AiView view,
        ArmyState army,
        CityState targetCity,
        long progress,
        bool mayMarch,
        List<AiCandidate> into,
        AiSiegeGateTally? siegeGates)
    {
        if (AttackLegality.AreAdjacent(army.X, army.Y, targetCity.X, targetCity.Y))
        {
            var besiege = new BesiegeCityCommand(view.NationId, army.Id, targetCity.Id);
            if (AttackLegality.IsLegal(view.State, view.Ruleset, besiege))
            {
                siegeGates?.RecordTreeSelection(army.Id, targetCity.Id);
                var score = AiView.WithVictoryAwareness(
                    view.Ruleset.Ai.BesiegeCityBaseScore, progress, view.Ruleset);
                into.Add(AiCandidate.Single(
                    AiPhase.Military,
                    "besiege",
                    besiege,
                    score,
                    Inv("besiege {0} with {1} (target tree)", targetCity.Id, army.Id),
                    army.Id));
            }

            return;
        }

        if (mayMarch)
        {
            AddMarchCandidate(view, army, targetCity.X, targetCity.Y, AiCandidate.ApproachKind,
                "approach target city", view.Ruleset.Ai.ApproachCityBaseScore, progress, into);
        }
    }

    /// <summary>
    /// The tree's <c>AttackArmy</c> command: an <see cref="AttackArmyCommand"/> when adjacent, a
    /// <see cref="Movement.Commands.MoveArmyCommand"/> toward the army's tile otherwise (when a march is allowed).
    /// </summary>
    private static void AddArmyTargetCandidate(
        AiView view,
        ArmyState army,
        ArmyState targetArmy,
        long progress,
        bool mayMarch,
        List<AiCandidate> into,
        bool originalTargetTreeRoute)
    {
        if (AttackLegality.AreAdjacent(army.X, army.Y, targetArmy.X, targetArmy.Y))
        {
            var attack = new AttackArmyCommand(view.NationId, army.Id, targetArmy.Id);
            if (AttackLegality.IsLegal(view.State, view.Ruleset, attack))
            {
                into.Add(AiCandidate.Single(
                    AiPhase.Military,
                    "attack-army",
                    attack,
                    view.Ruleset.Ai.AttackArmyBaseScore,
                    Inv("attack {0} ({1}) with {2} (target tree)", targetArmy.Id, targetArmy.Nation, army.Id),
                    army.Id));
            }

            return;
        }

        if (mayMarch)
        {
            var x = targetArmy.X;
            var y = targetArmy.Y;
            var why = "approach target army";
            if (originalTargetTreeRoute && ClassicalArmyNavigation.Applies(view)
                && ClassicalArmyNavigation.Classify(army.X, army.Y)
                    == ClassicalArmyNavigation.Classify(x, y))
            {
                var origin = new GridPoint(army.X, army.Y);
                var target = new GridPoint(x, y);
                try
                {
                    if (!ClassicalArmyNavigation.DirectLine(view, origin, target))
                    {
                        var route = ClassicalArmyNavigation.Intermediate(view, origin, target);
                        if (route.Point is not { } waypoint) return;
                        x = waypoint.X;
                        y = waypoint.Y;
                        why = "approach target army via waypoint";
                    }
                }
                catch (NotSupportedException)
                {
                    // The promoted original contract explicitly leaves some island
                    // graph/stack contexts undefined. Fall through to the existing
                    // garrison fallback rather than invent a path or crash the turn.
                    return;
                }
            }
            // T156 rework round 1 N2: the other marches pass `progress` so the WithVictoryAwareness
            // multiplier tracks the actor's own progress toward its victory condition. The
            // minimum-score floor in AddMarchCandidate still masks the difference (the chosen
            // score sits at or above the floor in every preset), so this matches pass-through
            // behaviour and is the do-not-change-the-rules correction.
            AddMarchCandidate(view, army, x, y, AiCandidate.ApproachKind,
                why, view.Ruleset.Ai.ApproachCityBaseScore, progress, into,
                target: new GridPoint(targetArmy.X, targetArmy.Y));
        }
    }

    /// <summary>
    /// A <see cref="Movement.Commands.MoveArmyCommand"/> candidate toward the chosen destination, subject to
    /// the passability and occupancy gates of <see cref="IsProposableMove"/> (a march whose first step is
    /// blocked is a no-op the walker accepts).
    /// </summary>
    private static void AddMarchCandidate(
        AiView view,
        ArmyState army,
        int x,
        int y,
        string kind,
        string why,
        long baseScore,
        long progress,
        List<AiCandidate> into,
        GridPoint? target = null)
    {
        // T239 (#1592): a faithful march (the original's classical navigation) keeps walking toward the
        // target the tree chose, as FUN_0044dba8 does; (x, y) may be a waypoint. T247 also
        // hands cross-geography and carried-army requests to the same-seat ferry protocol.
        var aimedAt = target ?? new GridPoint(x, y);
        var continues = ClassicalArmyNavigation.Applies(view);
        var command = AiArmyTargetTree.MarchCommand(view, army, aimedAt,
            target is null ? null : new GridPoint(x, y));
        if (command is null) return;
        // The march must also make progress this turn: a first step dearer than the army's moves would
        // be accepted and park the army with its moves zeroed (Sol's R1 on PR #933). Not proposing it
        // lets the caller's fallback chain (garrison, then the emergency move) take over.
        if (continues)
        {
            if (army.Moves <= 0) return; // Do not filter original swing/stay-put by a single-walk probe.
        }
        else if (!IsProposableMove(view, army, command) || !AiArmyTargetTree.MarchMakesProgress(view, army, x, y))
        {
            return;
        }

        // The tree chose this destination; the designed per-tile penalty (which sank a march at anything
        // beyond ten tiles below the minimum score and left the army idle) does not apply to it. The
        // floor keeps the chosen march from being dropped by the minimum-score cut.
        var distance = AiView.Distance(army.X, army.Y, x, y);
        var score = Math.Max(
            AiView.WithVictoryAwareness(baseScore, progress, view.Ruleset),
            view.Ruleset.Ai.MinimumActionScore);

        into.Add(AiCandidate.Single(
            AiPhase.Military,
            kind,
            command,
            score,
            Inv("{0} ({1}, {2}) with {3}: {4} tiles away", why, x, y, army.Id, distance),
            army.Id));
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

            var ratio = AiView.RatioPermille(attackerPower, defenderPower, view.Ruleset);
            if (ratio < requiredRatio)
            {
                continue;
            }

            into.Add(AiCandidate.Single(
                AiPhase.Military,
                "attack-fleet",
                attack,
                view.Ruleset.Ai.AttackFleetBaseScore + AiView.RatioScoreContribution(ratio, view.Ruleset),
                Inv(
                    "attack fleet {0} ({1}) with {2}: sampled naval strength {3} vs {4} "
                    + "(ratio {5} permille, need {6})",
                    target.Id, target.Nation, fleet.Id, attackerPower, defenderPower, ratio, requiredRatio),
                fleet.Id));
        }
    }

    /// <summary>
    /// The <see cref="MoveArmyCommandHandler"/> gates, checked in advance; whether the march progresses
    /// is the walker's own answer (<c>AiArmyTargetTree.MarchMakesProgress</c>).
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

        // #1012: no first-step test here. The walker now steps around a city, army or fleet on the line,
        // so whether a march progresses is the walker's own answer, which every proposal also gets from
        // AiArmyTargetTree.MarchMakesProgress.
        return true;
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

    /// <summary>
    /// #425, rework round 1 N7 (caller count corrected): reads
    /// <see cref="Cities.Capture.CapitalOwnership.IsAnyNationsCapital"/> since T91 — any nation's capital,
    /// eliminated or not, rather than only the current owner's — the same predicate
    /// <see cref="Battle.InstantBattleResolver"/> reads directly (via <see cref="Strength.SiegeStrength.Defender"/>)
    /// for its own siege resolution, and <see cref="Cities.Capture.ConquestTrigger"/> reads for its capital-move
    /// destination search. <see cref="Cities.Capture.CompleteDefenderStrength"/>'s one other caller,
    /// <see cref="Cities.Capture.CityCaptureResolver"/>, already excludes capitals before calling it and passes
    /// a hardcoded <see langword="false"/> rather than re-deriving the predicate there. Before this fix a
    /// city that another nation's stale capital still named (T90/#409's own scenario: a defected or
    /// eliminated nation's capital never moved) scored its defender strength without the ×5/3 capital
    /// multiplier the resolver itself applies, so this estimate could disagree with what
    /// <see cref="Battle.InstantBattleResolver"/> actually computes for the same siege.
    /// </summary>
    private static bool IsControllerCapital(GameState state, CityState city) =>
        Cities.Capture.CapitalOwnership.IsAnyNationsCapital(state, city.Id);

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
