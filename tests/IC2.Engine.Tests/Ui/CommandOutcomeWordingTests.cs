using System.Reflection;
using System.Text.RegularExpressions;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T147 (bug #781 point 4): every command kind the app can issue has a readable wording — a short line
/// the output area shows in place of the engine's raw <c>"{kind} accepted."</c> — and a kind with no
/// wording falls back to the engine's own line. The kinds are read from the engine's own
/// <see cref="ICommand"/> types (the same reflection <c>CommandVerbCatalog</c> uses), minus the small,
/// explicitly listed set <see cref="CommandOutcomeWording.NotIssuedByTheApp"/> the Godot app never
/// submits; a new command type therefore fails this test until it is given a wording or named as unused.
/// </summary>
/// <remarks>
/// <see cref="CommandOutcomeWording"/> has no <c>using Godot</c>, so it is linked into this project by the
/// same <c>&lt;Compile Include&gt;</c> seam <c>CommandOutcomeText.cs</c> already established (never a
/// second, hand-maintained copy). The two Scope examples are pinned by their exact text.
/// </remarks>
public sealed class CommandOutcomeWordingTests
{
    [Fact]
    public void Every_command_kind_the_app_can_issue_has_a_readable_wording()
    {
        var issuable = AllCommandKinds()
            .Where(kind => !CommandOutcomeWording.NotIssuedByTheApp.Contains(kind))
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(issuable);

        var missing = issuable.Where(kind => CommandOutcomeWording.For(kind) is null).ToList();
        Assert.True(
            missing.Count == 0,
            $"command kinds with no wording: {string.Join(", ", missing)}");

        var unknown = CommandOutcomeWording.ByKind.Keys
            .Except(issuable, StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            unknown.Count == 0,
            $"wording entries that name no app-issuable command kind: {string.Join(", ", unknown)}");

        foreach (var kind in issuable)
        {
            var wording = CommandOutcomeWording.For(kind)!;
            Assert.False(string.IsNullOrWhiteSpace(wording), $"'{kind}' has an empty wording");

            // No dotted engine key: the only '.' is the sentence's final full stop.
            Assert.False(
                Regex.IsMatch(wording, @"\b\w+\.\w+\b"),
                $"'{kind}' carries a dotted engine key: '{wording}'");
            Assert.False(
                wording.EndsWith(CommandOutcomeWording.AcceptanceSuffix, StringComparison.Ordinal),
                $"'{kind}' still ends in the raw acceptance suffix: '{wording}'");
        }
    }

    /// <summary>The two examples in the task's Scope, exactly as given.</summary>
    [Fact]
    public void The_two_scope_examples_read_as_given()
    {
        Assert.Equal("You accepted the peace treaty.", CommandOutcomeWording.For("diplomacy.accept-peace-treaty"));
        Assert.Equal("Unit recruited.", CommandOutcomeWording.For("recruitment.recruit-standing-unit"));
    }

    /// <summary>A wording exists for both app-issuable kinds the Scope names, even though one is a map
    /// order and one a context-panel order — the wording is keyed by the engine's kind, not the surface.</summary>
    [Fact]
    public void The_examples_replace_their_raw_lines()
    {
        Assert.Equal(
            "You accepted the peace treaty.",
            CommandOutcomeWording.ReadableLine("diplomacy.accept-peace-treaty accepted."));
        Assert.Equal(
            "Unit recruited.",
            CommandOutcomeWording.ReadableLine("recruitment.recruit-standing-unit accepted."));
    }

    /// <summary>Bug #914: the six kinds the Taxation, fleet and mercenary dialogs submit are worded.</summary>
    [Theory]
    [InlineData("economy.set-tax", "Tax set.")]
    [InlineData("naval.split-fleet", "Fleets split.")]
    [InlineData("naval.join-fleets", "Fleets joined.")]
    [InlineData("naval.buy-fleet-supply", "Fleet supplied.")]
    [InlineData("naval.fleet-to-fleet-transfer", "Ships transferred.")]
    [InlineData("recruitment.hire-mercenary", "Mercenaries hired.")]
    public void Dialog_issued_kinds_are_worded_through_ByKind(string kind, string expected)
    {
        Assert.False(CommandOutcomeWording.NotIssuedByTheApp.Contains(kind));
        Assert.Equal(expected, CommandOutcomeWording.ByKind[kind]);
        Assert.Equal(expected, CommandOutcomeWording.ReadableLine(kind + " accepted."));
    }

    /// <summary>Bug #914: Build fleet answers the original's sentence with the build city's name.</summary>
    [Fact]
    public void Build_fleet_names_the_build_city()
    {
        var state = IC2.Engine.Tests.Naval.NavalTestbed.InitialState();
        var arx = state.CityById("arx")!;
        var dispatcher = IC2.Engine.Tests.Naval.NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new IC2.Engine.Naval.Commands.OrderFleetCommand(arx.Owner, arx.Id, 10, "b914"));
        Assert.True(result.IsAccepted, result.ToString());

        Assert.False(CommandOutcomeWording.NotIssuedByTheApp.Contains("naval.order-fleet"));
        Assert.Equal(
            $"The fleet will be built at {arx.Name}.",
            CommandOutcomeWording.Apply("naval.order-fleet accepted.", result.State));
    }

    /// <summary>A kind with no wording, and every rejection or composed line, is returned unchanged.</summary>
    [Fact]
    public void A_line_without_a_wording_falls_back_to_the_engine_line()
    {
        Assert.Null(CommandOutcomeWording.For("no.such-kind"));
        Assert.Equal(
            "no.such-kind accepted.",
            CommandOutcomeWording.ReadableLine("no.such-kind accepted."));
        Assert.Equal(
            "The army is not adjacent.",
            CommandOutcomeWording.ReadableLine("battle.besiege-city rejected (battle.siege-not-adjacent): the army is not adjacent."));
        Assert.Equal(
            "diplomacy.declare-war accepted (composed ahead of the attack).",
            CommandOutcomeWording.ReadableLine("diplomacy.declare-war accepted (composed ahead of the attack)."));
    }

    /// <summary>Every <see cref="ICommand"/>-implementing type's <c>Kind</c>, read off a placeholder
    /// instance — the same reflection <c>CommandVerbCatalog.KindOf</c> uses, since <c>Kind</c> never
    /// depends on argument values.</summary>
    private static IEnumerable<string> AllCommandKinds() =>
        typeof(ICommand).Assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t))
            .Select(KindOf)
            .Distinct(StringComparer.Ordinal);

    private static string KindOf(Type commandType)
    {
        var constructor = commandType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var arguments = constructor.GetParameters().Select(PlaceholderFor).ToArray();
        return ((ICommand)constructor.Invoke(arguments)).Kind;
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

        // T238: an optional enum parameter (MoveWalkPolicy) takes its declared default.
        if (type.IsEnum)
        {
            return parameter.HasDefaultValue ? parameter.DefaultValue : Enum.GetValues(type).GetValue(0);
        }

        throw new NotSupportedException(
            $"CommandOutcomeWordingTests.PlaceholderFor has no case for parameter type '{type}' "
            + $"(parameter '{parameter.Name}').");
    }

    [Theory]
    [InlineData("diplomacy.propose-trade rejected (diplomacy.target-trade-cap-reached): 'Carthage' already trades with 3 nations.",
        "'Carthage' already trades with 3 nations.")]
    [InlineData("Move rejected (movement.no-moves): the army has no moves left.", "The army has no moves left.")]
    [InlineData("Purchase rejected (economy.too-little-money): too little money.", "Too little money.")]
    public void A_refusal_reads_as_its_own_plain_message(string line, string expected)
    {
        Assert.Equal(expected, CommandOutcomeWording.ReadableLine(line));
        Assert.Equal(expected, CommandOutcomeWording.Apply(line));
    }

    [Theory]
    [InlineData("Saved to user://saves/a.json")]
    [InlineData("diplomacy.declare-war accepted (composed ahead of the attack).")]
    [InlineData("Rome rejected (the offer): nothing")]
    [InlineData("battle.attack-army rejected (battle.x): ")]
    public void Other_lines_are_not_refusals(string line) => Assert.Null(CommandOutcomeWording.RefusalMessage(line));
}
