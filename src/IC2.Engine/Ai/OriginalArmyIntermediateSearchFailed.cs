using IC2.Engine.Core;

namespace IC2.Engine.Ai;

/// <summary>The initial DB38 invocation found no intermediate point. Only the immediate same-army E84C-to-EBE8
/// direct walk consumes this transient dispatch result; it is not a saved or global stack word.</summary>
[DomainEvent("ai.army-intermediate-search-failed")]
public sealed record OriginalArmyIntermediateSearchFailed(string ArmyId) : DomainEvent;
