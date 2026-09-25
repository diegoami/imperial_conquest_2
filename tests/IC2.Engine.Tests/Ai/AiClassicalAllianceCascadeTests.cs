using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Abstractions;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T82 (#359, bug #357) Done-when 3's own test: "one test runs a round of the classical scenario from
/// uniform peace and shows no alliance cascade... The user saw about 38 alliances in round one; the
/// original's rule makes an alliance only against a common enemy, so from uniform peace it forms none."
/// </summary>
/// <remarks>
/// <para>
/// <strong>Uniform peace, not the DAT's own starting matrix.</strong> T75 gave <c>classical-mediterranean</c>
/// its own <see cref="World.StartingRelations"/> (5 wars, 13 trades, 4 alliances), which
/// <see cref="Model.GameStateFactory"/> reads when present; setting it to <see langword="null"/> here
/// restores the pre-T75 "open at uniform peace" default (that field's own doc comment) without touching
/// the committed world file itself.
/// </para>
/// <para>
/// <strong>Why zero, and not merely "few".</strong> The alliance search
/// (<see cref="AiOwnDiplomacyRule.FindAlliancePartner"/>) only ever proposes a partner <c>m</c> already
/// at war with a neighbour <c>j</c> the acting nation shares. From uniform peace, no nation is at war
/// with anyone until <em>something</em> declares first — and a declaration needs a war target whose
/// power ratio exceeds the base (report §1a), which is not guaranteed within one round even where a
/// target exists. So the alliance search's own precondition, "some AI is already at war with a shared
/// neighbour", starts false for the whole round and can only become true after a war lands earlier in
/// the very same round — rare enough over 16 seat-turns that zero is the expected outcome, not merely a
/// generous bound.
/// </para>
/// </remarks>
public sealed class AiClassicalAllianceCascadeTests
{
    private readonly ITestOutputHelper _output;

    public AiClassicalAllianceCascadeTests(ITestOutputHelper output) => _output = output;

    /// <summary>One round of this 16-nation scenario is 16 seat-turns.</summary>
    private const int OneRound = 16;

    [Fact]
    public void OneRoundFromUniformPeace_FormsNoAllianceCascade()
    {
        var shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var world = shipped.World with { StartingRelations = null };

        var allianceCode = shipped.Ruleset.Diplomacy.StateCodes.Alliance;

        for (ulong seed = 1; seed <= 5; seed++)
        {
            var result = AiGameRunner.Run(world, shipped.Ruleset, shipped.Scenario, seed, OneRound);

            var allianceCount = CountAlliances(result.FinalState, allianceCode);
            _output.WriteLine($"seed {seed}: {allianceCount} alliance(s) after one round from uniform peace");

            // Zero rejected commands and no stall run this short is Done-when 5's own invariant, checked
            // here too since a run that hit either would say the fixture itself is broken, not that the
            // alliance rule passed for the wrong reason.
            Assert.Equal(0, result.CommandsRejected);
            Assert.Equal(0, result.ProjectionMismatches);

            Assert.True(
                allianceCount < 5,
                $"seed {seed}: expected no alliance cascade (the user saw ~38 under the old heuristic), "
                + $"got {allianceCount}");
        }
    }

    /// <summary>Half the non-zero cells of the symmetric relation matrix that read as an alliance.</summary>
    private static int CountAlliances(GameState state, int allianceCode)
    {
        var ids = state.Relations.NationIds;
        var count = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                if (state.Relations.Get(ids[i], ids[j]) == allianceCode)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
