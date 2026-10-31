using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;

namespace IC2.Engine.Ai;

/// <summary>
/// FUN_0044FB7C's closing partner/candidate loops, once per seat invocation. The cursor advances
/// through nation records, never back to a newly gained lower-index partner. No saved or static state.
/// </summary>
internal sealed class OriginalTradeSwapPass
{
    private int _partner;
    private int _candidate;
    private bool _finished;

    public void Propose(AiView view, List<AiCandidate> into)
    {
        if (_finished) return;
        // The original reaches this pass after its two fresh-trade loops. Keep the existing scored
        // driver, but do not consume this cursor while an ordinary trade can still be made.
        if (AiOwnDiplomacyRule.EligibleTradePartners(view.State, view.Ruleset, view.NationId).Count > 0) return;
        for (; _partner < view.State.Nations.Count; _partner++, _candidate = 0)
        {
            for (; _candidate < view.State.Nations.Count; _candidate++)
            {
                var poor = view.State.Nations[_partner].Id;
                var rich = view.State.Nations[_candidate].Id;
                if (!AiOwnDiplomacyRule.MaySwapTradePartner(view.State, view.Ruleset, view.NationId, poor, rich)) continue;
                into.Add(AiCandidate.Single(AiPhase.Diplomacy, "ai-swap-trade-partner",
                    new AiSwapTradePartnerCommand(view.NationId, poor, rich), view.Ruleset.Ai.OwnTradeSwapScore,
                    AiFormat.Inv("swap trade partner {0} for richer {1}", poor, rich)));
                return; // Selection may choose another action. Consume only after dispatch.
            }
        }
        _finished = true;
    }

    public void Dispatched(bool rejected)
    {
        _candidate++;
        // A surprising refusal must not restart or replay the pass against unchanged state.
        if (rejected) _finished = true;
    }
}
