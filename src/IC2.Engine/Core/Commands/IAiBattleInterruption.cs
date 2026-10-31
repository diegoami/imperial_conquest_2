namespace IC2.Engine.Core;

/// <summary>The Faithful Tactical gate retains a partial AI pass instead of abandoning it.</summary>
public interface IAiBattleInterruption
{
    bool RetainPartialAiPass { get; }
    bool ArmyPhaseInterrupted { get; }
}
