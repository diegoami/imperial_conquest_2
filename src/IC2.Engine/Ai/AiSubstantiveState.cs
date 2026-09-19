using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>
/// The part of a <see cref="GameState"/> that a turn is judged to have <em>changed</em>: everything a
/// command or a system can act on, and none of the bookkeeping that moves whether anything happened or
/// not.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists rather than comparing whole states.</strong>
/// <c>docs/task-catalogue.md</c> T22 Done-when 1 fails a soak on a stall — "<em>a turn that issues no
/// command and changes no state twice in a row</em>". Compared whole, two states are never equal:
/// <see cref="GameState.ActiveSeatIndex"/> moves every turn (T06's seat rotation),
/// <see cref="GameState.RandomSeed"/> moves on every accepted command and every phase
/// (<c>RngStreams</c>), <see cref="GameState.Calendar"/> moves every round tick, and
/// <see cref="GameState.NewsLog"/> gains a header every round (T42). A stall check against the whole
/// state would therefore never fire — it would be a green assertion that cannot go red, which this
/// project treats as no assertion at all (<c>docs/build-process.md</c> §4.2 gate 5, "a test that would
/// still pass if the behaviour were deleted").
/// </para>
/// <para>
/// So "changed no state" means these seven members: the nations, the cities, the armies, the fleets, the
/// mercenary pool, the relation matrix and the pending offer. Each is a value-equal
/// <see cref="ValueList{T}"/> or record, so equality here is structural and exact — a single talent, a
/// single ton or a single move spent anywhere in the game counts as a change, and nothing else does.
/// </para>
/// <para>
/// The same comparison does double duty inside one turn:
/// <see cref="AiTurn"/> stops a turn early if an accepted command left all seven untouched, which is what
/// keeps a scoring bug that re-proposes an inert command from spinning to the action cap.
/// </para>
/// </remarks>
/// <param name="Nations">Treasuries, unity, wealth, tax base, recruitment queues, elimination.</param>
/// <param name="Cities">Ownership, loyalty, supply, fortification, population, garrison, siege flag.</param>
/// <param name="Armies">Position, moves, morale, purse, supply, units, embarkation.</param>
/// <param name="Fleets">Position, moves, ships, condition, purse, supply, construction, carried army.</param>
/// <param name="MercenaryPool">The hireable pool.</param>
/// <param name="Relations">The symmetric relation matrix, cooldowns included.</param>
/// <param name="PendingOffer">The one pending diplomatic offer, or none.</param>
public sealed record AiSubstantiveState(
    ValueList<NationState> Nations,
    ValueList<CityState> Cities,
    ValueList<ArmyState> Armies,
    ValueList<FleetState> Fleets,
    ValueList<MercenaryPoolSlot> MercenaryPool,
    DiplomaticRelations Relations,
    PendingDiplomaticOffer? PendingOffer)
{
    /// <summary>Takes the substantive fingerprint of a state.</summary>
    /// <param name="state">The state to read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static AiSubstantiveState Of(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new AiSubstantiveState(
            state.Nations,
            state.Cities,
            state.Armies,
            state.Fleets,
            state.MercenaryPool,
            state.Relations,
            state.PendingOffer);
    }

    /// <summary>Whether two states are substantively identical.</summary>
    public static bool AreEquivalent(GameState a, GameState b) => Of(a) == Of(b);
}
