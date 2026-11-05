namespace IC2.Engine.Presentation;

/// <summary>Transient observer scheduling. Call once per available UI frame; never owns a thread or save field.</summary>
public sealed class ObserverRunController
{
    public bool IsRunning { get; private set; }
    public int? StopAtTurn { get; private set; }
    public string? StopReason { get; private set; }

    public void Play()
    {
        IsRunning = true;
        StopAtTurn = null;
        StopReason = null;
    }

    public void NextRound(GameSession session, int? displayedTurn = null)
    {
        Play();
        StopAtTurn = (displayedTurn ?? session.State.Calendar.TurnIndex) + 1;
    }

    public void Pause()
    {
        IsRunning = false;
        StopAtTurn = null;
    }

    /// <summary>Pause presentation without losing an in-flight Next round boundary.</summary>
    public void Suspend() => IsRunning = false;
    public void Resume() { IsRunning = true; StopReason = null; }

    public SessionOutput? Advance(GameSession session)
    {
        if (!IsRunning) return null;
        if (!session.IsObserverMode) { Pause(); return null; }
        if (StopAtTurn is { } already && session.State.Calendar.TurnIndex >= already) { Pause(); return null; }
        var before = session.State;
        var output = session.Submit("observer-step");
        var after = session.State;
        if (output.ShouldExit || (before.ActiveSeatIndex == after.ActiveSeatIndex && before.Calendar == after.Calendar))
        {
            Pause();
            StopReason = "Observation stopped: no further nation turn is available.";
        }
        else if (StopAtTurn is { } turn && after.Calendar.TurnIndex >= turn) Suspend();
        return output;
    }
}
