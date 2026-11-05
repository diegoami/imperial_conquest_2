using Godot;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

public partial class GameMapView
{
    public IReadOnlyList<(bool IsFleet, Vector2 Position)> RallyMarkersForCheck { get; private set; }
        = Array.Empty<(bool, Vector2)>();

    private void DrawRallyPoints(float tileSize)
    {
        if (_session is null || _session.IsObserverMode) { RallyMarkersForCheck = Array.Empty<(bool, Vector2)>(); return; }
        var points = _session.NationRallyPoints.Where(p => p.NationId == DisplayState.ActiveNationId).ToArray();
        var markers = new List<(bool, Vector2)>();
        foreach (var point in points)
        {
            var center = TileCenter(point.X, point.Y, tileSize);
            var radius = Mathf.Max(5f, tileSize * .28f);
            // Separate the two symbols even when army and fleet target the same port.
            if (points.Any(p => p.IsFleet != point.IsFleet && p.X == point.X && p.Y == point.Y))
                center.X += point.IsFleet ? radius + 2 : -radius - 2;
            center.Y -= tileSize * .4f;
            markers.Add((point.IsFleet, center));
            DrawCircle(center, radius + 2, Colors.Black);
            DrawArc(center, radius + 1, 0, Mathf.Tau, 24, Colors.White, 1f);
            var top = center + new Vector2(0, -radius * .7f);
            var bottom = center + new Vector2(0, radius * .7f);
            DrawLine(top, bottom, Colors.White, 1.5f);
            if (point.IsFleet)
            {
                DrawCircle(top, 1.4f, Colors.White);
                DrawLine(bottom, center + new Vector2(-radius * .65f, 0), Colors.White, 1.5f);
                DrawLine(bottom, center + new Vector2(radius * .65f, 0), Colors.White, 1.5f);
                DrawLine(center + new Vector2(-radius * .5f, -.05f * radius), center + new Vector2(radius * .5f, -.05f * radius), Colors.White, 1.5f);
            }
            else
                DrawColoredPolygon(new[] { top, top + new Vector2(radius * .7f, radius * .35f),
                    top + new Vector2(0, radius * .7f) }, Colors.White);
        }
        RallyMarkersForCheck = markers;
    }
}
