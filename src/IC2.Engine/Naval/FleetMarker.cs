using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// The fleet map-marker's owner-and-size encoding — <c>docs/task-catalogue.md</c> "T14 Naval"
/// Done-when 8. <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md part 3]</strong>:
/// <c>FUN_0044A878</c> computes <c>marker = owner + band</c>, where <c>band</c> steps up by
/// <see cref="NavalRules.MarkerBandStep"/> (16) per ship-count tier from
/// <see cref="NavalRules.MarkerBandBaseTier1"/> (300) — closing the "333 vs 335" question
/// <c>roadmap.md</c> §2 carried as open: they are not two fleet states, they are two different nations'
/// large fleets (90 ships owner 1 → 333; 70 ships owner 3 → 335, both tier 3, band 332).
/// </summary>
public static class FleetMarker
{
    /// <summary>Encodes a fleet's map marker from its ship count and owner code.</summary>
    /// <param name="ships">The fleet's ship count, banded by <see cref="MapMarkerRules.FleetShipTierThresholds"/>.</param>
    /// <param name="ownerCode">The owning nation's numeric code, added directly onto the band base.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="MapMarkerRules.FleetShipTierThresholds"/> (the existing, T02-owned tier
    /// boundaries: 25, 50) for which band applies, and <see cref="NavalRules.MarkerBandBaseTier1"/>/
    /// <see cref="NavalRules.MarkerBandStep"/> (this task's own fields) for the band's numeric encoding
    /// — never a C# literal.
    /// </param>
    public static int Encode(int ships, int ownerCode, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var tierIndex = 0;
        foreach (var threshold in ruleset.MapMarkers.FleetShipTierThresholds)
        {
            if (ships < threshold)
            {
                break;
            }

            tierIndex++;
        }

        var band = ruleset.Naval.MarkerBandBaseTier1 + (tierIndex * ruleset.Naval.MarkerBandStep);
        return band + ownerCode;
    }
}
