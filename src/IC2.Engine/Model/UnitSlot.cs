using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// One unit inside an army or a city garrison — the reimplementation's form of the original's
/// 32-byte army unit slot.
/// </summary>
/// <param name="MercenaryLabel">
/// The regular/mercenary marker, the army unit slot's <c>+0</c> word: <c>0</c> is a regular unit and
/// any other value is a mercenary, the value being its index into the mercenary name table
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>. This is a record-format
/// sentinel, not a tunable rule: it selects which quarterly-upkeep formula applies to the unit and
/// blocks the unit from being merged with regulars, so it has to survive a round-trip verbatim.
/// Prefer <see cref="IsMercenary"/> over comparing the raw value.
/// </param>
/// <param name="UnitTypeId">Key into <see cref="Ruleset.UnitTypes"/>.</param>
/// <param name="Troops">Troop count in this unit.</param>
/// <param name="Quality">
/// The persisted per-unit quality tier, as a raw numeric tier
/// <strong>[confirmed: battle-quality-promotion-and-morale-array-decompiled.md]</strong>. The tier
/// floor and cap the battle resolver applies to it are ruleset data
/// (<see cref="CombatRules.QualityFloor"/>, <see cref="CombatRules.QualityCap"/>), never constants here.
/// </param>
/// <param name="Name">
/// The unit's display name — an auto-generated <c>"Nth Foot/Guards/Bowmen/Lancers/Dragoons Battalion"</c>
/// for a regular, an ethnic name for a mercenary.
/// </param>
public sealed record UnitSlot(
    int MercenaryLabel,
    string UnitTypeId,
    int Troops,
    int Quality,
    string Name)
{
    /// <summary>
    /// Whether this unit is a mercenary. See <see cref="MercenaryLabel"/> for why the marker is a raw
    /// word rather than a boolean on disk.
    /// </summary>
    [JsonIgnore]
    public bool IsMercenary => MercenaryLabel != 0;

    /// <summary>Whether this unit is a regular (non-mercenary) unit.</summary>
    [JsonIgnore]
    public bool IsRegular => MercenaryLabel == 0;
}
