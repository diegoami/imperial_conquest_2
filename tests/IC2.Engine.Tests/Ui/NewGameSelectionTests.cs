using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T24.md</c> Done-when 4: "picking either value is what the scenario bootstrap actually
/// reads (not a cosmetic control disconnected from the loaded Ruleset)." <c>godot/Checks/RulesetFlowCheck.cs</c>
/// proves this against the real Godot scene tree (headless, not run by <c>dotnet test</c>); these tests
/// prove the same claim against <c>GameSessionFactory</c> itself — the Godot-free class every screen
/// (and that check) actually calls, linked here exactly as <c>RulesetPresetsTests</c> is.
/// </summary>
public sealed class NewGameSelectionTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    [Theory]
    [InlineData(RulesetPreset.ClassicalFaithful, "classical-faithful")]
    [InlineData(RulesetPreset.Improved, "improved")]
    public void CreateSession_reads_the_rulesetPreset_the_selection_carries(RulesetPreset preset, string expectedRulesetId)
    {
        var selection = new NewGameSelection { RulesetPreset = preset, HumanNationId = "rome" };

        var session = GameSessionFactory.CreateSession(Repository, selection);

        Assert.Equal(expectedRulesetId, session.Ruleset.Id);
    }

    [Fact]
    public void CreateSession_seats_exactly_the_chosen_nation_as_human()
    {
        var selection = new NewGameSelection { RulesetPreset = RulesetPreset.ClassicalFaithful, HumanNationId = "carthage" };

        var session = GameSessionFactory.CreateSession(Repository, selection);

        Assert.Equal("carthage", session.State.ActiveNationId);
        var carthage = session.State.NationById("carthage");
        Assert.NotNull(carthage);
        Assert.Equal(SeatControl.Human, carthage!.Control);

        foreach (var nation in session.State.Nations.Where(n => n.Id != "carthage"))
        {
            Assert.Equal(SeatControl.Ai, nation.Control);
        }
    }

    [Fact]
    public void CreateSession_applies_the_ai_personality_override_to_every_ai_seat_but_not_the_human_seat()
    {
        // Rework round 1, PR #466 review, non-blocking finding 4: this test's own name promises the
        // human seat is checked too, but it used to `continue` past "rome" without ever asserting
        // anything about it -- so a regression that applied the override to every seat, human included,
        // could not have failed this test. Fixed by asserting the human seat's own Personality
        // explicitly, against the value the base scenario itself ships (null -- confirmed:
        // data/scenarios/classical-mediterranean.json's own "rome" seat carries no personality), not
        // merely "not the override" (which null already satisfies, but stating the concrete expected
        // value is what actually pins the behaviour rather than a weaker not-equal check).
        var personality = new AiPersonality(0.9, 0.1, 0.4);
        var selection = new NewGameSelection
        {
            RulesetPreset = RulesetPreset.ClassicalFaithful,
            HumanNationId = "rome",
            AiPersonalityOverride = personality,
        };

        var session = GameSessionFactory.CreateSession(Repository, selection);

        var humanSeat = session.Scenario.Seats.Single(seat => seat.Nation == "rome");
        Assert.Null(humanSeat.Personality);

        foreach (var seat in session.Scenario.Seats)
        {
            if (seat.Nation == "rome")
            {
                continue;
            }

            Assert.Equal(personality, seat.Personality);
        }
    }

    [Fact]
    public void SeatOptionsFor_lists_every_seat_in_the_scenarios_own_order_with_world_names()
    {
        var resolved = Repository.Resolve("classical-mediterranean");

        var options = GameSessionFactory.SeatOptionsFor(resolved);

        Assert.Equal(resolved.Scenario.Seats.Count, options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            Assert.Equal(resolved.Scenario.Seats[i].Nation, options[i].NationId);
            var nation = resolved.World.NationById(options[i].NationId);
            Assert.Equal(nation?.Name, options[i].DisplayName);
        }
    }

    [Fact]
    public void CreateSession_throws_when_no_human_nation_was_chosen()
    {
        var selection = new NewGameSelection { RulesetPreset = RulesetPreset.ClassicalFaithful };

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for a null argument
        // specifically (ArgumentException for empty/whitespace) -- ThrowsAny matches either, since this
        // test's own point is "throws, rather than silently building a seatless session", not which of
        // the two subtypes.
        Assert.ThrowsAny<ArgumentException>(() => GameSessionFactory.CreateSession(Repository, selection));
    }
}
