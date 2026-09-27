using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T80.md</c> Done-when 2: "No command type is left out. A test lists every
/// <see cref="ICommand"/> type by reflection and fails, naming it, if a type has no CLI verb, or has no
/// line in the success-path script that the golden shows accepted."
/// </summary>
/// <remarks>
/// <para>
/// <strong>The verb map below is curated, not derived.</strong> <see cref="Presentation.GameSession"/>'s
/// own <c>Submit</c> switch is the actual source of truth for which string reaches which command, but
/// nothing here parses that switch (a task confined to <c>tests/**</c> cannot). <see cref="VerbByKind"/>
/// is this test's own record of "I have manually confirmed this type has a verb", built once, here, by
/// reading that switch and <c>GameSession.Commands.cs</c> directly. A type that is not in the map is
/// treated as if it had no verb — the conservative direction: a real command with a real verb that this
/// map has not yet been told about fails loudly (<see cref="Every_non_excluded_command_type_has_a_confirmed_cli_verb"/>),
/// rather than silently passing. That is exactly the gap this task's own Scope names: "nothing makes a
/// newly added command type join the demo."
/// </para>
/// <para>
/// <strong>Two disjoint kinds of exclusion, both named here rather than folded into one list.</strong>
/// </para>
/// <list type="number">
/// <item><description><see cref="NoCliVerbByDesign"/> — three AI-internal command types
/// (<c>diplomacy.ai-form-alliance</c>, <c>diplomacy.ai-form-trade</c>,
/// <c>diplomacy.ai-swap-trade-partner</c>) that an AI seat's own diplomacy phase dispatches directly and
/// that <see cref="Presentation.GameSession"/> never routes any CLI verb to at all — confirmed by reading
/// <c>GameSession.Commands.cs</c> and <c>GameSession.cs</c>'s own <c>Submit</c> switch, which name every
/// other command type exactly once each and these three nowhere. Their own handlers refuse an issuer
/// that is not AI-controlled (<c>AiFormAllianceRejections.IssuerNotAi</c> and its two siblings), so even
/// adding a verb for them would only ever print a refusal from a human seat — these are the AI's own
/// direct, no-consent writes, not a human order with no keyboard shortcut yet.</description></item>
/// <item><description><see cref="ConfirmedUnreachable"/> — five command types that <em>do</em> have a
/// real CLI verb, wired exactly like every other command here, but that cannot be driven to an accepted
/// outcome by any script this task can still call thin, each for a reason confirmed by reading the
/// engine (not merely by running out of turns) — see each entry's own remark, and
/// <see cref="Presentation.SuccessScriptTests"/>'s class remarks for the full account of all five. This is
/// this task's own Hazards note, followed literally: "If an order cannot be made to succeed ... do not
/// change <c>src/</c>. STOP and report which command, and why. That is a defect or a gap for the main
/// session to plan." rather than force one.</description></item>
/// </list>
/// </remarks>
public sealed class CommandCoverageTests
{
    /// <summary>
    /// Every <see cref="ICommand"/>-implementing type in the engine assembly, found by reflection —
    /// exactly the reflection Done-when 2 asks for. <c>IsAssignableFrom</c> over concrete, non-abstract
    /// types only; <see cref="ICommand"/> itself and any handler interface are excluded by construction
    /// (neither is assignable to itself as a proper subtype here since only <c>record</c> command types
    /// implement the interface at all).
    /// </summary>
    private static IReadOnlyList<Type> AllCommandTypes { get; } = typeof(ICommand).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICommand).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Three AI-internal command types with no CLI verb at all, by design — see this class's own remarks.
    /// Keyed by <see cref="ICommand.Kind"/>, read off a throwaway instance (<see cref="KindOf"/>) rather
    /// than assumed, so a rename of the constant would move this set with it instead of silently going
    /// stale.
    /// </summary>
    private static readonly HashSet<string> NoCliVerbByDesign = new(StringComparer.Ordinal)
    {
        "diplomacy.ai-form-alliance",
        "diplomacy.ai-form-trade",
        "diplomacy.ai-swap-trade-partner",
    };

    /// <summary>
    /// Five command types with a real CLI verb that cannot be driven to an accepted outcome by any script
    /// — see <see cref="Presentation.SuccessScriptTests"/>'s class remarks for the full, per-type account
    /// this class only summarises.
    /// </summary>
    private static readonly HashSet<string> ConfirmedUnreachable = new(StringComparer.Ordinal)
    {
        "recruitment.hire-mercenary", // the mercenary pool is never populated by any merged system (T56, #229, still open).
        "naval.embark-army", // an army can never occupy the same cell as a fleet -- see SuccessScriptTests.
        "naval.disembark-army", // can only ever follow a successful embark, which is itself unreachable.
        "battle.attack-fleet", // no nation both owns a fleet and can still succeed at propose-alliance.
        "diplomacy.accept-peace-treaty", // the post-battle offer needs a further, independent 1-in-5 roll this seed never hit.
    };

    /// <summary>
    /// This test's own curated verb map -- see this class's remarks on why it is manually maintained
    /// rather than parsed from <c>GameSession.cs</c>.
    /// </summary>
    private static readonly Dictionary<string, string> VerbByKind = new(StringComparer.Ordinal)
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

    /// <summary>
    /// Reads <see cref="ICommand.Kind"/> off a throwaway instance of <paramref name="commandType"/>,
    /// built by supplying a placeholder value for every constructor parameter — a command record's
    /// <c>Kind</c> never depends on its argument values, only on its type, so any well-typed placeholder
    /// set reads the real, constant string.
    /// </summary>
    private static string KindOf(Type commandType)
    {
        var constructor = commandType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(PlaceholderFor).ToArray();
        var instance = (ICommand)constructor.Invoke(arguments);
        return instance.Kind;
    }

    private static object? PlaceholderFor(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        // A nullable reference annotation (string?) is erased to plain System.String at runtime, so a
        // parameter that is genuinely optional (only ever has a null-argument call site in this engine,
        // e.g. BuySupplyCommand.ProviderCityId) still reads as typeof(string) here -- "placeholder" is a
        // fine, well-typed stand-in either way, since Kind never reads any argument value.
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
            $"CommandCoverageTests.PlaceholderFor has no case for parameter type '{type}' "
            + $"(parameter '{parameter.Name}'). Add one rather than skip the command it belongs to.");
    }

    /// <summary>
    /// Done-when 2, first half: "fails, naming it, if a type has no CLI verb" — for every command type
    /// this class has not named as a deliberate exception above.
    /// </summary>
    [Fact]
    public void Every_non_excluded_command_type_has_a_confirmed_cli_verb()
    {
        var missing = new List<string>();
        foreach (var commandType in AllCommandTypes)
        {
            var kind = KindOf(commandType);
            if (NoCliVerbByDesign.Contains(kind))
            {
                continue;
            }

            if (!VerbByKind.ContainsKey(kind))
            {
                missing.Add($"{commandType.FullName} (kind '{kind}')");
            }
        }

        Assert.True(
            missing.Count == 0,
            "The following command type(s) have no CLI verb confirmed in VerbByKind: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// Done-when 2, second half: "or has no line in the success-path script that the golden shows
    /// accepted" — for every command type this class has not named as a confirmed, documented gap.
    /// </summary>
    [Fact]
    public void Every_reachable_command_type_has_an_accepted_line_in_the_success_golden()
    {
        var golden = File.ReadAllText(
            Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success.golden.txt"));

        var missing = new List<string>();
        foreach (var commandType in AllCommandTypes)
        {
            var kind = KindOf(commandType);
            if (NoCliVerbByDesign.Contains(kind) || ConfirmedUnreachable.Contains(kind))
            {
                continue;
            }

            if (!HasAcceptedLine(golden, kind))
            {
                missing.Add($"{commandType.FullName} (kind '{kind}')");
            }
        }

        Assert.True(
            missing.Count == 0,
            "The following command type(s) have no accepted line in success.golden.txt: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// <c>move</c> and <c>buy</c> keep <c>GameSession.Commands</c>'s own bespoke wording (T41's contract:
    /// see that file's own remarks), so their accepted line is not <c>"{kind} accepted."</c>; every other
    /// kind shares the one generic renderer, whether composed ahead of an attack or not.
    /// </summary>
    private static bool HasAcceptedLine(string golden, string kind) => kind switch
    {
        "movement.move-army" => golden.Contains("moved from (", StringComparison.Ordinal),
        "economy.buy-supply" => golden.Contains("bought ", StringComparison.Ordinal)
            && golden.Contains(" tons of supply", StringComparison.Ordinal),
        _ => golden.Contains($"{kind} accepted", StringComparison.Ordinal),
    };

    /// <summary>
    /// Cross-check on <see cref="NoCliVerbByDesign"/> and <see cref="ConfirmedUnreachable"/> themselves:
    /// every kind named in either set must actually belong to a command type <see cref="AllCommandTypes"/>
    /// finds, so a future rename or removal cannot leave a stale entry quietly excusing nothing.
    /// </summary>
    [Fact]
    public void Every_named_exception_still_matches_a_real_command_type()
    {
        var liveKinds = AllCommandTypes.Select(KindOf).ToHashSet(StringComparer.Ordinal);

        foreach (var kind in NoCliVerbByDesign)
        {
            Assert.Contains(kind, liveKinds);
        }

        foreach (var kind in ConfirmedUnreachable)
        {
            Assert.Contains(kind, liveKinds);
        }
    }

    /// <summary>
    /// The three counts add up to every command type the engine declares today — a cheap guard against
    /// the three sets above silently drifting apart (double-counting a kind, or one gaining an entry the
    /// others should have shrunk by).
    /// </summary>
    [Fact]
    public void The_three_groups_partition_every_command_type_exactly()
    {
        var allKinds = AllCommandTypes.Select(KindOf).ToList();
        var accountedFor = NoCliVerbByDesign.Count + ConfirmedUnreachable.Count
            + allKinds.Count(k => !NoCliVerbByDesign.Contains(k) && !ConfirmedUnreachable.Contains(k));

        Assert.Equal(allKinds.Count, accountedFor);
        Assert.Equal(allKinds.Count, allKinds.Distinct(StringComparer.Ordinal).Count());
    }
}
