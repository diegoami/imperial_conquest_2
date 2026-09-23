using IC2.Engine.Core;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// C2 (§6.2): the original's tactical model run headless — the decompiled exchange arithmetic and rout
/// routine, driven by the designed D01–D06/D09–D12 driver. Unit-level break: <c>FUN_00438fb0</c> unchanged,
/// consequence <c>troops = 0</c>. No army-level exit. Survivors: none, by construction.
/// Draws (variable): 1 per unit at setup, 2 per shot, 4 per melee exchange, 2 per band check.
/// </summary>
public sealed class HeadlessTacticalCandidate : IAutoResolveCandidate
{
    /// <inheritdoc />
    public string Key => "C2";

    /// <inheritdoc />
    public string Name => "the original's tactical model, run headless";

    /// <inheritdoc />
    public CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false) =>
        TacticalBattle.Run(battle, rng, retreat: false, recordEvents);
}

/// <summary>
/// C5 (§6.5): C2 with three changes — a broken unit flees and pays for leaving (D40–D42), what leaving
/// costs depends on who is chasing (§6.5.2), and a side can make an ordered withdrawal (D43, D44). Fled
/// winners rejoin (D45); the loser's fled units are the scattered army. Draws: C2's, plus 2 per cavalry
/// pursuit hit and 2 per pursuing shot.
/// </summary>
public sealed class MoraleRetreatCandidate : IAutoResolveCandidate
{
    /// <inheritdoc />
    public string Key => "C5";

    /// <inheritdoc />
    public string Name => "morale and retreat (the Total War shape)";

    /// <inheritdoc />
    public CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false) =>
        TacticalBattle.Run(battle, rng, retreat: true, recordEvents);
}
