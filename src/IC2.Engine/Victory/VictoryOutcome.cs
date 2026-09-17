using IC2.Engine.Model;

namespace IC2.Engine.Victory;

/// <summary>
/// Whether a victory condition has resolved when evaluated against a given <see cref="GameState"/>.
/// </summary>
public enum VictoryStatus
{
    /// <summary>No nation has met the condition yet, and no hard limit has been reached; play continues.</summary>
    Undecided,

    /// <summary>A nation has met the condition. The game is over and that nation won.</summary>
    Won,

    /// <summary>
    /// The condition's own hard limit was reached with nobody meeting it. The game is over, but with no
    /// winner — e.g. the original's 250 BC comparison in <c>THumanFalls_InitializeForm</c>
    /// (<c>docs/design-audit.md</c> §"Victory conditions", <c>tests/fixtures/corpus.json</c>'s
    /// <c>victory.yearLimitBC</c>).
    /// </summary>
    Expired,
}

/// <summary>
/// The result of evaluating one <see cref="VictoryConditionType"/> against a <see cref="GameState"/> —
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions", evaluated directly against a constructed
/// <see cref="GameState"/> without needing capture logic merged.
/// </summary>
/// <param name="Status">Whether the condition resolved, and how.</param>
/// <param name="ConditionType">Which condition this outcome answers for.</param>
/// <param name="WinningNationId">
/// The nation that won, when <paramref name="Status"/> is <see cref="VictoryStatus.Won"/>; otherwise
/// <see langword="null"/>.
/// </param>
public sealed record VictoryOutcome(
    VictoryStatus Status,
    VictoryConditionType ConditionType,
    string? WinningNationId = null)
{
    /// <summary>Whether the game is over under this outcome, whichever way it ended.</summary>
    public bool IsGameOver => Status != VictoryStatus.Undecided;

    /// <summary>Builds an "undecided, play continues" outcome for a condition.</summary>
    public static VictoryOutcome Undecided(VictoryConditionType conditionType) =>
        new(VictoryStatus.Undecided, conditionType);

    /// <summary>Builds a "this nation won" outcome for a condition.</summary>
    /// <exception cref="ArgumentException"><paramref name="winningNationId"/> is null or empty.</exception>
    public static VictoryOutcome Won(VictoryConditionType conditionType, string winningNationId)
    {
        if (string.IsNullOrEmpty(winningNationId))
        {
            throw new ArgumentException("A won outcome must name the winning nation.", nameof(winningNationId));
        }

        return new VictoryOutcome(VictoryStatus.Won, conditionType, winningNationId);
    }

    /// <summary>Builds a "hard limit reached, nobody won" outcome for a condition.</summary>
    public static VictoryOutcome Expired(VictoryConditionType conditionType) =>
        new(VictoryStatus.Expired, conditionType);
}
