namespace IC2.Engine.Presentation;

/// <summary>
/// Why a human seat fell — the branch <c>THumanFalls_InitializeForm</c> reads, in the original's own
/// priority order — <c>docs/tasks/T138.md</c>, "The engine exposes the falls".
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompile]</strong>, <c>THumanFalls_InitializeForm</c> @ <c>0x00455e38</c>
/// (:56391–56404): total conquest first, the hard end year second, then (only when the seat was
/// <em>not</em> conquered) unpopularity or unpaid upkeep, and conquered otherwise. The names are this
/// task's own; only the order and the branches are the original's.
/// </remarks>
public enum SeatFallReason
{
    /// <summary>Holding every city on the map — the original's sole victory, announced by this window.</summary>
    AllCities,

    /// <summary>The year reached <see cref="Model.VictoryRules.HardEndYearBc"/>.</summary>
    HardEndYear,

    /// <summary>Unity below <see cref="Model.EconomyRules.DebtUnityThreshold"/> — the army overthrew
    /// the leader on its own.</summary>
    Unpopularity,

    /// <summary>In debt (<see cref="Economy.Deposition.InDebt"/>) with unity still at or above the
    /// threshold — the army was not paid.</summary>
    Unpaid,

    /// <summary>Conquered outright — its last city taken by another nation, named by
    /// <see cref="ConquerorNationId"/>.</summary>
    Conquered,
}

/// <summary>
/// One human seat's fall, as the engine reports it for one <see cref="GameSession.Submit"/> call —
/// <c>docs/tasks/T138.md</c>, "The engine exposes the falls": the seat, the reason, the conqueror when
/// there is one, the text <see cref="GameSession"/> already printed for it, and the nation's end figures
/// (population, city count, treasury) taken at the fall.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only data, and only figures already in the state.</strong> Nothing here is a new
/// <see cref="Model.NationState"/> or <see cref="Model.GameState"/> field and nothing changes the save
/// format; the start figures the game-end window shows beside these come from
/// <see cref="Model.NationState.PopulationAtStart"/> and its siblings, which
/// <see cref="Model.GameStateFactory"/> (and <c>OriginalSaveImporter</c>) already set. The end figures are
/// snapshotted into this record so a later fall in the same call cannot shift an earlier one's table.
/// </para>
/// <para>
/// <strong>The end figures are the nation <em>at the fall</em>, before the deposition's own write.</strong>
/// <c>TPremierForm_HumanLeaderFalls</c> opens the window (<c>dump :50787</c>) before
/// <c>FUN_0044C8F0</c>'s unity/treasury/relation writes (<c>:50795–50798</c>), so a seat deposed for
/// unpaid upkeep reports the treasury it actually held, not the post-fall credit — see
/// <c>GameSession.DepositActiveHumanSeatIfItShouldFallAtTurnStart</c>'s own remarks.
/// </para>
/// </remarks>
/// <param name="NationId">The fallen nation's id.</param>
/// <param name="Reason">Which of the original's branches this fall took.</param>
/// <param name="ConquerorNationId">
/// The nation that conquered it, or <see langword="null"/> for every reason but
/// <see cref="SeatFallReason.Conquered"/> (which is the only branch whose text names another nation).
/// </param>
/// <param name="Text">
/// The exact line <see cref="GameSession"/> printed for this fall when it happened — one of
/// <c>HumanLeaderFallsMessage</c>'s five strings, character for character, misspellings included.
/// </param>
/// <param name="EndPopulation">The nation's population at the fall.</param>
/// <param name="EndCityCount">
/// How many cities of <see cref="Model.GameState.Cities"/> it owned at the fall (0 for a conquest, whose
/// elimination is the loss of its last city).
/// </param>
/// <param name="EndTreasury">The nation's treasury at the fall.</param>
public sealed record SeatFall(
    string NationId,
    SeatFallReason Reason,
    string? ConquerorNationId,
    string Text,
    int EndPopulation,
    int EndCityCount,
    int EndTreasury);
