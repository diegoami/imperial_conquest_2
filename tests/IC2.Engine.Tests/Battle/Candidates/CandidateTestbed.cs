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
        new(AsAiControlled(Repository.CreateInitialState("toy-3city")), Repository.Resolve("toy-3city").World, 3, 2, 4, 2);

    /// <summary>
    /// T88: <c>InstantBattleResolver.ResolveField</c> now reads each side's <see cref="SeatControl"/> for
    /// its post-battle treaty gate — a battle with exactly one human side takes a different,
    /// human-consent path that draws differently from the AI-vs-AI one C1's own doc comment states
    /// ("the peace roll", drawn unconditionally). This harness is measurement-only and has never modelled
    /// human/AI distinctions at all ("nothing in the game calls anything in this namespace"), so C1's two
    /// nations are forced AI here — the toy scenario's own "north" seat is otherwise human, which would
    /// silently switch C1 onto the human-consent branch and break the §6.1 draw-count formula this file's
    /// own tests check it against, for a reason that has nothing to do with what C1 measures.
    /// </summary>
    internal static GameState AsAiControlled(GameState state) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                n.Control == SeatControl.Ai
                    ? n
                    : n with { Control = SeatControl.Ai, Personality = n.Personality ?? new AiPersonality(0.5, 0.5, 0.5) })),
        };

    public static IReadOnlyList<IAutoResolveCandidate> All() => new IAutoResolveCandidate[]
    {
        C1(),
        new HeadlessTacticalCandidate(),
        new TypeWeightedInstantCandidate(),
        new RoundBasedCandidate(),
        new MoraleRetreatCandidate(),
    };
}
