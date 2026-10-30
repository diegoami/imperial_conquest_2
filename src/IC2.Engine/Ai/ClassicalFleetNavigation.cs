using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Ai;

/// <summary>T247 fleet target/navigation rules, research fleet-move-contract131 (b490785).</summary>
public static class ClassicalFleetNavigation
{
    public static bool Applies(AiView view) => ClassicalArmyNavigation.Applies(view);

    /// <summary>The eastern exclusion shared by the original sea replacement and port domain.</summary>
    public static bool ExcludedEasternZone(GridPoint point) => point.X > 247 || (point.X > 213 && point.Y > 98);

    /// <summary>4939C: first expanding square containing sea; last x/y scan match wins.</summary>
    public static GridPoint? ReplaceLandTarget(AiView view, GridPoint target) => ReplaceTarget(view, target, land: false);

    /// <summary>B840's AI mode 1 starts at the explicit target, accepting map codes 2..11, including east.</summary>
    public static GridPoint? LandingTarget(AiView view, GridPoint target) => ReplaceTarget(view, target, land: true);

    private static GridPoint? ReplaceTarget(AiView view, GridPoint target, bool land)
    {
        for (var radius = 0; radius <= Math.Max(view.World.Width, view.World.Height); radius++)
        {
            GridPoint? last = null;
            for (var x = Math.Max(0, target.X - radius); x <= Math.Min(view.World.Width - 1, target.X + radius); x++)
                for (var y = Math.Max(0, target.Y - radius); y <= Math.Min(view.World.Height - 1, target.Y + radius); y++)
                {
                    // Earlier squares had no match, so only the new boundary needs testing.
                    if (Math.Max(Math.Abs(x - target.X), Math.Abs(y - target.Y)) != radius) continue;
                    var point = new GridPoint(x, y);
                    var code = OriginalFleetPort.MapCode(view, point);
                    if (land ? code is >= 2 and <= 11 : !ExcludedEasternZone(point) && code is 0 or 1) last = point;
                }
            if (last is not null) return last;
        }
        return null; // No sea in the supported domain: do not invent a waypoint.
    }

    /// <summary>Original major/minor line, shared medium samples for the ferry transition.</summary>
    public static IEnumerable<GridPoint> Line(GridPoint from, GridPoint to, bool extend = false)
    {
        yield return from;
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = Math.Sign(to.X - from.X);
        var sy = Math.Sign(to.Y - from.Y);
        var xMajor = dx >= dy;
        var major = Math.Max(dx, dy);
        if (major == 0) yield break;
        var minor = Math.Min(dx, dy);
        var error = 2 * minor - major;
        var point = from;
        for (var i = 0; extend || i < major; i++)
        {
            while (error >= 0)
            {
                point = xMajor ? point with { Y = point.Y + sy } : point with { X = point.X + sx };
                error -= 2 * major;
            }
            point = xMajor ? point with { X = point.X + sx } : point with { Y = point.Y + sy };
            error += 2 * minor;
            yield return point;
        }
    }

    public static (GridPoint Accepted, GridPoint Rejected)? CoastTransition(AiView view, GridPoint from, GridPoint toward)
    {
        var previous = new GridPoint(-1, 0); // Only X has meaning if no sample is accepted.
        var sourceSea = ClassicalArmyNavigation.IsSea(view, from.X, from.Y);
        foreach (var point in Line(from, toward, extend: true).Skip(1))
        {
            if ((uint)point.X >= (uint)view.World.Width || (uint)point.Y >= (uint)view.World.Height)
                return null; // No bounded normal return is established for an all-accepted out-of-map scan.
            var same = ClassicalArmyNavigation.IsSea(view, point.X, point.Y) == sourceSea;
            if (!sourceSea && ExcludedEasternZone(point)) same = true;
            if (!same) return (previous, point);
            previous = point;
        }
        return null;
    }
}
