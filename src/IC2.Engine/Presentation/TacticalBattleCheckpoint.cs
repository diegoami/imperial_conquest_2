using IC2.Engine.Battle.Tactical;

namespace IC2.Engine.Presentation;

/// <summary>Session-only exact tactical state and draw position; never a native save-format field.</summary>
public sealed record TacticalBattleCheckpoint(TacticalBattleState State, ulong DrawState);
