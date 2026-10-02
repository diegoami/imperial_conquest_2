using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// Which CLI verb <see cref="IC2.Engine.Presentation.GameSession"/>'s own <c>Submit</c> switch routes to
/// which <see cref="ICommand.Kind"/>, and which kinds have no verb at all or can never succeed — an
/// <strong>independently maintained mirror</strong> of
/// <c>tests/IC2.Engine.Tests/Presentation/CommandCoverageTests.cs</c>'s own curated map. That file's own
/// remarks explain why it is curated rather than parsed ("<c>GameSession</c>'s own <c>Submit</c> is the
/// actual source of truth ... built once, here, by reading that switch and
/// <c>GameSession.Commands.cs</c> directly"); this class exists only because
/// <c>godot/Checks/OrderCoverageCheck.cs</c> (a Godot scene script) cannot reference the xunit test
/// assembly that carries the original — <c>godot/IC2.MapViewer.csproj</c> is deliberately outside
/// <c>IC2.sln</c> (needs the Godot SDK CI does not install), and <c>tests/IC2.Engine.Tests.csproj</c> is
/// a plain xunit project with no reference back the other way. A rename or an added command type has to
/// be applied to both copies by hand; each one's own coverage test (this file's own reflection sweep in
/// <c>OrderCoverageCheck</c>, and <c>CommandCoverageTests</c> itself) is what catches a copy that falls
/// behind.
/// </summary>
public static class CommandVerbCatalog
{
    /// <summary>Every <see cref="ICommand"/>-implementing type in the engine assembly — the same
    /// reflection <c>CommandCoverageTests.AllCommandTypes</c> uses.</summary>
    public static IReadOnlyList<Type> AllCommandTypes { get; } = typeof(ICommand).Assembly
        .GetTypes()
        .Where(t => !t.IsInterface && !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>Three AI-internal command types with no CLI verb at all — an AI seat's own diplomacy
    /// phase dispatches them directly; no human-facing verb was ever meant to reach them.</summary>
    public static readonly IReadOnlySet<string> NoCliVerbByDesign = new HashSet<string>(StringComparer.Ordinal)
    {
        "diplomacy.ai-form-alliance",
        "diplomacy.ai-form-trade",
        "diplomacy.ai-swap-trade-partner",
    };

    /// <summary>Three command types that do have a real verb but that no shipped scenario can ever make
    /// succeed — keyed to the same issues <c>CommandCoverageTests.ConfirmedUnreachable</c> is: #229/#457
    /// (the mercenary pool is always empty) and #453 (no cell is ever both a legal army position and a
    /// legal fleet position). A rejection for one of these is the expected, correct outcome, not a
    /// failure.</summary>
    public static readonly IReadOnlySet<string> ConfirmedUnreachable = new HashSet<string>(StringComparer.Ordinal)
    {
        "recruitment.hire-mercenary",
        "naval.embark-army",
        "naval.disembark-army",
    };

    /// <summary>Verbs that print nothing to accept or reject — no outcome to judge.</summary>
    public static readonly IReadOnlySet<string> ReadOnlyVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "end", "status", "news", "balance", "quit", "help", "armies", "cities", "map",
    };

    /// <summary>This catalogue's own curated verb map — see this class's own remarks for why it is
    /// manually maintained rather than parsed from <c>GameSession.cs</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> VerbByKind = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["armies.disband-army"] = "disband-army",
        ["armies.join-armies"] = "join-armies",
        ["armies.join-units"] = "join-units",
        ["armies.split-army"] = "split-army",
        ["battle.attack-army"] = "attack-army",
        ["battle.attack-fleet"] = "attack-fleet",
        ["battle.besiege-city"] = "besiege-city",
        ["city.order"] = "order-city",
        ["diplomacy.accept-peace-treaty"] = "peace-yes",
        ["diplomacy.accept-pending-offer"] = "accept-offer",
        ["diplomacy.declare-war"] = "declare-war",
        ["diplomacy.make-peace"] = "make-peace",
        ["diplomacy.propose-alliance"] = "propose-alliance",
        ["diplomacy.propose-trade"] = "propose-trade",
        ["economy.buy-supply"] = "buy",
        ["economy.set-tax"] = "set-tax",
        ["economy.transfer-money"] = "transfer-money",
        ["movement.move-army"] = "move",
        ["naval.buy-fleet-supply"] = "buy-fleet-supply",
        ["naval.disembark-army"] = "disembark-army",
        ["naval.embark-army"] = "embark-army",
        ["naval.fleet-to-fleet-transfer"] = "fleet-transfer",
        ["naval.join-fleets"] = "join-fleets",
        ["naval.move-fleet"] = "move-fleet",
        ["naval.order-fleet"] = "order-fleet",
        ["naval.repair-fleet"] = "repair-fleet",
        ["naval.scuttle-fleet"] = "scuttle-fleet",
        ["naval.split-fleet"] = "split-fleet",
        ["recruitment.hire-mercenary"] = "hire-mercenary",
        ["recruitment.mobilize-recruit-slot"] = "mobilize",
        ["recruitment.recruit-standing-unit"] = "recruit-standing",
    };

    /// <summary>The inverse of <see cref="VerbByKind"/> — one verb, one kind, so the lookup direction a
    /// caller needs (kind-by-verb, the same direction <c>SuccessScriptTests</c>-style outcome judging
    /// needs) never drifts from the curated original above.</summary>
    public static readonly IReadOnlyDictionary<string, string> KindByVerb =
        VerbByKind.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Reads <see cref="ICommand.Kind"/> off a throwaway instance of <paramref name="commandType"/>
    /// — the same reflection-based construction <c>CommandCoverageTests.KindOf</c> uses, since
    /// <c>Kind</c> never depends on argument values, only on the type.</summary>
    public static string KindOf(Type commandType)
    {
        var constructor = commandType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(PlaceholderFor).ToArray();
        var instance = (ICommand)constructor.Invoke(arguments);
        return instance.Kind;
    }

    private static object? PlaceholderFor(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (type == typeof(string))
        {
            return "placeholder";
        }

        if (type == typeof(int))
        {
            return 0;
        }

        if (type == typeof(int?))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueList<>))
        {
            var emptyProperty = type.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static)!;
            return emptyProperty.GetValue(null);
        }

        throw new NotSupportedException(
            $"CommandVerbCatalog.PlaceholderFor has no case for parameter type '{type}' "
            + $"(parameter '{parameter.Name}'). Add one rather than skip the command it belongs to.");
    }
}
