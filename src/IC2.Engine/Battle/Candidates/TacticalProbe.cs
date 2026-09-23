using IC2.Engine.Core;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// A white-box handle on one C2/C5 tactical battle after its setup, so that a test can visit a single rule
/// directly with constructed inputs: read the seeded tactical morale (K26, K27, D08), set a unit's morale or
/// troops, and run §5's rout routine on one unit (the one-level cascade, K12–K14). Measurement code, like
/// every type in this namespace; nothing in the game uses it.
/// </summary>
public sealed class TacticalProbe
{
    private readonly TacticalBattle _battle;

    private TacticalProbe(TacticalBattle battle) => _battle = battle;

    /// <summary>Builds the battle and runs §6.2's setup only (morale seeding, shot pools, placement).</summary>
    /// <param name="battle">The two armies and the ruleset.</param>
    /// <param name="rng">The draws the setup consumes (one per unit).</param>
    /// <param name="retreat">False for C2's consequence (troops 0), true for C5's (<c>Flee</c>).</param>
    public static TacticalProbe AfterSetup(CandidateBattle battle, IRng rng, bool retreat)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rng);
        var engine = new TacticalBattle(battle, rng, retreat, recordEvents: true);
        engine.Setup(battle);
        return new TacticalProbe(engine);
    }

    /// <summary>A unit's tactical morale <c>m</c>.</summary>
    public int Morale(int side, int slot) => _battle.UnitAt(side, slot).Morale;

    /// <summary>Sets a unit's tactical morale <c>m</c>.</summary>
    public void SetMorale(int side, int slot, int morale) => _battle.UnitAt(side, slot).Morale = morale;

    /// <summary>A unit's troops (C5: its <c>fledTroops</c> once it has fled).</summary>
    public long Troops(int side, int slot) => _battle.UnitAt(side, slot).Troops;

    /// <summary>Whether a unit is still on the field.</summary>
    public bool IsLive(int side, int slot) => _battle.UnitAt(side, slot).Live;

    /// <summary>C5: whether a unit left the field by fleeing.</summary>
    public bool HasFled(int side, int slot) => _battle.UnitAt(side, slot).Fled;

    /// <summary>How a unit left the field, or <see cref="BreakCause.None"/>.</summary>
    public BreakCause Cause(int side, int slot) => _battle.UnitAt(side, slot).Cause;

    /// <summary>Whether K32 has ended the battle.</summary>
    public bool Ended => _battle.State.Ended;

    /// <summary>The events logged so far.</summary>
    public IReadOnlyList<CandidateEvent> Events => _battle.Events;

    /// <summary>Runs §5's rout routine (<c>FUN_00438fb0</c>) on one unit.</summary>
    public void RoutCheck(int side, int slot) => _battle.RoutCheckAt(side, slot);
}
