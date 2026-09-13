namespace IC2.Engine.Core;

/// <summary>
/// Marks an <see cref="IGameSystem"/> for assembly-scanned registration, declaring which phase it runs
/// in and where in that phase it sits.
/// </summary>
/// <remarks>
/// The attribute <em>is</em> the registration. <c>docs/build-orchestration-plan.md</c> §2.3 lists a
/// central system-registration list as one of the three files that would otherwise be edited by nearly
/// every task and conflict constantly; this replaces it.
/// </remarks>
/// <example>
/// <code>
/// [GameSystem(TurnPhase.CityTick, "economy.tribute-growth", Order = 20)]
/// public sealed class TributeGrowthSystem : IGameSystem
/// {
///     public GameState Execute(SystemContext context) => context.State;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GameSystemAttribute : Attribute
{
    /// <summary>Declares a system's phase and its stable id.</summary>
    /// <param name="phase">The phase this system runs in.</param>
    /// <param name="id">
    /// A stable, globally unique id in <c>area.name</c> form, for example <c>"economy.quarterly-upkeep"</c>.
    /// </param>
    public GameSystemAttribute(TurnPhase phase, string id)
    {
        Phase = phase;
        Id = SystemId.Validated(id, nameof(id));
    }

    /// <summary>The phase this system runs in.</summary>
    public TurnPhase Phase { get; }

    /// <summary>
    /// The system's stable id.
    /// </summary>
    /// <remarks>
    /// Deliberately not defaulted to the type name. The id is also the name of the system's random
    /// sub-stream (<see cref="IRng.ForStream"/>), so if it were derived from the class name, renaming a
    /// C# class would silently change every roll that system has ever made and invalidate every golden
    /// fixture written against it. Spelling it out makes that a deliberate act.
    /// </remarks>
    public string Id { get; }

    /// <summary>
    /// Position within the phase; lower runs first. Systems that do not care leave it at zero, and ties
    /// are broken by <see cref="Id"/> so the order is total and stable regardless of scan order.
    /// </summary>
    public int Order { get; init; }
}

/// <summary>
/// The naming rule shared by every attribute-declared id in the engine seam — systems, quarter-boundary
/// handlers, command kinds and rejection codes.
/// </summary>
/// <remarks>
/// A single convention, enforced at declaration time, is what keeps ~25 independently written tasks from
/// producing <c>EconomyUpkeep</c>, <c>economy_upkeep</c> and <c>Economy.Upkeep</c> for the same idea. The
/// form is <c>segment.segment[.segment…]</c>, each segment lowercase ASCII letters, digits or hyphens.
/// </remarks>
public static class SystemId
{
    /// <summary>Returns <paramref name="id"/> if it is well formed, and throws if it is not.</summary>
    /// <param name="id">The candidate id.</param>
    /// <param name="parameterName">The argument name to report in the exception.</param>
    /// <exception cref="ArgumentException">The id is null, empty, or not in <c>area.name</c> form.</exception>
    public static string Validated(string id, string parameterName)
    {
        if (!IsWellFormed(id))
        {
            throw new ArgumentException(
                $"'{id}' is not a valid id. Use lowercase dotted segments of letters, digits and hyphens, "
                + "with at least two segments, for example 'economy.quarterly-upkeep'.",
                parameterName);
        }

        return id;
    }

    /// <summary>Whether a string is a well-formed dotted id.</summary>
    public static bool IsWellFormed(string? id)
    {
        if (string.IsNullOrEmpty(id) || id[0] == '.' || id[^1] == '.')
        {
            return false;
        }

        var segments = 1;
        var segmentLength = 0;
        foreach (var character in id)
        {
            if (character == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }

                segments++;
                segmentLength = 0;
                continue;
            }

            var allowed = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!allowed)
            {
                return false;
            }

            segmentLength++;
        }

        return segments >= 2 && segmentLength > 0;
    }
}
