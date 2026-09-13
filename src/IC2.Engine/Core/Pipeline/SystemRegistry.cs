using System.Reflection;

namespace IC2.Engine.Core;

/// <summary>One registered <see cref="IGameSystem"/>, as the assembly scan found it.</summary>
/// <param name="Id">The system's declared, globally unique id, and the name of its random stream.</param>
/// <param name="Phase">The phase it runs in.</param>
/// <param name="Order">Its declared position within that phase.</param>
/// <param name="ImplementationType">The class that declared it.</param>
/// <param name="Instance">The single shared, stateless instance.</param>
public sealed record RegisteredSystem(
    string Id,
    TurnPhase Phase,
    int Order,
    Type ImplementationType,
    IGameSystem Instance);

/// <summary>One registered quarter-boundary subscriber.</summary>
/// <param name="Id">The subscriber's declared, globally unique id, and the name of its random stream.</param>
/// <param name="Order">Its declared position among subscribers.</param>
/// <param name="ImplementationType">The class that declared it.</param>
/// <param name="Instance">The single shared, stateless instance.</param>
public sealed record RegisteredQuarterBoundaryHandler(
    string Id,
    int Order,
    Type ImplementationType,
    IQuarterBoundaryHandler Instance);

/// <summary>One registered command handler.</summary>
/// <param name="CommandType">The command type it claims.</param>
/// <param name="ImplementationType">The class that declared it.</param>
/// <param name="Instance">The single shared, stateless instance.</param>
public sealed record RegisteredCommandHandler(
    Type CommandType,
    Type ImplementationType,
    ICommandHandler Instance);

/// <summary>
/// Everything the engine discovered by scanning assemblies for registration attributes: systems,
/// quarter-boundary subscribers and command handlers.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the central registration list that
/// <c>docs/build-orchestration-plan.md</c> §2.3 names as one of the three files that would otherwise be
/// edited by nearly every task. Adding a system, a subscriber or a command handler touches only the file
/// that declares it.
/// </para>
/// <para>
/// The registry is built once and is immutable afterwards. Its ordering is total and independent of scan
/// order — see <see cref="AssemblyScan"/> — so the same set of assemblies always yields the same
/// pipeline, on any machine and after any rebuild.
/// </para>
/// </remarks>
public sealed class SystemRegistry
{
    private readonly Dictionary<Type, RegisteredCommandHandler> _handlersByCommandType;
    private readonly Dictionary<TurnPhase, RegisteredSystem[]> _systemsByPhase;

    private SystemRegistry(
        IReadOnlyList<RegisteredSystem> systems,
        IReadOnlyList<RegisteredQuarterBoundaryHandler> quarterBoundaryHandlers,
        IReadOnlyList<RegisteredCommandHandler> commandHandlers)
    {
        Systems = systems;
        QuarterBoundaryHandlers = quarterBoundaryHandlers;
        CommandHandlers = commandHandlers;

        _handlersByCommandType = new Dictionary<Type, RegisteredCommandHandler>();
        foreach (var handler in commandHandlers)
        {
            _handlersByCommandType.Add(handler.CommandType, handler);
        }

        _systemsByPhase = new Dictionary<TurnPhase, RegisteredSystem[]>();
        foreach (var phase in TurnPhases.InOrder)
        {
            _systemsByPhase.Add(phase, systems.Where(system => system.Phase == phase).ToArray());
        }
    }

    /// <summary>Every registered system, in execution order: phase, then declared order, then id.</summary>
    public IReadOnlyList<RegisteredSystem> Systems { get; }

    /// <summary>Every quarter-boundary subscriber, in execution order: declared order, then id.</summary>
    public IReadOnlyList<RegisteredQuarterBoundaryHandler> QuarterBoundaryHandlers { get; }

    /// <summary>Every command handler, ordered by command type name.</summary>
    public IReadOnlyList<RegisteredCommandHandler> CommandHandlers { get; }

    /// <summary>An empty registry — a coordinator built on it runs every phase and does nothing in each.</summary>
    public static SystemRegistry Empty { get; } = new(
        Array.Empty<RegisteredSystem>(),
        Array.Empty<RegisteredQuarterBoundaryHandler>(),
        Array.Empty<RegisteredCommandHandler>());

    /// <summary>Scans the engine's own assembly. What a shipped build uses.</summary>
    public static SystemRegistry FromEngineAssembly() => FromAssemblies(typeof(SystemRegistry).Assembly);

    /// <inheritdoc cref="FromAssemblies(IEnumerable{Assembly}, Func{Type, bool})"/>
    public static SystemRegistry FromAssemblies(params Assembly[] assemblies) =>
        FromAssemblies(assemblies, typeFilter: null);

    /// <summary>
    /// Builds a registry from every registration attribute found in <paramref name="assemblies"/>.
    /// </summary>
    /// <param name="assemblies">
    /// The assemblies to scan. Order does not matter and duplicates are ignored.
    /// </param>
    /// <param name="typeFilter">
    /// An optional predicate over declaring types, applied before registration. A shipped build passes
    /// nothing; a test uses it to scope a scan to its own fixtures without giving up attribute-only
    /// registration.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Two registrations share an id, two handlers claim the same command type, a registered class does
    /// not implement the interface its attribute implies, or one cannot be constructed.
    /// </exception>
    public static SystemRegistry FromAssemblies(IEnumerable<Assembly> assemblies, Func<Type, bool>? typeFilter)
    {
        var systems = new List<RegisteredSystem>();
        var quarterHandlers = new List<RegisteredQuarterBoundaryHandler>();
        var commandHandlers = new List<RegisteredCommandHandler>();

        foreach (var type in AssemblyScan.CandidateTypes(assemblies))
        {
            if (typeFilter is not null && !typeFilter(type))
            {
                continue;
            }

            if (type.GetCustomAttribute<GameSystemAttribute>(inherit: false) is { } systemAttribute)
            {
                RequireImplements<IGameSystem>(type, nameof(GameSystemAttribute));
                RequireDeclaredPhase(type, systemAttribute.Phase);
                systems.Add(new RegisteredSystem(
                    systemAttribute.Id,
                    systemAttribute.Phase,
                    systemAttribute.Order,
                    type,
                    AssemblyScan.Instantiate<IGameSystem>(type, "a game system")));
            }

            if (type.GetCustomAttribute<QuarterBoundaryHandlerAttribute>(inherit: false) is { } quarterAttribute)
            {
                RequireImplements<IQuarterBoundaryHandler>(type, nameof(QuarterBoundaryHandlerAttribute));
                quarterHandlers.Add(new RegisteredQuarterBoundaryHandler(
                    quarterAttribute.Id,
                    quarterAttribute.Order,
                    type,
                    AssemblyScan.Instantiate<IQuarterBoundaryHandler>(type, "a quarter-boundary handler")));
            }

            if (type.GetCustomAttribute<CommandHandlerAttribute>(inherit: false) is not null)
            {
                RequireImplements<ICommandHandler>(type, nameof(CommandHandlerAttribute));
                var instance = AssemblyScan.Instantiate<ICommandHandler>(type, "a command handler");
                commandHandlers.Add(new RegisteredCommandHandler(instance.CommandType, type, instance));
            }
        }

        systems.Sort(static (left, right) =>
        {
            var byPhase = TurnPhases.PositionOf(left.Phase).CompareTo(TurnPhases.PositionOf(right.Phase));
            if (byPhase != 0)
            {
                return byPhase;
            }

            var byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Id, right.Id);
        });

        quarterHandlers.Sort(static (left, right) =>
        {
            var byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Id, right.Id);
        });

        commandHandlers.Sort(static (left, right) =>
            string.CompareOrdinal(left.CommandType.FullName, right.CommandType.FullName));

        RequireDistinctIds(systems.Select(s => (s.Id, s.ImplementationType)), "game system");
        RequireDistinctIds(quarterHandlers.Select(h => (h.Id, h.ImplementationType)), "quarter-boundary handler");
        RequireDistinctCommandTypes(commandHandlers);

        return new SystemRegistry(systems, quarterHandlers, commandHandlers);
    }

    /// <summary>The systems registered in one phase, in execution order. Empty if none are.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is not a declared phase.</exception>
    public IReadOnlyList<RegisteredSystem> InPhase(TurnPhase phase) =>
        _systemsByPhase.TryGetValue(phase, out var inPhase)
            ? inPhase
            : throw new ArgumentOutOfRangeException(nameof(phase), phase, "Not a declared turn phase.");

    /// <summary>The handler claiming <paramref name="commandType"/>, or <see langword="null"/>.</summary>
    public RegisteredCommandHandler? HandlerFor(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        return _handlersByCommandType.TryGetValue(commandType, out var handler) ? handler : null;
    }

    /// <summary>
    /// Rejects a system declaring a phase that is not in <see cref="TurnPhases.InOrder"/>.
    /// </summary>
    /// <remarks>
    /// <c>[GameSystem((TurnPhase)0, "x.y")]</c> compiles — C# does not constrain an enum argument to its
    /// declared members — and without this check such a system would simply never run: the phase has no
    /// bucket, so nothing would ever look for it. A pipeline that silently omits a system is the worst
    /// possible failure mode for a build where 25 tasks each add one.
    /// </remarks>
    private static void RequireDeclaredPhase(Type type, TurnPhase phase)
    {
        foreach (var declared in TurnPhases.InOrder)
        {
            if (declared == phase)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"'{type.FullName}' declares phase '{(int)phase}', which is not one of the declared turn "
            + $"phases ({string.Join(", ", TurnPhases.InOrder)}). The phase list is closed; adding one is "
            + "an escalation, not a cast.");
    }

    private static void RequireImplements<TInterface>(Type type, string attributeName)
    {
        if (!typeof(TInterface).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' carries [{attributeName}] but does not implement "
                + $"{typeof(TInterface).Name}.");
        }
    }

    private static void RequireDistinctIds(IEnumerable<(string Id, Type Type)> registrations, string kind)
    {
        // A linear scan over the already-sorted list: ordered, so the message names the same pair every
        // time rather than whichever one a hash set happened to surface first.
        var seen = new List<(string Id, Type Type)>();
        foreach (var registration in registrations)
        {
            foreach (var earlier in seen)
            {
                if (string.Equals(earlier.Id, registration.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Two {kind} registrations share the id '{registration.Id}': "
                        + $"'{earlier.Type.FullName}' and '{registration.Type.FullName}'. Ids are global.");
                }
            }

            seen.Add(registration);
        }
    }

    private static void RequireDistinctCommandTypes(IReadOnlyList<RegisteredCommandHandler> handlers)
    {
        for (var i = 1; i < handlers.Count; i++)
        {
            if (handlers[i].CommandType == handlers[i - 1].CommandType)
            {
                throw new InvalidOperationException(
                    $"Two handlers claim the command '{handlers[i].CommandType.FullName}': "
                    + $"'{handlers[i - 1].ImplementationType.FullName}' and "
                    + $"'{handlers[i].ImplementationType.FullName}'. A command has exactly one handler.");
            }
        }
    }
}
