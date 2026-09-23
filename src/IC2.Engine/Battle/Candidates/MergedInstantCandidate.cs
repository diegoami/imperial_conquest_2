using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// C1 (§6.1), the baseline: <see cref="InstantBattleResolver.ResolveField"/> wrapped UNCHANGED (T59
/// Done-when 1). The wrapper only builds a two-army <see cref="GameState"/>, calls the merged resolver with
/// the battle's own <see cref="IRng"/>, and reads the result back. Nothing in the resolver is copied or
/// re-implemented here, so a C1 number is the merged code's number — including T63's small-unit deletion
/// pass (#289's fix, merged at 562e608).
/// </summary>
/// <remarks>
/// <para><strong>Call sites covered.</strong> C1 is <c>ResolveField</c>, the field-battle resolver, whose one
/// game call site is <c>AttackArmyCommandHandler</c>. <c>ResolveSiege</c> and <c>ResolveNaval</c> are not
/// candidates and are not wrapped (§1: no metric scores a siege or a naval battle).</para>
/// <para><strong>Draws.</strong> The merged count, not the original's: <c>nW</c> casualty divisors (one per
/// winner unit-list entry, where the original's <c>FUN_0044AE20</c> always draws 20), then one
/// <c>Random(4)</c> per surviving winner unit, then the peace roll; under <c>scatter</c>, <c>nL</c> divisors
/// and one scatter-distance draw when the loser has survivors (§6.1 "Draws"; #290).</para>
/// <para><strong>The battlefield.</strong> The resolver needs a map only to place a scattered survivor. The
/// wrapper stands the two armies on two adjacent land tiles given by the harness, with no cities, fleets or
/// other armies on the map, so a placement always exists for a loser with survivors.</para>
/// </remarks>
public sealed class MergedInstantCandidate : IAutoResolveCandidate
{
    private const string AttackerId = "candidate-attacker";
    private const string DefenderId = "candidate-defender";

    private readonly GameState _baseState;
    private readonly World _world;
    private readonly (int X, int Y, int Tile) _attackerAt;
    private readonly (int X, int Y, int Tile) _defenderAt;
    private readonly string _attackerNation;
    private readonly string _defenderNation;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="baseState">Any state carrying at least two nations; only its nation records are used.</param>
    /// <param name="world">The map the two armies stand on.</param>
    /// <param name="attackerX">The attacker's column.</param>
    /// <param name="attackerY">The attacker's row.</param>
    /// <param name="defenderX">The defender's column.</param>
    /// <param name="defenderY">The defender's row.</param>
    public MergedInstantCandidate(GameState baseState, World world, int attackerX, int attackerY, int defenderX, int defenderY)
    {
        ArgumentNullException.ThrowIfNull(baseState);
        ArgumentNullException.ThrowIfNull(world);
        if (baseState.Nations.Count < 2)
        {
            throw new ArgumentException("A field battle needs two nations.", nameof(baseState));
        }

        _world = world;
        _attackerNation = baseState.Nations[0].Id;
        _defenderNation = baseState.Nations[1].Id;
        _baseState = baseState with
        {
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList<CityState>.Empty,
        };

        var terrain = world.Terrain.Decode(world.Width, world.Height);
        _attackerAt = (attackerX, attackerY, LandTile(world, terrain, attackerX, attackerY));
        _defenderAt = (defenderX, defenderY, LandTile(world, terrain, defenderX, defenderY));
    }

    /// <inheritdoc />
    public string Key => "C1";

    /// <inheritdoc />
    public string Name => "the merged instant resolver (baseline)";

    /// <inheritdoc />
    public CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rng);

        var ruleset = battle.Ruleset.Flags.CombatOnDefeat == battle.OnDefeat
            ? battle.Ruleset
            : battle.Ruleset with { Flags = battle.Ruleset.Flags with { CombatOnDefeat = battle.OnDefeat } };

        var attacker = Army(AttackerId, _attackerNation, _attackerAt, battle.Attacker);
        var defender = Army(DefenderId, _defenderNation, _defenderAt, battle.Defender);
        var state = _baseState with { Armies = ValueList.Of(attacker, defender) };

        var counting = new DrawCountingRng(rng);
        var resolution = InstantBattleResolver.ResolveField(
            state, AttackerId, DefenderId, ruleset, _world, counting, NullEventSink.Instance);
        var result = resolution.Result;

        var attackerWon = result.AttackerWon;
        var winnerInput = attackerWon ? battle.Attacker : battle.Defender;
        var loserInput = attackerWon ? battle.Defender : battle.Attacker;
        var winnerAfter = ReadBack(resolution.State.ArmyById(attackerWon ? AttackerId : DefenderId), winnerInput.Units.Count);

        var scattered = result.LoserFate == LoserFate.Scattered;
        var loserSurvivors = battle.OnDefeat == DefeatOutcome.Scatter && scattered
            ? ReadBack(resolution.State.ArmyById(attackerWon ? DefenderId : AttackerId), loserInput.Units.Count)
            : new int[loserInput.Units.Count];

        // §6.1 "Draws", the merged order.
        var nW = winnerInput.Units.Count;
        var nL = loserInput.Units.Count;
        var promotionRolls = 0;
        foreach (var troops in winnerAfter)
        {
            if (troops > 0)
            {
                promotionRolls++;
            }
        }

        var scatter = battle.OnDefeat == DefeatOutcome.Scatter;
        long battleDraws = nW + (scatter ? nL : 0);
        long stated = nW + promotionRolls + 1 + (scatter ? nL + (scattered ? 1 : 0) : 0);

        return new CandidateOutcome(
            attackerWon ? BattleSide.Attacker : BattleSide.Defender,
            winnerAfter,
            loserSurvivors,
            CandidateEnding.Decided,
            battleDraws,
            counting.Draws,
            stated,
            CascadeBreak: false,
            Rounds: 0,
            Array.Empty<CandidateEvent>());
    }

    private static int LandTile(World world, int[] terrain, int x, int y)
    {
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is off the map.");
        }

        var code = terrain[(y * world.Width) + x];
        if (world.TileTypeByCode(code) is not { PassableByArmies: true })
        {
            throw new ArgumentException($"({x},{y}) is not a land tile an army may stand on.", nameof(x));
        }

        return code;
    }

    private static ArmyState Army(string id, string nation, (int X, int Y, int Tile) at, CandidateArmy army)
    {
        var units = new UnitSlot[army.Units.Count];
        for (var i = 0; i < units.Length; i++)
        {
            var unit = army.Units[i];
            units[i] = new UnitSlot(unit.MercenaryLabel, unit.UnitTypeId, unit.Troops, unit.Quality, SlotName(i));
        }

        return new ArmyState(
            id,
            nation,
            at.X,
            at.Y,
            Moves: 5,
            army.Morale,
            Money: 0,
            SupplyTons: 0,
            at.Tile,
            AboardFleetId: null,
            ValueList.From(units));
    }

    private static string SlotName(int slot) => "slot-" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int[] ReadBack(ArmyState? army, int slots)
    {
        // Units are matched by the name the wrapper gave each input slot: the merged deletion pass removes
        // units swap-with-last, so list positions do not survive the battle. A unit absent afterwards is 0.
        var troops = new int[slots];
        if (army is null)
        {
            return troops;
        }

        foreach (var unit in army.Units)
        {
            for (var i = 0; i < slots; i++)
            {
                if (string.Equals(unit.Name, SlotName(i), StringComparison.Ordinal))
                {
                    troops[i] = unit.Troops;
                    break;
                }
            }
        }

        return troops;
    }
}
