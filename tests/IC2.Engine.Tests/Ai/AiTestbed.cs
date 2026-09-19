using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// The shipped toy data, plus the all-AI scenario the soak plays and the settings it plays it under.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the all-AI scenario is built here and not committed as a file.</strong>
/// <c>docs/task-catalogue.md</c> T22 Done-when 1 asks for an "<em>all-AI toy scenario</em>", but this
/// task's Owns list grants <c>data/rulesets/toy-ruleset.json</c>'s <c>economy</c> key and nothing else
/// under <c>data/</c> — adding <c>data/scenarios/toy-3city-all-ai.json</c> would be a scope breach
/// (<c>docs/build-process.md</c> §4.2 gate 4). So the scenario is derived in the test from the committed
/// one: the same world, the same ruleset, the same victory condition and the same turn limit, with
/// <c>north</c>'s seat flipped from <see cref="SeatControl.Human"/> to <see cref="SeatControl.Ai"/> and
/// given a personality. Everything the soak plays is therefore committed data apart from those two
/// fields, which are visible right here.
/// </para>
/// </remarks>
public static class AiTestbed
{
    private static readonly Lazy<GameDataRepository> LazyRepository =
        new(() => GameDataRepository.Load(ModelTestPaths.DataRoot));

    /// <summary>The shipped toy scenario, world and ruleset, as committed.</summary>
    public static ResolvedScenario Toy => LazyRepository.Value.Resolve("toy-3city");

    /// <summary>
    /// The north seat's personality for the soak: moderately aggressive, moderately expansionist,
    /// indifferent to alliances. <c>[designed]</c> test-fixture values, chosen to be visibly different
    /// from <c>south</c>'s committed <c>0.8 / 0.5 / 0.25</c> so the two seats do not play the same game,
    /// and not derived from any report — <c>docs/design-audit.md</c> §1 records that the original has no
    /// per-nation AI tuning at all, so there is nothing to transcribe.
    /// </summary>
    public static AiPersonality NorthPersonality { get; } =
        new(Aggression: 0.7, ExpansionDrive: 0.6, LoyaltyToAlliances: 0.3);

    /// <summary>
    /// The turn cap every soak seed runs under. <c>[designed]</c>: sized so that fifty seeds fit
    /// comfortably inside <c>docs/task-catalogue.md</c> T22 Done-when 2's five-minute budget, and large
    /// enough that a seed which is going to reach a decision reaches it. It is deliberately <em>not</em>
    /// tuned upward to chase victories — that line forbids it in as many words.
    /// </summary>
    public const int SoakTurnCap = 1200;

    /// <summary>The fifty fixed seeds. Consecutive from 1 so a failing seed's number is also its index.</summary>
    public const int SoakSeedCount = 50;

    /// <summary>The all-AI toy scenario: both seats AI, everything else as committed.</summary>
    public static Scenario AllAiScenario()
    {
        var scenario = Toy.Scenario;
        var seats = new Seat[scenario.Seats.Count];
        for (var i = 0; i < scenario.Seats.Count; i++)
        {
            var seat = scenario.Seats[i];
            seats[i] = seat.Control == SeatControl.Ai
                ? seat
                : seat with { Control = SeatControl.Ai, Personality = NorthPersonality };
        }

        return scenario with { Id = scenario.Id + "-all-ai", Seats = ValueList<Seat>.Of(seats) };
    }

    /// <summary>
    /// Runs one soak seed. <c>docs/task-catalogue.md</c> T22 Done-when 4 requires a failing seed to be
    /// reproducible "<em>from its number alone</em>", and this method is how: the soak and the
    /// single-seed reproduction test both call it, so there is exactly one definition of what seed
    /// <c>n</c> means.
    /// </summary>
    /// <param name="seed">The seed, which is also the run's name in the log directory.</param>
    public static AiGameResult RunSeed(ulong seed) =>
        AiGameRunner.Run(Toy.World, Toy.Ruleset, AllAiScenario(), seed, SoakTurnCap);

    /// <summary>The seeds the soak runs, in order: <c>1 … SoakSeedCount</c>.</summary>
    public static IReadOnlyList<ulong> SoakSeeds()
    {
        var seeds = new ulong[SoakSeedCount];
        for (var i = 0; i < SoakSeedCount; i++)
        {
            seeds[i] = (ulong)(i + 1);
        }

        return seeds;
    }
}
