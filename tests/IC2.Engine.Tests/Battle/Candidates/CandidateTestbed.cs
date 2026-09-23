using IC2.Engine.Battle.Candidates;
using IC2.Engine.Battle.Candidates.Tournament;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;

namespace IC2.Engine.Tests.Battle.Candidates;

/// <summary>
/// T59's test fixtures: the shipped <c>classical-faithful</c> ruleset (the one the tournament runs), the
/// harness under <c>scatter</c>, and the five candidates behind the seam, C1 standing on the toy world exactly
/// as the tool stands it.
/// </summary>
internal static class CandidateTestbed
{
    private static readonly Lazy<GameDataRepository> LazyRepository = new(() => GameDataRepository.Load(TestPaths.DataRoot));

    public static GameDataRepository Repository => LazyRepository.Value;

    public static Ruleset Ruleset => Repository.RulesetById("classical-faithful")!;

    public static TournamentHarness Harness(DefeatOutcome onDefeat = DefeatOutcome.Scatter) => new(Ruleset, onDefeat);

    public static MergedInstantCandidate C1() =>
        new(Repository.CreateInitialState("toy-3city"), Repository.Resolve("toy-3city").World, 3, 2, 4, 2);

    public static IReadOnlyList<IAutoResolveCandidate> All() => new IAutoResolveCandidate[]
    {
        C1(),
        new HeadlessTacticalCandidate(),
        new TypeWeightedInstantCandidate(),
        new RoundBasedCandidate(),
        new MoraleRetreatCandidate(),
    };
}
