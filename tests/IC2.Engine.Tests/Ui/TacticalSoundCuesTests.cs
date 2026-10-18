using IC2.Engine.Assets;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Audio;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class TacticalSoundCuesTests
{
    internal static (GameSession Session, TacticalBattleFrame[] Frames) Battle(Ruleset? rules = null)
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, rules,
            TacticalSessionTestbed.StrongAttacker());
        var opening = session.Submit("attack-army north-army-1 south-army-1").TacticalTimeline;
        var ending = session.BattleGeneral(true);
        Assert.Null(ending.OpenBattleView);
        return (session, opening.Concat(ending.TacticalTimeline).ToArray());
    }

    [Fact]
    public void Actual_shooters_use_loaded_archer_identity_even_with_renamed_reordered_types()
    {
        var rules = TacticalSessionTestbed.Toy.Ruleset;
        rules = rules with { UnitTypes = ValueList.From(rules.UnitTypes.Reverse().Select(t => t with { Name = "not an archer" })) };
        var (session, frames) = Battle(rules);
        var shots = frames.Where(f => f.Event is TacticalShotEvent).ToArray();
        Assert.Contains(shots, f => f.Actor!.TypeId == rules.ArcherUnitTypeId);
        Assert.Contains(shots, f => f.Actor!.TypeId != rules.ArcherUnitTypeId);
        foreach (var frame in shots)
            Assert.Equal(frame.Actor!.TypeId == rules.ArcherUnitTypeId ? AssetKeys.SfxBattleArrows : AssetKeys.SfxBattleJavelin,
                TacticalSoundCues.ForExchange(frame, session.Ruleset));
        Assert.All(frames.Where(f => f.Event is TacticalMeleeEvent),
            f => Assert.Equal(AssetKeys.SfxBattleMelee, TacticalSoundCues.ForExchange(f, rules)));
        Assert.All(frames.Where(f => f.Event is not (TacticalShotEvent or TacticalMeleeEvent)),
            f => Assert.Null(TacticalSoundCues.ForExchange(f, rules)));
        var shot = shots[0];
        Assert.Null(TacticalSoundCues.ForExchange(shot with { Actor = null }, rules));
        Assert.Null(TacticalSoundCues.ForExchange(shot with { Actor = shot.Actor! with { TypeId = "missing" } }, rules));
    }

    [Fact]
    public void Only_matching_tactical_resolution_loses_generic_cue_with_unrelated_strategic_events_preserved()
    {
        var (session, frames) = Battle();
        var view = frames[0].After.View;
        var result = new BattleResult(BattleKind.Field, view.AttackerArmyId, view.DefenderArmyId,
            view.AttackerNationId, view.DefenderNationId, 100, 80, BattleSide.Attacker, null, LoserFate.Destroyed,
            10, 50, ValueList<UnitCasualty>.Empty, ValueList<UnitPromotion>.Empty, 0, 0, 0, 0, 0, 0, 0, false, false, null);
        DomainEvent[] events = [new BattleResolved(result), new BattleResolved(result with { AttackerId = "other" })];
        Assert.Equal(new[] { AssetKeys.SfxBattle, AssetKeys.SfxBattle }, SoundCues.ForEvents(events, session.State));
        Assert.Equal(new[] { AssetKeys.SfxBattle }, SoundCues.ForEvents(events, session.State, frames));
        Assert.Empty(SoundCues.ForEvents(new DomainEvent[] { events[0] }, session.State, frames));
    }
}
