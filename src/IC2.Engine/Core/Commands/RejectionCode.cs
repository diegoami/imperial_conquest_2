namespace IC2.Engine.Core;

/// <summary>
/// Why a command was refused: a stable, machine-readable code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a string-keyed value type rather than an enum.</strong> Twenty later tasks each add their
/// own refusal reasons — an over-cap recruitment, an embarkation past a fleet's capacity, a fortify order
/// on a besieged city, a repair away from an owned port. An <c>enum</c> would make every one of those
/// tasks edit the same file, which is the exact conflict
/// <c>docs/build-orchestration-plan.md</c> §2.3 is built to avoid, and it would also make the code
/// unstable across renumbering. A validated string key is open: a task declares its codes in its own
/// directory, as <c>static readonly</c> fields on its own class, and nothing shared changes.
/// </para>
/// <para>
/// <strong>Why not a bare string.</strong> The type keeps a rejection code from being confused with a
/// message, a command kind or an id at a call site, and it enforces the naming convention once, at
/// construction: <c>area.reason</c>, lowercase, as <see cref="SystemId"/> defines it. A UI can key
/// localized text off the code and a test can assert on it, neither of which survives free-form prose.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static class RecruitmentRejections
/// {
///     public static readonly RejectionCode OverArmyTroopCap = new("recruitment.over-army-troop-cap");
/// }
/// </code>
/// </example>
public readonly record struct RejectionCode
{
    /// <summary>Creates a rejection code.</summary>
    /// <param name="value">The code, in <c>area.reason</c> form.</param>
    /// <exception cref="ArgumentException">The code is not well formed.</exception>
    public RejectionCode(string value) => Value = SystemId.Validated(value, nameof(value));

    /// <summary>The code string.</summary>
    public string Value { get; }

    /// <summary>
    /// Whether this is <c>default(RejectionCode)</c> — the one way to hold a code that never went through
    /// the validating constructor, since C# does not let a struct suppress its default value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <inheritdoc/>
    public override string ToString() => IsEmpty ? "<no code>" : Value;
}

/// <summary>
/// The rejection codes the dispatch seam itself produces. Gameplay refusals belong to the task that owns
/// the rule, in that task's own directory — this list is deliberately short and is not a place for later
/// tasks to add to.
/// </summary>
public static class CoreRejections
{
    /// <summary>No handler is registered for the command's type.</summary>
    public static readonly RejectionCode NoHandler = new("command.no-handler");

    /// <summary>
    /// The command was issued by, or on behalf of, a nation that is not the active seat. Checked by the
    /// dispatcher rather than by every handler, because forgetting it is a whole class of bug.
    /// </summary>
    public static readonly RejectionCode NotActiveSeat = new("command.not-active-seat");

    /// <summary>The command names a nation the state does not contain.</summary>
    public static readonly RejectionCode UnknownNation = new("command.unknown-nation");

    /// <summary>The issuing nation has been eliminated and can no longer act.</summary>
    public static readonly RejectionCode NationEliminated = new("command.nation-eliminated");

    /// <summary>
    /// A system tried to issue a command, but its coordinator was built without a dispatcher. A wiring
    /// mistake, reported as a rejection rather than an exception so that it cannot take a turn down.
    /// </summary>
    public static readonly RejectionCode NoDispatcher = new("command.no-dispatcher");
}
