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

/// <summary>Production one-tile policy [designed]. Original UI audit, design-audit and T187
/// establish no researched minimum size. Overview sampling can erase tier detail; no pixel
/// floor may enlarge a marker beyond its real tile. Coordinates, tint and thresholds are separate.</summary>
public static class StrategicMarkerGeometry
{
    public const float MaximumSize = 48;
    public static float Size(StrategicMarkerKind kind, float tilePixels) =>
        Math.Min(Math.Max(0, tilePixels) * (kind == StrategicMarkerKind.Army ? .8f : .7f), MaximumSize);

    // A centred stroke must remain inside the shared art rectangle even below one canvas pixel.
    public static float StrokeWidth(float markerSize, bool selected) => Math.Min(selected ? 2.5f : 1f, markerSize / 4);
    public static StrategicMarkerBounds Outline(StrategicMarkerBounds bounds, bool selected)
    {
        var inset = StrokeWidth(bounds.Size, selected) / 2;
        return new(bounds.X + inset, bounds.Y + inset, bounds.Size - inset * 2);
    }

    public static StrategicMarkerBounds Bounds(StrategicMarker marker, float tilePixels, float panX, float panY)
    {
        var size = Size(marker.Kind, tilePixels);
        return new(panX + (marker.X + .5f) * tilePixels - size / 2,
            panY + (marker.Y + .5f) * tilePixels - size / 2, size);
    }

    /// <summary>The complete real tile is the target, including padding around the art.
    /// Half-open edges agree with the map's floor-to-tile operation. Coincident targets use
    /// army then ordinal id; caller excludes embarked armies and construction fleets.</summary>
    public static StrategicMarker? Pick(IEnumerable<StrategicMarker> markers, float x, float y,
        float tilePixels, float panX, float panY)
    {
        if (tilePixels <= 0) return null;
        var tx = (int)Math.Floor((x - panX) / tilePixels);
        var ty = (int)Math.Floor((y - panY) / tilePixels);
        return markers.Where(m => m.X == tx && m.Y == ty)
            .OrderBy(m => m.Kind).ThenBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => (StrategicMarker?)m).FirstOrDefault();
    }

    /// <summary>Idle coincident boards draw fleet before army and reverse ordinal ids. Selection
    /// stays visible, while the pointer winner is promoted last so clicking its visible board is
    /// predictable. Promotion does not feed back into Pick, preventing hover oscillation.</summary>
    public static IEnumerable<StrategicMarker> DrawOrder(IEnumerable<StrategicMarker> markers,
        string? selectedArmy, string? selectedFleet, StrategicMarker? hovered) => markers
        .OrderByDescending(m => m.Kind)
        .ThenByDescending(m => m.Id, StringComparer.Ordinal)
        .OrderBy(m => (m.Kind == StrategicMarkerKind.Army ? m.Id == selectedArmy : m.Id == selectedFleet) ? 1 : 0)
        .OrderBy(m => m == hovered ? 1 : 0);

}
