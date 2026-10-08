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
/// <item><description><see cref="ConfirmedUnreachable"/> — <strong>keyed exceptions, and nothing else</strong>
/// (docs/tasks/T80.md Done-when 2b, added 2026-09-27 after PR #452's review): exactly one command type
/// that <em>does</em> have a real CLI verb, wired exactly like every other command here, keyed to the
/// GitHub issue whose own task removes it, and named with its reason inline — see
/// <see cref="Presentation.SuccessScriptTests"/>'s class remarks for the full account. <c>attack-fleet</c>
/// and <c>peace-yes</c> — the two PR #452 originally reported as unreachable too — get no exception per
/// that same Done-when 2b: both have an accepted line (<c>success-fleet.golden.txt</c> and a
/// re-seeded <c>success.golden.txt</c> respectively), described in full in <c>SuccessScriptTests</c>. T93
/// removed the last two it had added for #453 (<c>embark-army</c>, <c>disembark-army</c>), which now have
/// accepted lines in <c>success.golden.txt</c>.</description></item>
/// </list>
/// </remarks>
public sealed class CommandCoverageTests
{
    /// <summary>
    /// Every <see cref="ICommand"/>-implementing type in the engine assembly, found by reflection —
    /// exactly the reflection Done-when 2 asks for. <c>IsAssignableFrom</c> over concrete types only;
    /// <see cref="ICommand"/> itself and any handler interface are excluded by <c>!IsInterface</c>, and
    /// there is no abstract command base to exclude further.
    /// </summary>
    /// <remarks>
    /// T80 rework round 1, N1/N4: this used to require <c>IsClass: true</c> too, on the premise that
    /// "only <c>record</c> command types implement the interface at all" — false, since
    /// <c>ICommandHandler&lt;TCommand&gt; where TCommand : ICommand</c> carries no <c>class</c>
    /// constraint, so a <c>record struct</c> (or any value type) command is legal and dispatchable, and
    /// the old filter silently dropped it from every set below — the exact "a newly added command type
    /// join(s) the demo" gap this task exists to close. Proved by mutation: a throwaway
    /// <c>readonly record struct</c> command with no verb, added under
    /// <c>src/IC2.Engine/Naval/Commands/</c> and reverted, made both coverage tests below fail, naming it,
    /// only after this filter was corrected — see the PR for the before/after.
    /// </remarks>
    private static IReadOnlyList<Type> AllCommandTypes { get; } = typeof(ICommand).Assembly
        .GetTypes()
        .Where(t => !t.IsInterface && !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t))
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
    /// docs/tasks/T80.md Done-when 2b: "The coverage test may exempt exactly these, each named in the
    /// test with its reason ... Each keyed exception names its issue. The task that closes that issue
    /// removes the exception and adds the command's line to the success script." One entry, and no more:
    /// <c>attack-fleet</c> and <c>peace-yes</c> are deliberately absent — both must succeed, and both do —
    /// and T93/#453 removed the <c>embark-army</c> and <c>disembark-army</c> entries by adding an accepted
    /// line for each to <c>success.txt</c> (see <see cref="Presentation.SuccessScriptTests"/>'s class
    /// remarks).
    /// </summary>
    private static readonly HashSet<string> ConfirmedUnreachable = new(StringComparer.Ordinal)
    {
        // Keyed to #229 (T56, the quarterly mercenary restock, still open). GameStateFactory.CreateInitial
        // hardcodes MercenaryPool empty for every scenario -- no world/scenario JSON field feeds it -- and
        // the only write to GameState.MercenaryPool anywhere in the engine is OriginalSaveImporter's (a
        // legacy-save import the CLI has no flag to reach). T56's own Scope line ("T22's soak fires the
        // hire in the first quarters") describes the ORIGINAL decompiled game, not this reimplementation:
        // AiEconomyPhase never calls HireMercenaryCommand at all (confirmed by grep), so no AI turn, in any
        // scenario, at any seed, can populate or drain the pool either. T56 removes this exception.
        "recruitment.hire-mercenary",
    };

    /// <summary>
    /// This test's own curated verb map -- see this class's remarks on why it is manually maintained
    /// rather than parsed from <c>GameSession.cs</c>. <c>internal</c> (T80 rework round 2, B3) so
    /// <see cref="Presentation.SuccessScriptTests"/> can build its own inverse (kind-by-verb) from the
    /// same single source, rather than keeping a second, driftable copy.
    /// </summary>
    internal static readonly Dictionary<string, string> VerbByKind = new(StringComparer.Ordinal)
    {
        ["armies.army-transfer"] = "army-transfer",
        ["armies.disband-army"] = "disband-army",
        ["armies.disband-unit"] = "disband-unit",
        ["armies.join-armies"] = "join-armies",
        ["armies.join-units"] = "join-units",
        ["armies.rename-unit"] = "rename-unit",
        ["armies.split-army"] = "split-army",
        ["armies.split-unit"] = "split-unit",
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
        ["recruitment.disband-recruitment-slot"] = "disband-slot",
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
    /// Done-when 2b widened this to two goldens (<c>success.golden.txt</c> carries every command except
    /// <c>attack-fleet</c>; <c>success-fleet.golden.txt</c> carries only <c>attack-fleet</c>) — a type
    /// passes if its accepted line shows up in either.
    /// </summary>
    /// <remarks>
    /// T80 rework round 1, B1 (blocking): this used to search the whole golden text for
    /// <c>"{kind} accepted"</c> as a bare substring, which also matches
    /// <c>diplomacy.declare-war accepted (composed ahead of the attack).</c> — a line
    /// <c>attack-army</c>/<c>besiege-city</c> print from their own block, not a <c>declare-war</c> script
    /// line's own outcome. So a golden with zero <c>declare-war</c> lines, but at least one composing
    /// attack, still "covered" <see cref="Diplomacy.Commands.DeclareWarCommand"/>. Fixed by attributing
    /// coverage to the exact <see cref="GoldenTranscriptBlocks.Block"/> the verb's own script line produced
    /// (<see cref="HasAcceptedLine"/>), so a composed declare-war inside an <c>attack-army</c> block can
    /// never satisfy <c>declare-war</c>'s own coverage. Proved by mutation: removing
    /// <c>declare-war armenia</c> from <c>success.txt</c> and regenerating the golden through the CLI now
    /// fails this test, naming <see cref="Diplomacy.Commands.DeclareWarCommand"/> — see the PR for the
    /// before/after.
    /// </remarks>
    [Fact]
    public void Every_reachable_command_type_has_an_accepted_line_in_the_success_golden()
    {
        var blocks = GoldenTranscriptBlocks.Parse(File.ReadAllText(
                Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success.golden.txt")))
            .Concat(GoldenTranscriptBlocks.Parse(File.ReadAllText(
                Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "success-fleet.golden.txt"))))
            .ToList();

        var missing = new List<string>();
        foreach (var commandType in AllCommandTypes)
        {
            var kind = KindOf(commandType);
            if (NoCliVerbByDesign.Contains(kind) || ConfirmedUnreachable.Contains(kind))
            {
                continue;
            }

            if (!VerbByKind.TryGetValue(kind, out var verb) || !HasAcceptedLine(blocks, kind, verb))
            {
                missing.Add($"{commandType.FullName} (kind '{kind}')");
            }
        }

        Assert.True(
            missing.Count == 0,
            "The following command type(s) have no accepted line, attributed to their own verb's script "
            + "line, in success.golden.txt or success-fleet.golden.txt: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Whether some block whose own prompt verb is <paramref name="verb"/> shows an accepted outcome for
    /// <paramref name="kind"/>. <c>move</c>, <c>buy</c> and <c>transfer-money</c> keep
    /// <c>GameSession.Commands</c>'s own bespoke wording (T41's contract; fix #710 for the last), so their
    /// accepted line is not <c>"{kind} accepted."</c>; every other kind shares the one generic renderer's
    /// exact, full-stop-terminated line — a composed declare-war line reads
    /// <c>"...accepted (composed ahead of the attack)."</c>, which does not contain
    /// <c>"...accepted."</c> as a substring, so it cannot false-positive a plain <c>declare-war</c> line
    /// even before the per-block verb attribution is taken into account (B1's own belt and suspenders).
    /// </summary>
    private static bool HasAcceptedLine(IReadOnlyList<GoldenTranscriptBlocks.Block> blocks, string kind, string verb)
    {
        foreach (var block in blocks)
        {
            if (!string.Equals(block.Verb, verb, StringComparison.Ordinal))
            {
                continue;
            }

            var accepted = verb switch
            {
                "move" => block.Outcome.Contains("moved from (", StringComparison.Ordinal),
                "buy" => block.Outcome.Contains("bought ", StringComparison.Ordinal)
                    && block.Outcome.Contains(" tons of supply", StringComparison.Ordinal),
                "transfer-money" => block.Outcome.Contains(" talents from ", StringComparison.Ordinal),
                _ => block.Outcome.Contains($"{kind} accepted.", StringComparison.Ordinal),
            };

            if (accepted)
            {
                return true;
            }
        }

        return false;
    }

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
