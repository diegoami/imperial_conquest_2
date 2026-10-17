namespace IC2.Engine.Battle.Tactical;

/// <summary>Transient presentation facts captured from the actual write-back, never saved.</summary>
public sealed record TacticalArmyAftermath(int ExchangeCasualties, int RoutedUnits, int CascadeUnits,
    int RejoinedTroops, int SurvivingTroops, bool ArmyRemoved, int UnityBefore, int UnityAfter);

/// <summary>Final army fates and observed unity changes; routing is distinct from casualties.</summary>
public sealed record TacticalBattleAftermath(TacticalArmyAftermath Winner, TacticalArmyAftermath Loser,
    bool RetreatEnabled, bool ScatterFailed, int PursuitLosses);
