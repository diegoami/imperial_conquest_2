namespace IC2.Engine.Battle.Tactical.General;

/// <summary>What the computer general decided at one step of its half-round.</summary>
public enum GeneralStep
{
    /// <summary>Placement put a unit on a cell (<c>FUN_004381A4</c>).</summary>
    Placed,

    /// <summary>Pass 1's target choice picked an enemy and recorded a claim (<c>FUN_00439E08</c>).</summary>
    TargetChosen,

    /// <summary>Engage: archers with shots at distance below 3 fire at the target.</summary>
    ArcherVolley,

    /// <summary>Engage: an adjacent unit shoots, then takes the enemy as its melee target if it lives.</summary>
    AdjacentEngage,

    /// <summary>Engage: the line is clear, so the unit moves toward the target.</summary>
    ApproachLine,

    /// <summary>Engage: the line is not clear, so the unit moves toward a cell of the target's box.</summary>
    ApproachBox,

    /// <summary>Pass 2's shot target (<c>FUN_0043A544</c>).</summary>
    PassTwoShots,

    /// <summary>Pass 2's melee target (<c>FUN_0043A544</c>).</summary>
    PassTwoTarget,

    /// <summary>Pass 3: the first enemy by threat with a clear line; the unit moves toward it (<c>FUN_0043ABB4</c>).</summary>
    ThreatLine,

    /// <summary>Pass 3: a cell with a clear line in that enemy's box; the unit moves toward it.</summary>
    ThreatBox,

    /// <summary>Pass 3's flank: the destination tried and walked to.</summary>
    Flank,
}

/// <summary>
/// One entry of the computer general's journal: the unit, the step, the enemy it concerned (or <c>−1</c>)
/// and the cell it aimed at. The battle's own trace (<see cref="TacticalBattleState.Log"/>) records what
/// the orders did (each step, shot and target); the journal records why, so a test can assert a unit's
/// destination beside the path the trace shows.
/// </summary>
/// <param name="Slot">The general's unit.</param>
/// <param name="Step">The decision.</param>
/// <param name="Enemy">The enemy slot the decision concerns, or <c>−1</c>.</param>
/// <param name="X">The destination or target column.</param>
/// <param name="Y">The destination or target row.</param>
public sealed record GeneralDecision(int Slot, GeneralStep Step, int Enemy, int X, int Y);
