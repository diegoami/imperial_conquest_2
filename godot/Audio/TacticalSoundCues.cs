using IC2.Engine.Assets;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.Audio;

/// <summary>Sound identity comes from producer metadata and loaded rules, never display names.</summary>
public static class TacticalSoundCues
{
    public static string? ForExchange(TacticalBattleFrame frame, Ruleset rules)
    {
        if (frame.Event is TacticalMeleeEvent) return AssetKeys.SfxBattleMelee;
        if (frame.Event is not TacticalShotEvent || frame.Actor is not { } actor) return null;
        // Slot.Type is an index into this ruleset, not a fixed archers=2 convention.
        var type = rules.UnitTypes.FirstOrDefault(t => t.Id == actor.TypeId);
        if (type is null) return null;
        return type.Id == rules.ArcherUnitTypeId ? AssetKeys.SfxBattleArrows : AssetKeys.SfxBattleJavelin;
    }
}
