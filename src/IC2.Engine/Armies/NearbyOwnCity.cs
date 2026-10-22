using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>
/// The own city that receives a disbanded army's or a scuttled fleet's supplies (#1343):
/// <c>FUN_004494e4</c>, read in <c>2026-10-22-manual-gaps-disband-purchase-storm-ai-war.md</c> §1 (research
/// <c>0658ff3</c>) <strong>[confirmed: code; the two-city case was not played]</strong>.
/// </summary>
/// <remarks>
/// The original scans the cells around the unit with the x offset as the outer loop and the y offset as
/// the inner one, each from <c>−r</c> to <c>+r</c>, and overwrites its result at every own city, so the
/// <strong>last</strong> own city in that order wins: the largest x offset, then the largest y offset. The
/// clone's X and Y are the original's x and y (the 320-cell axis is X in both). Disband army and Scuttle
/// fleet share this one helper so the two cannot drift, as their adjacency once did (#222).
/// </remarks>
public static class NearbyOwnCity
{
    /// <summary>
    /// The last own city of <paramref name="nationId"/> within Chebyshev distance <paramref name="radius"/>
    /// of <c>(x, y)</c> in the original's scan order, or <see langword="null"/> when there is none.
    /// </summary>
    public static CityState? Find(GameState state, string nationId, int x, int y, int radius)
    {
        ArgumentNullException.ThrowIfNull(state);

        CityState? found = null;
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                foreach (var city in state.Cities)
                {
                    if (city.X == x + dx && city.Y == y + dy
                        && string.Equals(city.Owner, nationId, StringComparison.Ordinal))
                    {
                        found = city;
                    }
                }
            }
        }

        return found;
    }
}
