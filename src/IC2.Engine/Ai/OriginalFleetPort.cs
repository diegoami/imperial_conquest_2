using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Ai;

/// <summary>E9A8/E920, source-precision addendum171c746: local sea probe, two ordered scans, no path test.</summary>
public static class OriginalFleetPort
{
    public static CityState? Pick(AiView view, FleetState fleet)
    {
        CityState? chosen = null;
        var best = 1000;
        foreach (var city in view.State.Cities)
        {
            var own = city.Owner == view.NationId;
            var relation = view.RelationBetween(view.NationId, city.Owner);
            if (!((own && city.SupplyTons > 20)
                || (relation is < 3 && city.SupplyTons > 120 && fleet.Money > 10)) || !IsPort(view, new(city.X, city.Y))) continue;
            var score = AiView.Distance(city.X, city.Y, fleet.X, fleet.Y);
            if (own && score == 1 && fleet.SupplyTons > 0) score = -1;
            else if (!own) score = unchecked((short)(score + 50));
            if (score >= best) continue;
            chosen = city;
            best = score;
        }
        if (chosen is null || (chosen.Owner != view.NationId && best > 30))
        {
            best = 1000; // Deliberately retain the foreign result if no own port replaces it.
            foreach (var city in view.State.Cities)
            {
                if (city.Owner != view.NationId || !IsPort(view, new(city.X, city.Y))) continue;
                var score = AiView.Distance(city.X, city.Y, fleet.X, fleet.Y);
                if (score >= best) continue;
                chosen = city;
                best = score;
            }
        }
        return chosen;
    }

    public static bool IsPort(AiView view, GridPoint point)
    {
        if (ClassicalFleetNavigation.ExcludedEasternZone(point)) return false;
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                var probe = new GridPoint(point.X + dx, point.Y + dy);
                // Original helper does not clamp; supported classical cities are interior. Do not
                // emulate arbitrary out-of-map reads for edited diagnostic worlds.
                if ((uint)probe.X >= (uint)view.World.Width || (uint)probe.Y >= (uint)view.World.Height) continue;
                if (MapCode(view, probe) < 2) return true;
            }
        return false;
    }

    internal static int MapCode(AiView view, GridPoint point)
    {
        var state = view.State;
        if (state.Fleets.Any(f => !f.IsUnderConstruction && f.X == point.X && f.Y == point.Y)) return 300;
        if (state.Armies.Any(a => a.CoveredTileCode is not null && a.X == point.X && a.Y == point.Y)) return 200;
        if (state.Cities.Any(c => c.X == point.X && c.Y == point.Y)) return 20;
        if (state.IsRoughSea(point.X, point.Y)) return 1;
        return unchecked((short)view.TerrainCells[point.Y * view.World.Width + point.X]);
    }
}
