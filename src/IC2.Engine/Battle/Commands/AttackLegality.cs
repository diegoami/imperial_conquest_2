using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// Whether an attack, a siege or a naval attack is legal — decided here, once, and callable
/// <em>without issuing the command</em>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is a public type and not three private methods.</strong> T22's Done-when 1 requires a
/// soak run in which the AI issues <em>zero rejected commands</em>, and an AI can only promise that if it
/// can ask whether an order is legal before placing it. Every gate the three handlers apply lives here
/// and nowhere else, so the answer a caller gets in advance is the answer the dispatcher will give — the
/// handlers call these very methods. A second copy of the rules inside the handlers is exactly how the
/// probe and the gate drift apart, so there is not one.
/// </para>
/// <para>
/// <strong>What this does not cover</strong>, because <see cref="CommandDispatcher"/> decides it before
/// any handler runs: the issuing nation existing, not being eliminated, and being the active seat
/// (<see cref="CoreRejections"/>). A caller wanting a complete "will this be accepted" answer checks
/// <see cref="GameState.ActiveNationId"/> and <see cref="NationState.Eliminated"/> alongside this.
/// </para>
/// <para>
/// <strong>Adjacency, and where it comes from.</strong> An order is issued by selecting your own unit and
/// then clicking the target: <c>TUnitMap_SelectUnit</c> is "where a click on the map becomes an order"
/// and turns a click on an enemy city, army or fleet into an attack
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md,
/// decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>. The attacker never stands on the
/// target's tile: city markers are map codes <c>20..99</c>, army markers <c>200..247</c> and fleet markers
/// <c>300..347</c>, and the movement walk steps only codes <c>2..11</c> — "codes ≥ 12 are not steppable at
/// all by this walk… interactions with those are handled at the destination"
/// <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>. The user confirmed the same rule from
/// play on 2026-09-19: <em>"first you select the army, then the town, and if they are adjacent there is a
/// siege action. Same pattern for an army attacking an army, or a fleet attacking a fleet."</em>
/// <strong>[confirmed: attack-and-siege-are-adjacency-orders.md — direct user observation of the original
/// game, 2026-09-19]</strong>. So adjacency is a gate on a target, not a destination to move to.
/// </para>
/// <para>
/// <strong>That "adjacent" means <em>Chebyshev</em> ≤ 1 is <c>[derived]</c>, and the tag is the point.</strong>
/// Three sub-claims above are each <c>[confirmed]</c>: the order is issued against an adjacent target, a
/// marker tile is never stepped, and the original's own city-threat test walks "each of the 9 cells
/// centred on the city" — an eight-neighbourhood — <strong>[confirmed:
/// city-population-growth.md]</strong>. The <em>metric</em> is not.
/// <c>attack-and-siege-are-adjacency-orders.md</c> says so itself under "What this does not establish":
/// whether the game treats adjacency as 8-way (Chebyshev) or 4-way (Manhattan) was not observed, so
/// reading that 9-cell block as this gate's metric is an inference. It is the better-supported one — that
/// block is the only adjacency the decompilation states, it is eight-way, and it agrees with the literal
/// one-tile bound the merged supply and disembark gates already use — but it is an inference, and an
/// untagged inference reads as a confirmed fact.
/// <c>[derived: the 8-way reading of "adjacent", from FUN_004497cc's 9-cell block; no observation rules
/// out a 4-way rule]</c>.
/// </para>
/// <para>
/// <strong>War, and why this gate refuses rather than declares.</strong> In the original the two are one
/// click: <c>TUnitMap_SelectUnit</c> "calls <c>FUN_00449B40(you, them, 3)</c> <em>before</em> resolving
/// the attack. Attacking <strong>is</strong> declaring war"
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>. This engine
/// spells that sequence as two commands in that same order, which is what T19's merged
/// <c>DeclareWarCommand</c> already documents as the intended path: <em>"This is also the
/// auto-declaration path DoD 5 exercises: attacking sets the relation to war before a battle resolves, by
/// dispatching this command first"</em>. The ordering the original guarantees is therefore preserved
/// exactly — the relation is war before anything resolves — and this gate is what enforces it, by
/// refusing an attack that would resolve <em>without</em> it. See
/// <see cref="AttackArmyCommandHandler"/>'s remarks for the structural reason the declaration cannot live
/// inside this handler.
/// </para>
/// </remarks>
public static class AttackLegality
{
    /// <summary>
    /// Whether two map positions are adjacent — Chebyshev distance ≤ 1, the eight-neighbourhood.
    /// Co-location counts. The metric is <c>[derived]</c>; see this class's remarks.
    /// </summary>
    /// <param name="ax">First x.</param>
    /// <param name="ay">First y.</param>
    /// <param name="bx">Second x.</param>
    /// <param name="by">Second y.</param>
    /// <returns><see langword="true"/> when the two tiles adjoin or coincide.</returns>
    public static bool AreAdjacent(int ax, int ay, int bx, int by) =>
        Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) <= 1;

    /// <summary>Whether <paramref name="command"/> would be accepted by its handler.</summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="command">The attack being considered.</param>
    /// <returns><see langword="true"/> when <see cref="Check(GameState, Ruleset, AttackArmyCommand)"/> finds nothing wrong.</returns>
    public static bool IsLegal(GameState state, Ruleset ruleset, AttackArmyCommand command) =>
        Check(state, ruleset, command) is null;

    /// <summary>Whether <paramref name="command"/> would be accepted by its handler.</summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="command">The siege being considered.</param>
    /// <returns><see langword="true"/> when <see cref="Check(GameState, Ruleset, BesiegeCityCommand)"/> finds nothing wrong.</returns>
    public static bool IsLegal(GameState state, Ruleset ruleset, BesiegeCityCommand command) =>
        Check(state, ruleset, command) is null;

    /// <summary>Whether <paramref name="command"/> would be accepted by its handler.</summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="command">The naval attack being considered.</param>
    /// <returns><see langword="true"/> when <see cref="Check(GameState, Ruleset, AttackFleetCommand)"/> finds nothing wrong.</returns>
    public static bool IsLegal(GameState state, Ruleset ruleset, AttackFleetCommand command) =>
        Check(state, ruleset, command) is null;

    /// <summary>
    /// The reason <paramref name="command"/> would be refused, or <see langword="null"/> if it would be
    /// accepted (Done-when 1's four gates, plus the three refusals that exist because
    /// <see cref="InstantBattleResolver.ResolveField"/> throws on them).
    /// </summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset, for <c>diplomacy.stateCodes.war</c>.</param>
    /// <param name="command">The attack being considered.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public static CommandRejection? Check(GameState state, Ruleset ruleset, AttackArmyCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(command);

        var attacker = state.ArmyById(command.AttackerArmyId);
        if (attacker is null)
        {
            return Refuse(AttackArmyRejections.UnknownArmy, $"'{command.AttackerArmyId}' is not a known army.");
        }

        if (!string.Equals(attacker.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return Refuse(
                AttackArmyRejections.NotYourArmy,
                $"Army '{attacker.Id}' belongs to '{attacker.Nation}', not '{command.IssuingNationId}'.");
        }

        var target = state.ArmyById(command.TargetArmyId);
        if (target is null)
        {
            return Refuse(AttackArmyRejections.UnknownTarget, $"'{command.TargetArmyId}' is not a known army.");
        }

        if (string.Equals(attacker.Nation, target.Nation, StringComparison.Ordinal))
        {
            return Refuse(
                AttackArmyRejections.SameNation,
                $"'{attacker.Id}' and '{target.Id}' both belong to '{attacker.Nation}'.");
        }

        if (attacker.IsEmbarked)
        {
            return Refuse(
                AttackArmyRejections.AttackerEmbarked,
                $"Army '{attacker.Id}' is aboard fleet '{attacker.AboardFleetId}' and cannot fight a field battle.");
        }

        if (target.IsEmbarked)
        {
            return Refuse(
                AttackArmyRejections.TargetEmbarked,
                $"Army '{target.Id}' is aboard fleet '{target.AboardFleetId}'; attack the fleet carrying it instead.");
        }

        if (attacker.Moves <= 0)
        {
            return Refuse(
                AttackArmyRejections.NoMovesLeft, $"Army '{attacker.Id}' has no moves left this turn.");
        }

        if (!AreAdjacent(attacker.X, attacker.Y, target.X, target.Y))
        {
            return Refuse(
                AttackArmyRejections.NotAdjacent,
                $"Army '{attacker.Id}' at ({attacker.X}, {attacker.Y}) is not adjacent to '{target.Id}' "
                + $"at ({target.X}, {target.Y}).");
        }

        return WarCheck(state, ruleset, attacker.Nation, target.Nation, AttackArmyRejections.NotAtWar);
    }

    /// <summary>
    /// The reason <paramref name="command"/> would be refused, or <see langword="null"/> if it would be
    /// accepted (Done-when 2's gates, on the same discipline as Done-when 1's).
    /// </summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset, for the war code and the two resolver ids.</param>
    /// <param name="command">The siege being considered.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public static CommandRejection? Check(GameState state, Ruleset ruleset, BesiegeCityCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(command);

        var attacker = state.ArmyById(command.AttackerArmyId);
        if (attacker is null)
        {
            return Refuse(BesiegeCityRejections.UnknownArmy, $"'{command.AttackerArmyId}' is not a known army.");
        }

        if (!string.Equals(attacker.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return Refuse(
                BesiegeCityRejections.NotYourArmy,
                $"Army '{attacker.Id}' belongs to '{attacker.Nation}', not '{command.IssuingNationId}'.");
        }

        var city = state.CityById(command.TargetCityId);
        if (city is null)
        {
            return Refuse(BesiegeCityRejections.UnknownCity, $"'{command.TargetCityId}' is not a known city.");
        }

        if (string.Equals(city.Owner, attacker.Nation, StringComparison.Ordinal))
        {
            return Refuse(
                BesiegeCityRejections.OwnCity, $"City '{city.Id}' already belongs to '{attacker.Nation}'.");
        }

        if (attacker.IsEmbarked)
        {
            return Refuse(
                BesiegeCityRejections.AttackerEmbarked,
                $"Army '{attacker.Id}' is aboard fleet '{attacker.AboardFleetId}' and cannot besiege '{city.Id}'.");
        }

        if (attacker.Moves <= 0)
        {
            return Refuse(
                BesiegeCityRejections.NoMovesLeft, $"Army '{attacker.Id}' has no moves left this turn.");
        }

        if (!AreAdjacent(attacker.X, attacker.Y, city.X, city.Y))
        {
            return Refuse(
                BesiegeCityRejections.NotAdjacent,
                $"Army '{attacker.Id}' at ({attacker.X}, {attacker.Y}) is not adjacent to '{city.Id}' "
                + $"at ({city.X}, {city.Y}).");
        }

        // Defensive: unreachable while every city's Owner names a real nation, which the loaded data
        // always satisfies today -- the same branch, and the same reasoning, as
        // BuySupplyCommandHandler's. Ordered ahead of the war check so a broken state is reported as
        // itself rather than as "not at war".
        if (state.NationById(city.Owner) is null)
        {
            return Refuse(
                BesiegeCityRejections.UnresolvableCityOwner,
                $"City '{city.Id}''s owner '{city.Owner}' is not a known nation.");
        }

        if (WarCheck(state, ruleset, attacker.Nation, city.Owner, BesiegeCityRejections.NotAtWar) is { } notAtWar)
        {
            return notAtWar;
        }

        return ResolverIdsCheck(
            ruleset, BesiegeCityRejections.NoArcherUnitType, BesiegeCityRejections.NoFortificationOrder);
    }

    /// <summary>
    /// The reason <paramref name="command"/> would be refused, or <see langword="null"/> if it would be
    /// accepted (Done-when 7).
    /// </summary>
    /// <param name="state">The state the command would be decided against.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="command">The naval attack being considered.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public static CommandRejection? Check(GameState state, Ruleset ruleset, AttackFleetCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(command);

        var attacker = state.FleetById(command.AttackerFleetId);
        if (attacker is null)
        {
            return Refuse(AttackFleetRejections.UnknownFleet, $"'{command.AttackerFleetId}' is not a known fleet.");
        }

        if (!string.Equals(attacker.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return Refuse(
                AttackFleetRejections.NotYourFleet,
                $"Fleet '{attacker.Id}' belongs to '{attacker.Nation}', not '{command.IssuingNationId}'.");
        }

        var target = state.FleetById(command.TargetFleetId);
        if (target is null)
        {
            return Refuse(AttackFleetRejections.UnknownTarget, $"'{command.TargetFleetId}' is not a known fleet.");
        }

        if (string.Equals(attacker.Nation, target.Nation, StringComparison.Ordinal))
        {
            return Refuse(
                AttackFleetRejections.SameNation,
                $"'{attacker.Id}' and '{target.Id}' both belong to '{attacker.Nation}'.");
        }

        if (attacker.IsUnderConstruction || target.IsUnderConstruction)
        {
            return Refuse(
                AttackFleetRejections.UnderConstruction,
                $"A fleet still under construction is not on the map: '{attacker.Id}', '{target.Id}'.");
        }

        if (attacker.Moves <= 0)
        {
            return Refuse(
                AttackFleetRejections.NoMovesLeft, $"Fleet '{attacker.Id}' has no moves left this turn.");
        }

        if (!AreAdjacent(attacker.X, attacker.Y, target.X, target.Y))
        {
            return Refuse(
                AttackFleetRejections.NotAdjacent,
                $"Fleet '{attacker.Id}' at ({attacker.X}, {attacker.Y}) is not adjacent to '{target.Id}' "
                + $"at ({target.X}, {target.Y}).");
        }

        if (DockedAtOwnCity(state, target) is { } harbour)
        {
            return Refuse(
                AttackFleetRejections.TargetDockedAtItsOwnCity,
                $"You cannot attack fleet '{target.Id}' docked at its own city '{harbour.Id}'.");
        }

        if (WarCheck(state, ruleset, attacker.Nation, target.Nation, AttackFleetRejections.NotAtWar) is { } notAtWar)
        {
            return notAtWar;
        }

        return BattleCommandRuleset.ArcherUnitTypeIdIn(ruleset) is null
            ? Refuse(
                AttackFleetRejections.NoArcherUnitType,
                $"Ruleset '{ruleset.Id}' declares no '{BattleCommandRuleset.ArcherUnitTypeId}' unit type, which a "
                + "carried army's strength is measured with.")
            : null;
    }

    /// <summary>
    /// The one place the war relation is read, so the three commands cannot disagree about it.
    /// </summary>
    /// <remarks>
    /// <see cref="DiplomaticRelations.Get"/> throws for a nation outside the matrix, which a handler must
    /// never do, so the pair is resolved through <see cref="DiplomaticRelations.IndexOf"/> first. A nation
    /// the matrix does not carry cannot be at war, so it refuses — the same answer, without the throw.
    /// </remarks>
    private static CommandRejection? WarCheck(
        GameState state, Ruleset ruleset, string attackerNationId, string defenderNationId, RejectionCode code)
    {
        var relations = state.Relations;
        if (relations.IndexOf(attackerNationId) < 0 || relations.IndexOf(defenderNationId) < 0)
        {
            return Refuse(
                code,
                $"'{attackerNationId}' and '{defenderNationId}' are not both in the relation matrix, so they "
                + "cannot be at war.");
        }

        if (relations.Get(attackerNationId, defenderNationId) != ruleset.Diplomacy.StateCodes.War)
        {
            return Refuse(
                code,
                $"'{attackerNationId}' is not at war with '{defenderNationId}'. Declare war first: attacking is "
                + "declaring war, and the declaration is the command that comes before this one.");
        }

        return null;
    }

    /// <summary>
    /// The two ids the siege path must hand the merged resolvers, refused as a typed rejection rather
    /// than thrown — see <see cref="BattleCommandRuleset"/> for why they are not <see cref="Ruleset"/>
    /// fields.
    /// </summary>
    private static CommandRejection? ResolverIdsCheck(
        Ruleset ruleset, RejectionCode noArcher, RejectionCode noFortificationOrder)
    {
        if (BattleCommandRuleset.ArcherUnitTypeIdIn(ruleset) is null)
        {
            return Refuse(
                noArcher,
                $"Ruleset '{ruleset.Id}' declares no '{BattleCommandRuleset.ArcherUnitTypeId}' unit type, which the "
                + "besieger's strength is measured with.");
        }

        if (BattleCommandRuleset.FortificationOrderIdIn(ruleset) is null)
        {
            return Refuse(
                noFortificationOrder,
                $"Ruleset '{ruleset.Id}' declares no city order a siege attempt wipes, so a city's fortification "
                + "word cannot be decoded.");
        }

        return null;
    }

    /// <summary>
    /// The city a launched fleet is moored at, when that city belongs to the fleet's own nation —
    /// <em>"You cannot attack a fleet docked at its own city !"</em>
    /// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>.
    /// </summary>
    /// <remarks>
    /// The <em>rule</em> is confirmed; its <em>radius</em> is <c>[derived]</c>. No report decompiles the
    /// check, and a fleet cannot occupy a city's own tile any more than an army can (the naval walk steps
    /// only cell codes <c>0</c> and <c>1</c>, <strong>[confirmed:
    /// terrain-move-cost-table-in-dat.md]</strong>), so "docked at" has to mean "within one tile of" — the
    /// same reading, and the same literal bound, as the merged supply gates that come from
    /// <c>TAFSupply_FindProviders</c>'s confirmed one-tile provider radius.
    /// </remarks>
    private static CityState? DockedAtOwnCity(GameState state, FleetState fleet)
    {
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal)
                && AreAdjacent(fleet.X, fleet.Y, city.X, city.Y))
            {
                return city;
            }
        }

        return null;
    }

    private static CommandRejection Refuse(RejectionCode code, string message) => new(code, message);
}
