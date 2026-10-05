namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// Pass 1's target choice, <c>FUN_00439E08</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7, "Target choice", "The target score"]</strong>;
/// <c>docs/game-design.md</c>, "The computer general", "Pass 1".
/// </summary>
/// <remarks>
/// <para>
/// The unit's type gives its preferences (<c>combat.tactical.targetPreferences</c>): light infantry
/// prefers archers, then light infantry, then any; heavy infantry prefers heavy infantry, then any; heavy
/// cavalry prefers heavy infantry, then any; archers and light cavalry take any. The first preferred type
/// with a live enemy is the one chosen among; with none, every live enemy is.
/// </para>
/// <para>
/// Among those enemies, in slot order, it takes the smallest score: <c>v = troops × quality × morale ×
/// M[theirs][mine] div 10000</c> (the enemy's troops, quality and morale; 32-bit, wrapping), then, with
/// <c>c</c> the focus count (the own slots, alive or not, whose melee target is that enemy) plus the
/// claims already recorded on it, <c>v div (c + 1)</c> when <c>c &lt; 4</c> (<c>claimLimit</c>) and
/// <c>v × 2</c> otherwise. Ties go to the first, by a strict <c>&lt;</c>. The unit's claim on the chosen
/// enemy is recorded (<see cref="TacticalBattleState.GeneralClaims"/>, indexed by the enemy's slot; the
/// half-round setup clears them, T123).
/// </para>
/// <para>
/// The <c>+ 1</c> of <c>c + 1</c> and the doubling's <c>× 2</c> are the score's shape (report §7), not
/// <c>combat.tactical</c> fields.
/// </para>
/// </remarks>
public static class GeneralTargeting
{
    /// <summary>The score's <c>× 2</c> at or above the claim limit (report §7).</summary>
    private const int CrowdedMultiplier = 2;

    /// <summary>The chosen target, or <c>−1</c>, and the state with the claim recorded.</summary>
    /// <param name="State">The battle after the claim.</param>
    /// <param name="Target">The chosen enemy slot, or <c>−1</c> when no enemy lives.</param>
    public sealed record Choice(TacticalBattleState State, int Target);

    /// <summary>Chooses unit <paramref name="slot"/>'s target and records its claim.</summary>
    public static Choice Choose(TacticalBattleState state, int slot, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        var target = Apply(board, slot, journal: null);
        return new Choice(board.Freeze(), target);
    }

    /// <summary>The raw score <c>v</c> of enemy <paramref name="theirs"/> for unit <paramref name="mine"/>, before the claims.</summary>
    public static int Score(TacticalBattleState state, int mine, int theirs, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return Score(board, mine, theirs);
    }

    /// <summary>The focus count: the own slots of <paramref name="side"/>, alive or not, whose melee target is <paramref name="enemy"/>.</summary>
    public static int Focus(TacticalBattleState state, int side, int enemy, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return Focus(board, side, enemy);
    }

    internal static int Apply(TacticalBoard board, int slot, IList<GeneralDecision>? journal)
    {
        var context = board.Context;
        var unit = board.Slots[slot];
        var enemyFirst = board.FirstSlotOf(TacticalBoard.Other(board.SideOf(slot)));

        var target = -1;
        foreach (var preferred in Preferences(context, unit.Type))
        {
            target = Best(board, slot, enemyFirst, preferred);
            if (target >= 0)
            {
                break;
            }
        }

        if (target < 0)
        {
            target = Best(board, slot, enemyFirst, wantedType: -1);
        }

        if (target >= 0)
        {
            board.GeneralClaims[target] = unchecked(board.GeneralClaims[target] + 1);
            journal?.Add(new GeneralDecision(slot, GeneralStep.TargetChosen, target, board.Slots[target].X, board.Slots[target].Y));
        }

        return target;
    }

    internal static int Score(TacticalBoard board, int mine, int theirs)
    {
        var them = board.Slots[theirs];
        var me = board.Slots[mine];
        return unchecked(them.Troops * them.Quality * them.Morale * board.Context.Matrix(them.Type, me.Type))
               / board.Rules.ScoreDivisor;
    }

    internal static int Focus(TacticalBoard board, int side, int enemy)
    {
        var first = board.FirstSlotOf(side);
        var focus = 0;
        for (var s = first; s < first + board.SlotsPerSide; s++)
        {
            if (board.Slots[s].Target == enemy)
            {
                focus++;
            }
        }

        return focus;
    }

    /// <summary>The preferred types of <paramref name="type"/>, in order; empty for a type with no entry.</summary>
    private static IEnumerable<int> Preferences(TacticalContext context, int type)
    {
        var id = context.TypeOf(type).Id;
        foreach (var entry in context.Rules.TargetPreferences)
        {
            if (string.Equals(entry.Type, id, StringComparison.Ordinal))
            {
                foreach (var preferred in entry.Prefers)
                {
                    yield return context.TypeIndexOf(preferred);
                }

                yield break;
            }
        }
    }

    private static int Best(TacticalBoard board, int slot, int enemyFirst, int wantedType)
    {
        var rules = board.Rules;
        var side = board.SideOf(slot);
        var best = -1;
        var bestScore = 0;
        for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
        {
            if (!board.IsLive(e) || (wantedType >= 0 && board.Slots[e].Type != wantedType))
            {
                continue;
            }

            var v = Score(board, slot, e);
            var c = unchecked(Focus(board, side, e) + board.GeneralClaims[e]);
            v = c < rules.ClaimLimit ? v / unchecked(c + 1) : unchecked(v * CrowdedMultiplier);
            if (best < 0 || v < bestScore)
            {
                best = e;
                bestScore = v;
            }
        }

        return best;
    }
}
