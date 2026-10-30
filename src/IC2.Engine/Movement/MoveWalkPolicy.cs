namespace IC2.Engine.Movement;

/// <summary>
/// How many times a move order calls the walker, set by whoever builds the order and never inferred by
/// the handler from seat control.
/// </summary>
public enum MoveWalkPolicy
{
    /// <summary>One walk toward the destination: every order the AI builds, and the default.</summary>
    Single,

    /// <summary>
    /// T238 (#1585): a human order. When the first walk ends short of the destination with moves left,
    /// the walker runs once more from the new position on a fresh line, as <c>TUnitMap_MoveHumanArmy</c>
    /// (<c>0x00446D9C</c>) and <c>TUnitMap_MoveHumanFleet</c> (<c>0x00446E24</c>) do. Never more than two walks.
    /// </summary>
    HumanSecondWalk,

    /// <summary>
    /// T239 (#1592): a faithful AI march (<c>FUN_0044dba8</c>). After the first walk it keeps walking, level
    /// by level, until the army is within 1 tile of the order's target or out of moves; a level that makes no
    /// progress walks toward the local fallback point and costs 1 move whatever happens, so moves may end at
    /// −1. Needs the order's target.
    /// </summary>
    AiContinuation,

    /// <summary>T247: original AI fleet E1FC routing, fallback, recursion and returning arrivals.</summary>
    AiFleetContinuation,
}
