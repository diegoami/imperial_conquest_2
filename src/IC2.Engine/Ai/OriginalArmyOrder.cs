using IC2.Engine.Armies;

namespace IC2.Engine.Ai;

/// <summary>F31C's forward-only army table loop, with the count read once at entry.</summary>
internal sealed class OriginalArmyOrder(int entryCount)
{
    private int _slot;
    private string? _armyId;
    private int _sampledThrough = -1;
    private int _offeredSlot;
    private string? _offeredId;

    internal void Propose(AiView view, long progress, IReadOnlyList<string> marched,
        List<AiCandidate> into, AiSiegeGateTally? siegeGates, ICollection<string>? boxedIn)
    {
        _offeredId = null;
        var slot = _slot;
        var table = ArmyTable.Slots(view.State);
        while (slot < entryCount && slot < table.Count)
        {
            var army = table[slot];
            if (army is null || army.Nation != view.NationId || army.TotalTroops <= 0) { slot++; continue; }
            // Original table sweeps preserve a dead slot during the seat. If a custom command removes
            // the current record instead, advance safely rather than inheriting another army's turn.
            if (slot == _slot && _armyId is not null && _armyId != army.Id) { slot++; continue; }
            var candidates = new List<AiCandidate>();
            AiMilitaryPhase.ProposeArmy(view, army, progress, marched, candidates,
                slot <= _sampledThrough ? null : siegeGates, boxedIn);
            _sampledThrough = Math.Max(_sampledThrough, slot);
            if (candidates.Any(c => c.Score >= view.Ruleset.Ai.MinimumActionScore))
            {
                into.AddRange(candidates);
                _offeredSlot = slot;
                _offeredId = army.Id;
                return; // This army owns the next military action, independent of later scores.
            }
            slot++;
        }
    }

    internal void Dispatched(string? armyId)
    {
        // Proposals do not consume table positions: a non-army decision may win and change the state
        // before military execution. Commit this position only when its own command was selected.
        if (_offeredId is not null && _offeredId == armyId) { _slot = _offeredSlot; _armyId = armyId; }
    }
}
