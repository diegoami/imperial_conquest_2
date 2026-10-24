using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Ai;

/// <summary>
/// The original classical world's static geographic and waypoint rules for army target routing.
/// The interpreted tables are from the bounded navigation study promoted in Research PR72.
/// This is a target-tree planner, not a replacement for the movement walker or an arbitrary-world pathfinder.
/// </summary>
public static partial class ClassicalArmyNavigation
{
    private readonly record struct RegionBox(int MinX, int MaxX, int MinY, int MaxY);

    public readonly record struct Geography(int Region, int Side);
    public readonly record struct WaypointChoice(GridPoint? Point, string Kind, int? NodeId = null);

    public static bool Applies(AiView view) =>
        view.World.Id == "classical-mediterranean"
        && view.World.Width == 320 && view.World.Height == 140
        && view.Ruleset.Flags.AiEconomy == AiEconomyModel.Original;

    /// <summary>CA30: last strict-interior region and the coordinate-side split bit.</summary>
    public static Geography Classify(int x, int y)
    {
        var region = -1;
        for (var i = 0; i < Regions.Length; i++)
        {
            var box = Regions[i];
            if (box.MinX < x && x < box.MaxX && box.MinY < y && y < box.MaxY)
                region = i;
        }

        var side = 1;
        foreach (var split in CoordinateSplits)
            if (x < split.X && y < split.Y) side = 0;
        return new Geography(region, side);
    }

    /// <summary>CAB4: same geographic pair, or any fleet owned by the actor, even an unusable one.</summary>
    public static bool TargetEligible(AiView view, ArmyState actor, int targetX, int targetY) =>
        Classify(actor.X, actor.Y) == Classify(targetX, targetY)
        || view.State.Fleets.Any(f => f.Nation == actor.Nation);

    /// <summary>D9A8's narrower ferry lookup, retained as a pure boundary control until route state exists.</summary>
    public static FleetState? NearestUsableFerry(AiView view, ArmyState actor)
    {
        FleetState? closest = null;
        var bestDistance = int.MaxValue;
        foreach (var fleet in view.State.Fleets)
        {
            if (fleet.Nation != actor.Nation || fleet.IsUnderConstruction
                || fleet.IsCarryingArmy || fleet.Moves <= 0) continue;
            var distance = Distance(new GridPoint(actor.X, actor.Y), new GridPoint(fleet.X, fleet.Y));
            if (distance >= bestDistance) continue; // First record wins a tie.
            closest = fleet;
            bestDistance = distance;
        }
        return closest;
    }

    /// <summary>CB40: same land/sea medium along the integer line, excluding the final sample.</summary>
    public static bool DirectLine(AiView view, GridPoint origin, GridPoint target, bool coincidentAfterOriginTest = false)
    {
        if (origin == target)
        {
            if (coincidentAfterOriginTest) return true;
            throw new NotSupportedException("An isolated coincident CB40 call reads an uninitialized original word.");
        }

        var originalSea = IsSea(view, origin.X, origin.Y);
        var dx = Math.Abs(target.X - origin.X);
        var dy = Math.Abs(target.Y - origin.Y);
        var sx = Math.Sign(target.X - origin.X);
        var sy = Math.Sign(target.Y - origin.Y);
        var xMajor = dx >= dy;
        var major = Math.Max(dx, dy);
        var minor = Math.Min(dx, dy);
        var error = 2 * minor - major;
        var x = origin.X;
        var y = origin.Y;
        for (var step = 0; step < major; step++)
        {
            while (error >= 0)
            {
                if (xMajor) y += sy;
                else x += sx;
                error -= 2 * major;
            }
            if (xMajor) x += sx;
            else y += sy;
            error += 2 * minor;
            if (step < major - 1 && IsSea(view, x, y) != originalSea) return false;
        }
        return true;
    }

    /// <summary>D174: choose a visible point, route-row entry, or source-shaped local fallback.</summary>
    public static WaypointChoice Intermediate(AiView view, GridPoint origin, GridPoint target) =>
        Intermediate(view, RouteGroup(origin), origin, target, 0);

    private static WaypointChoice Intermediate(AiView view, int group, GridPoint origin, GridPoint target, int depth)
    {
        if (depth > Points.Length)
            throw new NotSupportedException("Original waypoint recursion did not converge on this static world.");

        var (first, last) = GroupBounds(group);
        var visible = new List<int>();
        int? closestTarget = null;
        int? closestCommon = null;
        var targetDistance = 1000;
        var commonDistance = 1000;
        for (var id = first; id <= last; id++)
        {
            var point = Points[id];
            var distance = Distance(origin, point);
            var sourceVisible = distance > 0 && DirectLine(view, origin, point);
            if (sourceVisible) visible.Add(id);
            var targetVisible = DirectLine(view, target, point, coincidentAfterOriginTest: true);
            if (targetVisible && distance is > 0 and < 1000 && distance < targetDistance)
            {
                closestTarget = id;
                targetDistance = distance;
            }
            if (targetVisible && sourceVisible && distance is > 0 and < 1000 && distance < commonDistance)
            {
                closestCommon = id;
                commonDistance = distance;
            }
        }

        if (closestCommon is { } common)
            return new(Points[common], "common-visible", common);
        if (visible.Count > 0 && closestTarget is { } targetId)
        {
            if (group == 3)
                throw new NotSupportedException("The original island group has no route matrix or hub.");
            int? primary = null;
            int? secondary = null;
            var primaryCost = 1000;
            var secondaryCost = 1000;
            foreach (var entry in visible)
            {
                foreach (var row in Routes[group])
                {
                    var entryAt = LastIndex(row, entry);
                    if (entryAt < 0) continue;
                    var targetAt = LastIndex(row, targetId);
                    if (targetAt >= 0 && Math.Abs(entryAt - targetAt) < primaryCost)
                    {
                        primary = entry;
                        primaryCost = Math.Abs(entryAt - targetAt);
                    }
                    var hubAt = LastIndex(row, Hub(group));
                    if (hubAt >= 0 && Math.Abs(entryAt - hubAt) < secondaryCost)
                    {
                        secondary = entry;
                        secondaryCost = Math.Abs(entryAt - hubAt);
                    }
                }
            }
            if (primary is { } p) return new(Points[p], "route-row", p);
            if (secondary is { } h) return new(Points[h], "route-hub", h);
            throw new NotSupportedException("Original route-row fallback has no initialized output.");
        }
        if (visible.Count == 0) return new(LocalFallback(view, origin), "origin-local-fallback");
        var local = LocalFallback(view, target);
        if (local is { } next && !DirectLine(view, origin, next))
            return Intermediate(view, group, origin, next, depth + 1);
        return new(local, "target-local-fallback");
    }

    private static GridPoint? LocalFallback(AiView view, GridPoint origin)
    {
        GridPoint? chosen = null;
        var sea = IsSea(view, origin.X, origin.Y);
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var good = true;
                var last = origin;
                for (var n = 1; n <= 3; n++)
                {
                    last = new(Math.Clamp(origin.X + dx * n, 0, 319), Math.Clamp(origin.Y + dy * n, 0, 139));
                    if (IsSea(view, last.X, last.Y) != sea) good = false;
                }
                if (good) chosen = last; // The last successful direction wins.
            }
        return chosen;
    }

    private static bool IsSea(AiView view, int x, int y)
    {
        // Fleet markers cover the army/city/base map; embarked armies have no on-map marker.
        if (view.State.Fleets.Any(f => !f.IsUnderConstruction && f.X == x && f.Y == y)) return true;
        if (view.State.Armies.Any(a => a.CoveredTileCode is not null && a.X == x && a.Y == y)) return false;
        if (view.State.Cities.Any(c => c.X == x && c.Y == y)) return false;
        var code = view.TerrainCells[y * view.World.Width + x];
        return code < 2 || code >= 300;
    }

    private static int RouteGroup(GridPoint point)
    {
        var geo = Classify(point.X, point.Y);
        return geo.Region >= 0 ? 3 : geo.Side == 0 ? 2 : 1;
    }

    private static (int First, int Last) GroupBounds(int group) => group switch
    {
        0 => (1, 25), 1 => (26, 39), 2 => (40, 54), 3 => (55, 58),
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
    private static int Hub(int group) => group switch
    {
        0 => 9, 1 => 32, 2 => 43, _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
    private static int LastIndex(short[] row, int id)
    {
        var at = -1;
        for (var i = 0; i < row.Length; i++) if (row[i] == id) at = i;
        return at;
    }
    private static int Distance(GridPoint a, GridPoint b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
