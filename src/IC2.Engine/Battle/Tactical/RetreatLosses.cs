using IC2.Engine.Model;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// [designed, approved PR 1142 comment 6215718392] combat.tactical.retreatLosses: a common army fraction,
/// applied by floor to each unit. Searched tactical decompile sections 4-9 and sweep B5 at research 87d1571:
/// neither has a pursuit model or calibrates these ruleset parameters. No variance or RNG draw is added.
/// </summary>
public static class RetreatLosses
{
    /// <summary>The ruleset's loss numerator; only live on-board winner troops contribute pursuit.</summary>
    public static int Fraction(TacticalBattleState battle, int winnerSide, long remnantTroops, TacticalContext context)
    {
        if (remnantTroops <= 0) return 0;
        var t = context.Rules;
        var weights = t.RetreatPursuitWeights ?? throw new ArgumentException("Retreat pursuit weights are required.", nameof(context));
        long pursuit = 0;
        var first = winnerSide * battle.SlotsPerSide;
        for (var i = first; i < first + battle.SlotsPerSide; i++)
        {
            var unit = battle.Slots[i];
            if (unit.IsLive) pursuit = checked(pursuit + ((long)unit.Troops * weights[unit.Type]));
        }
        return (int)Math.Min(t.RetreatLossCap, checked(t.RetreatLossBase + checked(t.RetreatLossPursuit * pursuit) / remnantTroops));
    }

    /// <summary>Floor each unit's loss independently, with no minimum-one loss or rounding redistribution.</summary>
    public static ValueList<UnitSlot> Apply(ValueList<UnitSlot> remnant, TacticalBattleState battle, int winnerSide, TacticalContext context)
    {
        var fraction = Fraction(battle, winnerSide, remnant.Sum(u => (long)u.Troops), context);
        return ValueList.From(remnant.Select(u => u with
        {
            Troops = u.Troops - (int)(((long)u.Troops * fraction) / context.Rules.RetreatLossDenominator),
        }).Where(u => u.Troops > 0));
    }
}
