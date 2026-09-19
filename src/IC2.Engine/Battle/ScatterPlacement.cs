using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Battle;

/// <summary>
/// Where a beaten army or fleet goes under the <c>improved</c> ruleset's
/// <see cref="DefeatOutcome.Scatter"/> outcome — <c>docs/game-design.md</c> §"The defeated side's fate",
/// DoD 10 and DoD 11.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is <c>[designed, no original analogue]</c>.</strong> The original has no partial-defeat
/// outcome at either level of detail — all three decompiled resolvers annihilate the loser
/// unconditionally — so there is nothing in the decompilation to transcribe here, and
/// <c>design-audit.md</c> Q10 answers explicitly that the placeholder stays and is <em>not</em> to be
/// re-grounded on the rout mechanic's per-type thresholds. The two numbers this file consumes,
/// <see cref="ScatteredDefeatRules.ScatterTilesMin"/> and <see cref="ScatteredDefeatRules.ScatterTilesMax"/>,
/// are ruleset data carrying a <c>designed</c> provenance, meant to be retuned from play.
/// </para>
/// <para>
/// <strong>The rule, as specified.</strong> Relocate the survivor <c>scatterTiles</c> tiles from where it
/// stood, in a direction away from the victor, onto the nearest valid tile of the right kind — passable
/// land for an army, sea for a fleet — that no other army, fleet or city occupies. If the whole ring at
/// that distance is unusable, shrink the distance one tile at a time down to 1. If even an adjacent tile
/// is unavailable, there is nowhere to route it and the caller falls back to the
/// <see cref="DefeatOutcome.Destroyed"/> outcome (DoD 11).
/// </para>
/// <para>
/// <strong>Determinism.</strong> The only random input is the distance, drawn by the caller. Everything
/// after that is a fixed total order over the ring: nearest to the ideal tile first (the ideal tile being
/// the one exactly <c>distance</c> steps directly away from the victor), then ascending <c>y</c>, then
/// ascending <c>x</c>. No dictionary, no hash order, no iteration over unordered state.
/// </para>
/// </remarks>
public static class ScatterPlacement
{
    /// <summary>
    /// Finds the tile a survivor scatters to, or <see langword="null"/> when it is fully boxed in.
    /// </summary>
    /// <param name="origin">Where the survivor stands — its own tile, which the battle was fought over or beside.</param>
    /// <param name="awayFrom">The victor's tile. The scatter direction points away from it.</param>
    /// <param name="requestedDistance">The distance drawn from the ruleset's scatter range.</param>
    /// <param name="forFleet">
    /// <see langword="true"/> to look for a sea tile a fleet may enter, <see langword="false"/> for land
    /// an army may stand on.
    /// </param>
    /// <param name="movingId">
    /// The scattering army's or fleet's own id, so its current tile is not treated as occupied by
    /// something else. Its carried army, if any, moves with it and is likewise not an obstacle.
    /// </param>
    /// <param name="state">The live state, for the occupying armies, fleets and cities.</param>
    /// <param name="world">The map, for bounds and terrain.</param>
    /// <param name="destinationTileCode">
    /// The terrain code of the returned destination — <see cref="Model.ArmyState.CoveredTileCode"/> and
    /// <see cref="Model.FleetState.CoveredTileCode"/>, "the map cell this entity's marker covers". Every
    /// other mover in the engine recomputes this at its destination (<c>MoveArmyCommandHandler</c>,
    /// <c>MoveFleetCommandHandler</c>, <c>DisembarkArmyCommandHandler</c>), and a scatter is a move. For a
    /// fleet it is not cosmetic: <c>FleetTickSystem</c> decides each turn's storm-tripling branch by
    /// comparing this code with <see cref="NavalRules.StormTripleConditionTileCode"/>, so a survivor that
    /// kept its pre-battle code would carry the old tile's weather with it for the rest of the game.
    /// Meaningless (<c>0</c>) when this method returns <see langword="null"/>. Returned rather than
    /// decoded a second time by the caller (T52 DoD 11): this method already decodes the terrain grid once
    /// to search the ring, and a second, separate decode of the whole map per scattered survivor is not
    /// free on the 334-city map with battles resolving constantly.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requestedDistance"/> is not positive.</exception>
    public static ScatterOutcome? Find(
        GridPoint origin,
        GridPoint awayFrom,
        int requestedDistance,
        bool forFleet,
        string movingId,
        GameState state,
        World world,
        out int destinationTileCode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(movingId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedDistance);

        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var stepX = Math.Sign(origin.X - awayFrom.X);
        var stepY = Math.Sign(origin.Y - awayFrom.Y);

        for (var distance = requestedDistance; distance >= 1; distance--)
        {
            var ideal = new GridPoint(origin.X + (stepX * distance), origin.Y + (stepY * distance));
            var best = BestOnRing(origin, ideal, distance, forFleet, movingId, state, world, terrain, out var tileCode);
            if (best is { } chosen)
            {
                destinationTileCode = tileCode;
                return new ScatterOutcome(origin.X, origin.Y, chosen.X, chosen.Y, requestedDistance, distance);
            }
        }

        destinationTileCode = 0;
        return null;
    }

    private static GridPoint? BestOnRing(
        GridPoint origin,
        GridPoint ideal,
        int distance,
        bool forFleet,
        string movingId,
        GameState state,
        World world,
        int[] terrain,
        out int bestTileCode)
    {
        GridPoint? best = null;
        var bestRank = 0;
        bestTileCode = 0;

        for (var y = origin.Y - distance; y <= origin.Y + distance; y++)
        {
            for (var x = origin.X - distance; x <= origin.X + distance; x++)
            {
                // The ring at exactly this Chebyshev distance, not the filled square.
                if (Math.Max(Math.Abs(x - origin.X), Math.Abs(y - origin.Y)) != distance)
                {
                    continue;
                }

                var candidate = new GridPoint(x, y);
                if (!IsValid(candidate, forFleet, movingId, state, world, terrain))
                {
                    continue;
                }

                var rank = Math.Max(Math.Abs(x - ideal.X), Math.Abs(y - ideal.Y));

                // Ascending y then ascending x is the scan order itself, so a strict improvement is the
                // only reason to replace the incumbent -- ties keep the earlier, lower-(y, x) tile.
                if (best is null || rank < bestRank)
                {
                    best = candidate;
                    bestRank = rank;
                    bestTileCode = terrain[(y * world.Width) + x];
                }
            }
        }

        return best;
    }

    private static bool IsValid(
        GridPoint point,
        bool forFleet,
        string movingId,
        GameState state,
        World world,
        int[] terrain)
    {
        if ((uint)point.X >= (uint)world.Width || (uint)point.Y >= (uint)world.Height)
        {
            return false;
        }

        var tile = world.TileTypeByCode(terrain[(point.Y * world.Width) + point.X]);
        if (tile is null)
        {
            return false;
        }

        if (forFleet ? !tile.PassableByFleets : !tile.PassableByArmies)
        {
            return false;
        }

        foreach (var city in state.Cities)
        {
            if (city.X == point.X && city.Y == point.Y)
            {
                return false;
            }
        }

        foreach (var army in state.Armies)
        {
            // An embarked army is off the map and occupies nothing.
            if (army.IsEmbarked || string.Equals(army.Id, movingId, StringComparison.Ordinal))
            {
                continue;
            }

            if (army.X == point.X && army.Y == point.Y)
            {
                return false;
            }
        }

        foreach (var fleet in state.Fleets)
        {
            // A fleet still counting down its construction is not on the map yet.
            if (fleet.IsUnderConstruction || string.Equals(fleet.Id, movingId, StringComparison.Ordinal))
            {
                continue;
            }

            if (fleet.X == point.X && fleet.Y == point.Y)
            {
                return false;
            }
        }

        return true;
    }
}
