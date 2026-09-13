using IC2.Engine.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Definition of Done item 2: "A no-op test system registers by attribute alone and executes in the
/// declared phase order; a second one added in a second file does not require editing any shared file
/// (asserted by the test's own structure)."
/// </summary>
/// <remarks>
/// The second clause is asserted mechanically rather than by inspection. Nothing in this file names the
/// fixture classes: their names are recovered from the registry, and the repository's own C# sources are
/// then searched for each name. A name that occurs in exactly one file cannot have been mentioned in a
/// registration list, a wiring file, or anywhere else — which is the whole claim.
/// </remarks>
[Collection(RepositorySourcesCollection.Name)]
public class SystemRegistrationTests
{
    private const string Group = "phase-order";
    private const string TieBreakGroup = "order-tie-break";

    [Fact]
    public void Systems_register_by_attribute_alone_and_run_in_the_declared_phase_order()
    {
        var coordinator = CoreTestbed.CoordinatorFor(Group);
        var registry = CoreTestbed.RegistryFor(Group);

        // Three no-op systems live in three files beside this one. Nothing here, and nothing anywhere
        // else, lists them.
        Assert.Equal(3, registry.Systems.Count);

        var state = CoreTestbed.InitialState();
        var result = coordinator.RunTurn(state);

        // Declared order, not file order and not scan order: gamma and beta are in SeatStart (gamma
        // first, by Order), alpha is in the later SeatEnd phase even though its file sorts first.
        Assert.Equal(
            new[] { "test.phase-order.gamma", "test.phase-order.beta", "test.phase-order.alpha" },
            result.Trace.Select(execution => execution.SystemId).ToArray());

        Assert.Equal(
            new[] { TurnPhase.SeatStart, TurnPhase.SeatStart, TurnPhase.SeatEnd },
            result.Trace.Select(execution => execution.Phase).ToArray());
    }

    [Fact]
    public void No_shared_file_mentions_the_registered_fixtures()
    {
        var registry = CoreTestbed.RegistryFor(Group);
        var sources = CSharpSources().ToArray();
        Assert.NotEmpty(sources);

        foreach (var system in registry.Systems)
        {
            // Recovered from the registry, never written here: if this test named the classes, it would
            // itself be a second file mentioning them and the assertion below would be circular.
            var typeName = system.ImplementationType.Name;

            var mentioning = sources
                .Where(path => File.ReadAllText(path).Contains(typeName, StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                mentioning.Length == 1,
                $"'{typeName}' is registered by attribute, so exactly one file — its own — should mention "
                + $"it. Found {mentioning.Length}: {string.Join(", ", mentioning)}");
        }
    }

    [Fact]
    public void A_systems_order_within_a_phase_is_total_even_when_two_declare_the_same_order()
    {
        var registry = CoreTestbed.RegistryFor(TieBreakGroup);

        // Two systems, same phase, SAME Order — so only the id tie-break decides, and deleting that
        // tie-break from SystemRegistry would make this test fail rather than leave it quietly green.
        // Their declared Orders are equal and their file names sort in the opposite direction to their
        // ids, so neither declaration order nor scan order could produce this result by accident.
        var seatStart = registry.InPhase(TurnPhase.SeatStart);
        Assert.Equal(2, seatStart.Count);
        Assert.Equal(seatStart[0].Order, seatStart[1].Order);
        Assert.Equal(
            new[] { "test.tie-break.aardvark", "test.tie-break.zulu" },
            seatStart.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void A_system_declaring_a_phase_that_does_not_exist_fails_loudly()
    {
        // [GameSystem((TurnPhase)0, "x.y")] compiles, and before this check such a system was silently
        // dropped: no bucket for its phase, so nothing ever looked for it. A pipeline that quietly omits a
        // system is the worst failure mode available to a build where 25 tasks each add one.
        var error = Assert.Throws<InvalidOperationException>(
            () => CoreTestbed.RegistryFor(UndeclaredPhaseFixture.Group));

        Assert.Contains("not one of the declared turn phases", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_scans_of_the_same_assembly_produce_the_same_pipeline()
    {
        var first = CoreTestbed.RegistryFor(Group);
        var second = CoreTestbed.RegistryFor(Group);

        Assert.Equal(
            first.Systems.Select(s => (s.Id, s.Phase, s.Order)),
            second.Systems.Select(s => (s.Id, s.Phase, s.Order)));
    }

    [Fact]
    public void The_declared_phase_order_is_stated_in_exactly_one_place()
    {
        // Later tasks read TurnPhases.InOrder; the scopes partition it, so no phase can be silently
        // dropped from the pipeline by being neither seat- nor round-scoped.
        Assert.Equal(
            TurnPhases.InOrder,
            TurnPhases.SeatScoped.Concat(TurnPhases.RoundScoped).ToArray());

        Assert.Equal(
            Enum.GetValues<TurnPhase>().OrderBy(phase => (int)phase).ToArray(),
            TurnPhases.InOrder.ToArray());

        for (var i = 0; i < TurnPhases.InOrder.Count; i++)
        {
            Assert.Equal(i, TurnPhases.PositionOf(TurnPhases.InOrder[i]));
        }
    }

    /// <summary>
    /// Every committed C# source file in the repository, excluding build output, in ordinal path order
    /// so that a failure names the same file first on every machine.
    /// </summary>
    internal static IReadOnlyList<string> CSharpSources()
    {
        var files = new List<string>();
        foreach (var root in new[] { "src", "tests", "godot" })
        {
            var directory = Path.Combine(ModelTestPaths.RepositoryRoot, root);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(ModelTestPaths.RepositoryRoot, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
