using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies;

/// <summary>
/// The army a mobilized recruit gets when no existing one will take it — the original's
/// <c>FUN_00449f08</c> and its <c>FUN_004492c0</c> placement scan. <c>docs/task-catalogue.md</c>
/// "T55 Mobilization: a ready recruit becomes an army unit", Done-when 3.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §3]</strong>:
/// </para>
/// <code>
/// FUN_004492c0(cityXY, 1);                       // a free land cell next to the city
/// if (cellFound &amp;&amp; armyCount &lt; 0xc6) {     // hard cap: 198 armies
///   army.xy = cell;  army.owner = nation;
///   army.moves = 0;  if (nationIsComputer) army.moves = 1;
///   army.covered = map[x][y];                    // the cell the marker hides
///   army.supplies = 0;  army.money = 0;  army.morale = 0x3b;   // 59
/// }
/// </code>
/// <para>
/// <strong>Nothing here is re-derived.</strong> Morale 59, 0 moves for a human seat and 1 for an AI one
/// are already <see cref="ArmyManagementRules.NewArmyMorale"/>,
/// <see cref="ArmyManagementRules.NewArmyMovesHumanSeat"/> and
/// <see cref="ArmyManagementRules.NewArmyMovesAiSeat"/> — T15's split rule, merged and tested — and the
/// report confirms mobilization creates armies with the <em>same</em> values. This reads the same three
/// fields through the same <see cref="RulesetFlags.SeatAsymmetry"/> expression
/// <see cref="Commands.SplitArmyCommandHandler"/> uses, and
/// <c>MobilizationArmyCreationTests.A_mobilized_army_and_a_split_army_are_created_with_the_same_stats</c>
/// asserts the two paths agree by running both.
/// </para>
/// <para>
/// <strong>The placement scan takes the <em>last</em> cell, not the first.</strong>
/// <c>FUN_004492c0(xy, 1)</c> walks <c>dx, dy</c> over <c>{-1, 0, 1}</c> and keeps the last cell whose
/// map code is in <c>[2, 11]</c> <strong>[confirmed]</strong>, so the new army appears at the city's
/// south-east neighbour whenever that cell qualifies — which is exactly what the corpus pair shows:
/// Rome is <c>(101, 43)</c> and the new army 14 stands at <c>(102, 44)</c>. This is the opposite of
/// <see cref="LandingTile.FirstAdjacentLandTile"/>'s first-match convention, so that helper is
/// deliberately not reused; only its <see cref="LandingTile.IsPassableForArmy"/> predicate is, which is
/// this engine's form of "map code in <c>[2, 11]</c>" (<c>data/worlds/toy-3city.json</c>'s own tile
/// types cite the same range: "the army step guard accepts cell codes 2..11 only").
/// </para>
/// <para>
/// <strong>Why an occupied cell is excluded</strong> — <c>[confirmed]</c>, by the same report: the
/// original's map array holds markers, not terrain, wherever something stands on it, so a city cell
/// reads <c>20 + owner + 16 × variant &gt;= 20</c> and an army cell reads <c>owner + 200</c>; neither
/// can be in <c>[2, 11]</c>, which "is the mechanism behind 'an army never occupies a city tile'". This
/// engine keeps terrain and occupancy apart, so the two conditions are written out separately, using
/// the same occupancy set <see cref="Movement.Commands.MoveArmyCommandHandler"/> already blocks a step
/// into: a city, another army that is on the map, or a fleet.
/// </para>
/// </remarks>
public static class MobilizationArmyCreation
{
    /// <summary>
    /// Creates the army a recruit mobilized at <paramref name="city"/> lands in, or
    /// <see langword="null"/> when the original would also fail to make one — no qualifying cell in the
    /// city's 3×3 neighbourhood, or the army table already full.
    /// </summary>
    /// <param name="state">The live state, read for occupancy and the army count.</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="city">The training city the new army is placed beside.</param>
    /// <param name="nation">The owning nation; its seat picks the starting moves.</param>
    /// <param name="ruleset">Supplies <see cref="ArmyManagementRules"/> and the seat-asymmetry flag.</param>
    /// <param name="newArmyId">
    /// The id the new army takes. The engine invents no id — see
    /// <see cref="Recruitment.Commands.MobilizeRecruitSlotCommand.NewArmyId"/>.
    /// </param>
    public static ArmyState? Create(
        GameState state, World world, CityState city, NationState nation, Ruleset ruleset, string newArmyId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(newArmyId);

        var rules = ruleset.ArmyManagement;
        if (state.Armies.Count >= rules.MaxArmies)
        {
            return null;
        }

        if (PlacementCell(state, world, city) is not { } cell)
        {
            return null;
        }

        // The same expression SplitArmyCommandHandler uses, over the same two fields: classical-faithful
        // gives a human seat 0 moves and an AI seat 1, so a player's freshly mobilized army cannot act
        // in the week it appears; improved generalises the AI's value to every seat.
        var seatIsAi = nation.Control == SeatControl.Ai;
        var moves = ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful
            ? (seatIsAi ? rules.NewArmyMovesAiSeat : rules.NewArmyMovesHumanSeat)
            : rules.NewArmyMovesAiSeat;

        return new ArmyState(
            Id: newArmyId,
            Nation: nation.Id,
            X: cell.X,
            Y: cell.Y,
            Moves: moves,
            Morale: rules.NewArmyMorale,
            Money: 0,
            SupplyTons: 0,
            CoveredTileCode: TerrainCodeAt(world, cell),
            AboardFleetId: null,
            Units: ValueList<UnitSlot>.Empty);
    }

    /// <summary>
    /// The cell a new army is placed on: the <em>last</em> cell of the 3×3 block centred on
    /// <paramref name="city"/>, scanned row-major, that an army may stand on and nothing occupies.
    /// <see langword="null"/> when no cell qualifies.
    /// </summary>
    /// <param name="state">The live state, read for occupancy.</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="city">The city at the centre of the scan.</param>
    public static GridPoint? PlacementCell(GameState state, World world, CityState city)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(city);

        GridPoint? chosen = null;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidate = new GridPoint(city.X + dx, city.Y + dy);
                if (LandingTile.IsPassableForArmy(candidate, world) && !IsOccupied(state, candidate))
                {
                    // No break, and no early return: the last qualifying cell wins, which for a city
                    // with open ground all round is (+1, +1), its south-east neighbour.
                    chosen = candidate;
                }
            }
        }

        return chosen;
    }

    private static bool IsOccupied(GameState state, GridPoint point)
    {
        foreach (var city in state.Cities)
        {
            if (city.X == point.X && city.Y == point.Y)
            {
                return true;
            }
        }

        foreach (var army in state.Armies)
        {
            if (!army.IsEmbarked && army.X == point.X && army.Y == point.Y)
            {
                return true;
            }
        }

        foreach (var fleet in state.Fleets)
        {
            if (!fleet.IsUnderConstruction && fleet.X == point.X && fleet.Y == point.Y)
            {
                return true;
            }
        }

        return false;
    }

    private static int TerrainCodeAt(World world, GridPoint point)
    {
        var cells = world.Terrain.Decode(world.Width, world.Height);
        return cells[(point.Y * world.Width) + point.X];
    }
}
