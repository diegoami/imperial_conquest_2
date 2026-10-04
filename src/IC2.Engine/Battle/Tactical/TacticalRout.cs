namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// Morale and rout, <c>FUN_00438FB0</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §6]</strong>; <c>docs/game-design.md</c>,
/// "Morale and rout".
/// </summary>
/// <remarks>
/// <code>
/// Rout(u):
///   if troops_u ≥ standardBattalionSize(type_u) div 25 and morale_u &gt; 19:
///       if morale_u &gt; 39: return
///       if Random(morale_u) + Random(morale_u) &gt; 29: return
///   remove u (troops 0, its cell empty)
///   every live friend: morale −= 6; if morale &lt; 30: remove it (no further cascade)
///   every live enemy: morale = min(99, morale + 5); if its target is u: target = none
///   if either side has no live unit: the battle is over
/// </code>
/// The two draws are taken only in the 20–39 band with troops at or above the floor. A unit the cascade
/// drags out starts no cascade of its own, earns the enemy no <c>+5</c>, and clears no enemy target.
/// </remarks>
public static class TacticalRout
{
    /// <summary>Runs <c>Rout(u)</c> on slot <paramref name="slot"/>.</summary>
    public static TacticalBattleState Rout(TacticalBattleState state, int slot, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, slot);
        return board.Freeze();
    }

    internal static void Apply(TacticalBoard board, int slot)
    {
        var rules = board.Rules;
        var unit = board.Slots[slot];
        var floor = board.Context.StandardOf(unit.Type) / rules.RoutTroopsDivisor;

        if (unit.Troops >= floor && unit.Morale > rules.RoutMoraleAutomatic)
        {
            if (unit.Morale > rules.RoutMoraleSafe)
            {
                return;
            }

            var first = board.Draw(unit.Morale);
            var second = board.Draw(unit.Morale);
            if (unchecked(first + second) > rules.RoutDrawThreshold)
            {
                return;
            }
        }

        board.Remove(slot);
        board.Log.Add(new TacticalRoutedEvent(slot));

        var side = board.SideOf(slot);
        var friends = board.FirstSlotOf(side);
        for (var f = friends; f < friends + board.SlotsPerSide; f++)
        {
            if (!board.IsLive(f))
            {
                continue;
            }

            var friend = board.Slots[f];
            var morale = unchecked(friend.Morale - rules.RoutFriendPenalty);
            board.Slots[f] = friend with { Morale = morale };
            if (morale < rules.RoutCascadeBelow)
            {
                board.Remove(f);
                board.Log.Add(new TacticalCascadeRemovedEvent(f));
            }
        }

        var enemies = board.FirstSlotOf(TacticalBoard.Other(side));
        for (var e = enemies; e < enemies + board.SlotsPerSide; e++)
        {
            if (!board.IsLive(e))
            {
                continue;
            }

            var enemy = board.Slots[e];
            board.Slots[e] = enemy with
            {
                Morale = Math.Min(rules.MoraleCap, unchecked(enemy.Morale + rules.RoutEnemyBonus)),
                Target = enemy.Target == slot ? TacticalSlot.NoTarget : enemy.Target,
            };
        }

        if (!board.HasLiveUnit(TacticalBattleState.AttackerSide) || !board.HasLiveUnit(TacticalBattleState.DefenderSide))
        {
            board.EndBattle();
        }
    }
}
