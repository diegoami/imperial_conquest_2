using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>Transient spectator policy; see docs/design/v011-continuous-observer.md.
/// Choosing a viewpoint never consumes the campaign's RNG.</summary>
public sealed class ContinuousObserverPolicy
{
    private readonly Func<int, int> _choose;
    public string? WatchedNationId { get; private set; }
    public ContinuousObserverPolicy(Func<int, int>? choose = null) => _choose = choose ?? Random.Shared.Next;

    public string? SelectSurvivor(GameState state, Scenario scenario)
    {
        var candidates = state.Nations.Where(n => !n.Eliminated && n.Id != scenario.Barbarians?.NationId
            && scenario.Seats.Any(s => s.Nation == n.Id)).Select(n => n.Id).ToArray();
        if (WatchedNationId is not null && candidates.Contains(WatchedNationId)) return WatchedNationId;
        WatchedNationId = candidates.Length == 0 ? null : candidates[_choose(candidates.Length)];
        return WatchedNationId;
    }

    /// <summary>Pure watch sessions can outlive a human reign. The showcase stops at the authored
    /// limit/conquest without changing the engine's existing watch semantics.</summary>
    public static string? Ending(GameSession session)
    {
        if (session.IsGameOver) return "Campaign complete";
        if (session.State.Calendar.YearBc <= session.Ruleset.Victory.HardEndYearBc) return "Campaign time limit reached";
        if (session.Scenario.TurnLimit is { } limit && session.State.Calendar.TurnIndex >= limit)
            return "Campaign turn limit reached";
        if (session.Scenario.Victory.Type == VictoryConditionType.TotalConquest && session.State.Cities.Count > 0
            && session.State.Cities.Select(c => c.Owner).Distinct().Count() == 1) return "Total conquest";
        if (!session.State.Nations.Any(n => !n.Eliminated && n.Id != session.Scenario.Barbarians?.NationId
            && session.Scenario.Seats.Any(s => s.Nation == n.Id)))
            return "No surviving civilizations";
        return null;
    }
}
