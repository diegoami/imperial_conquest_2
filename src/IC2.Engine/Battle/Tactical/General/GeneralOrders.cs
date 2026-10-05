namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// The one order the general gives without a T123 routine to call: "set a target". A human's
/// <see cref="TacticalOrders.SetTarget"/> is refused while the computer drives the side, so the general
/// writes the slot's target word and records it on the trace exactly as the order does
/// (<see cref="TacticalTargetSetEvent"/>), and nothing else: the melee that follows is T123's.
/// </summary>
internal static class GeneralOrders
{
    internal static void SetTarget(TacticalBoard board, int slot, int target)
    {
        board.Slots[slot] = board.Slots[slot] with { Target = target };
        board.Log.Add(new TacticalTargetSetEvent(slot, target));
    }

    /// <summary>The general's units, in its iteration order: <c>typeOrder</c>, then slot order; each yielded only while it lives.</summary>
    internal static IEnumerable<int> InOrder(TacticalBoard board)
    {
        var context = board.Context;
        var first = board.FirstSlotOf(board.SideToMove);
        foreach (var typeId in context.Rules.TypeOrder)
        {
            var type = context.TypeIndexOf(typeId);
            for (var s = first; s < first + board.SlotsPerSide; s++)
            {
                if (board.IsOver)
                {
                    yield break;
                }

                if (board.IsLive(s) && board.Slots[s].Type == type)
                {
                    yield return s;
                }
            }
        }
    }
}
