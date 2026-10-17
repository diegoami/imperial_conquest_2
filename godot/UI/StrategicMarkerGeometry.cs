using System;
using System.Collections.Generic;
using System.Linq;

namespace IC2.Slice.UI;

public enum StrategicMarkerKind { Army, Fleet }

public readonly record struct StrategicMarker(string Id, StrategicMarkerKind Kind, int X, int Y);

public readonly record struct StrategicMarkerBounds(float X, float Y, float Size)
{
    public float CenterX => X + Size / 2;
    public float CenterY => Y + Size / 2;
    public bool Contains(float x, float y) => x >= X && x <= X + Size && y >= Y && y <= Y + Size;
}

/// <summary>
/// Production screen-space policy for strategic boards and picking. [designed]: 24 px keeps the
/// soldier/ship motifs and their three tiers readable at overview; growth follows the old army/fleet
/// proportions until a 48 px cap. The task's evidence and original UI audit establish no original
/// minimum screen-pixel size. Keys, thresholds, owner tint and world coordinates are independent.
/// </summary>
public static class StrategicMarkerGeometry
{
    public const float MinimumSize = 24;
    public const float MaximumSize = 48;

    public static float Size(StrategicMarkerKind kind, float tilePixels) =>
        Math.Clamp(tilePixels * (kind == StrategicMarkerKind.Army ? .8f : .7f), MinimumSize, MaximumSize);

    public static StrategicMarkerBounds Bounds(StrategicMarker marker, float tilePixels, float panX, float panY)
    {
        var size = Size(marker.Kind, tilePixels);
        return new(panX + (marker.X + .5f) * tilePixels - size / 2,
            panY + (marker.Y + .5f) * tilePixels - size / 2, size);
    }

    /// <summary>
    /// A hit includes the whole displayed square, including tile overhang and its boundary.
    /// Nearest centre wins dense overlaps; equal distances prefer army, then ordinal id.
    /// State/list order never changes the answer. Embarked/construction exclusion is the caller's
    /// shared visible-marker enumeration. No hit leaves city/tile resolution with the original tile.
    /// </summary>
    public static StrategicMarker? Pick(IEnumerable<StrategicMarker> markers, float x, float y,
        float tilePixels, float panX, float panY) => markers
        .Select(marker => (Marker: marker, Bounds: Bounds(marker, tilePixels, panX, panY)))
        .Where(hit => hit.Bounds.Contains(x, y))
        .OrderBy(hit => (hit.Bounds.CenterX - x) * (hit.Bounds.CenterX - x)
            + (hit.Bounds.CenterY - y) * (hit.Bounds.CenterY - y))
        .ThenBy(hit => hit.Marker.Kind)
        .ThenBy(hit => hit.Marker.Id, StringComparer.Ordinal)
        .Select(hit => (StrategicMarker?)hit.Marker).FirstOrDefault();
}
