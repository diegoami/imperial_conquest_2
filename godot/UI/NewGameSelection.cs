using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

/// <summary>
/// One candidate human seat New Game's seat-assignment step can offer — a nation id/display name pair
/// read off the resolved scenario's own <see cref="Scenario.Seats"/>, in that list's own order (never
/// re-sorted, so the screen's order matches the scenario file's).
/// </summary>
public sealed record SeatOption(string NationId, string DisplayName);

/// <summary>
/// The New Game flow's own accumulating state — Godot-free (see <see cref="RulesetPresets"/>'s remarks
/// for why) so <see cref="GameSessionFactory"/> and this class can both be exercised by a plain xunit
/// test. Holds exactly what <c>docs/tasks/T24.md</c>'s flow needs to remember between its own screens:
/// the chosen ruleset preset (<c>RulesetChooser</c>), which nation the player seats
/// (<c>ScenarioSeatScreen</c>), and the AI personality override that screen's sliders produce.
/// </summary>
public sealed class NewGameSelection
{
    /// <summary><c>docs/tasks/T24.md</c> Scope: "Classical Faithful is the default highlight."</summary>
    public RulesetPreset RulesetPreset { get; set; } = RulesetPresets.Default;

    /// <summary>
    /// The human seat's nation id, or <see langword="null"/> before the seat step has ever set one — a
    /// screen that reads this before the seat step ran is a bug, not a state to silently default from.
    /// </summary>
    public string? HumanNationId { get; set; }

    /// <summary>
    /// The New Game flow's own AI personality sliders (<c>docs/game-design.md</c> §"User interface"
    /// item 1's "AI personality sliders"), applied uniformly to every non-human seat.
    /// <strong>[designed]</strong>: <c>game-design.md</c> does not say whether the sliders are one set
    /// for every AI seat or one set per seat; sixteen individual sliders (the world's own seat count)
    /// would not fit a screen a human visually reviews in one sitting, so this build offers one set that
    /// applies to every AI seat at once. <see langword="null"/> until the seat step's sliders are shown
    /// (every value neutral, 0.5, until moved), matching <see cref="AiPersonality"/>'s own documented
    /// 0–1 range.
    /// </summary>
    public AiPersonality? AiPersonalityOverride { get; set; }

    public RulesetCard SelectedCard => RulesetPresets.CardFor(RulesetPreset);
}

/// <summary>
/// Builds the actual <see cref="GameSession"/> the New Game flow's own selections resolve to — the one
/// place <see cref="NewGameSelection"/>'s fields turn into a real, playable session, so
/// <c>docs/tasks/T24.md</c> Done-when 4's own claim ("picking either value is what the scenario
/// bootstrap actually reads") is code, not a comment.
/// </summary>
public static class GameSessionFactory
{
    /// <summary>
    /// The repository root, given <c>res://</c> already globalized to an absolute path — mirrors
    /// <c>Slice.cs</c>'s own convention (<c>godot/</c> is one level below the repository root) exactly,
    /// rather than inventing a second one.
    /// </summary>
    public static string RepositoryRootFromGlobalizedResPath(string globalizedResPath) =>
        Path.GetFullPath(Path.Combine(globalizedResPath, ".."));

    public static GameDataRepository LoadRepository(string repositoryRoot) =>
        GameDataRepository.Load(Path.Combine(repositoryRoot, "data"));

    /// <summary>
    /// The human-seat candidates a scenario offers, in the scenario's own seat order, each labelled by
    /// the <em>world's</em> own nation name (<see cref="World.NationById"/>) — the scenario's <see cref="Seat"/>
    /// itself carries only the bare nation id.
    /// </summary>
    public static IReadOnlyList<SeatOption> SeatOptionsFor(ResolvedScenario resolved)
    {
        var options = new List<SeatOption>();
        foreach (var seat in resolved.Scenario.Seats)
        {
            var nation = resolved.World.NationById(seat.Nation);
            options.Add(new SeatOption(seat.Nation, nation?.Name ?? seat.Nation));
        }

        return options;
    }

    /// <summary>Resolves <paramref name="selection"/>'s own ruleset card to its scenario/world/ruleset.</summary>
    public static ResolvedScenario ResolveSelectedScenario(GameDataRepository repository, NewGameSelection selection) =>
        repository.Resolve(selection.SelectedCard.ScenarioId);

    /// <summary>
    /// Builds the <see cref="GameSession"/> a "Start Game" action bootstraps —
    /// <see cref="GameSession"/>'s own constructor already marks <paramref name="selection"/>'s
    /// <see cref="NewGameSelection.HumanNationId"/> as the one human seat (never a second one), so this
    /// method's only own composition is folding <see cref="NewGameSelection.AiPersonalityOverride"/>
    /// into every <em>other</em> seat first, the same <c>scenario with { Seats = ... }</c> shape
    /// <see cref="GameSession"/>'s own constructor already uses for the human seat.
    /// </summary>
    public static GameSession CreateSession(GameDataRepository repository, NewGameSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.HumanNationId);

        var resolved = ResolveSelectedScenario(repository, selection);
        var scenario = resolved.Scenario;

        if (selection.AiPersonalityOverride is { } personality)
        {
            scenario = scenario with
            {
                Seats = ValueList.From(scenario.Seats.Select(seat =>
                    string.Equals(seat.Nation, selection.HumanNationId, StringComparison.Ordinal)
                        ? seat
                        : seat with { Personality = personality })),
            };
        }

        return new GameSession(resolved.World, resolved.Ruleset, scenario, humanSeatNationId: selection.HumanNationId);
    }
}
