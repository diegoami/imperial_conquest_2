using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Engine.Model;

namespace IC2.Engine.Victory;

/// <summary>
/// Parses and evaluates a scenario's <see cref="VictoryCondition.Goal"/> for
/// <see cref="VictoryConditionType.Custom"/> — "an explicit goal defined in the scenario file (e.g.,
/// 'hold these 3 named cities on turn 50')" (<c>docs/game-design.md</c> §"Victory conditions").
/// </summary>
/// <remarks>
/// <para>
/// T02's <see cref="VictoryCondition"/> carries the goal as a free-form <see cref="string"/> — there is
/// no original analogue for scenario authoring, so no report defines a schema for it. This is the one
/// place that string's shape is decided, and it lives entirely inside this task's own
/// <c>src/IC2.Engine/Victory/**</c> Owns list: a small JSON object naming which nation must hold which
/// cities, and (optionally) not before which turn. Kept deliberately small — exactly the example
/// <c>game-design.md</c> gives, made checkable, and nothing more.
/// </para>
/// <para>
/// A goal is authored purely as scenario JSON text; nothing about which cities or which nation matters
/// is ever a C# literal here.
/// </para>
/// <para>
/// <strong>Degenerate goals (review round 1).</strong> Everything <see cref="Parse"/> can check from the
/// JSON text alone is rejected there, with a <see cref="FormatException"/>, rather than left to produce
/// a surprising runtime answer: a missing or empty <c>"nation"</c>, a missing, empty, or
/// null/blank-entry-carrying <c>"holdCities"</c> array (an empty array previously fell through
/// <see cref="Evaluate"/>'s hold-check loop zero times and declared an instant, unearned win — the
/// defect this note exists to close), and a negative <c>"turnAtOrAfter"</c> (meaningless: every
/// <see cref="Model.CalendarState.TurnIndex"/> is already <c>&gt;= 0</c>, so a negative value could only
/// ever ratify a win the state doesn't need to earn). What <see cref="Parse"/> cannot check — because it
/// never sees a <see cref="GameState"/> — is left to <see cref="Evaluate"/> as documented, tested
/// behaviour rather than a rejection: a <c>"nation"</c> id absent from the state's nations, and a city id
/// in <c>"holdCities"</c> absent from the state's cities, each simply mean the goal is not currently met
/// (<see cref="VictoryStatus.Undecided"/>) — a scenario author who misspells either gets a goal that
/// never fires rather than a crash mid-game, which is the friendlier failure mode for something that can
/// only be discovered once the scenario is actually played. A repeated city id in <c>"holdCities"</c> is
/// accepted and is provably harmless: the hold check is idempotent (checking the same city's owner twice
/// changes nothing), so it is not rejected.
/// </para>
/// </remarks>
public static class CustomVictoryGoal
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Evaluates a custom victory condition's goal text against a state.</summary>
    /// <exception cref="ArgumentException"><paramref name="victory"/> is not a custom condition.</exception>
    /// <exception cref="FormatException">The goal text is not the small JSON shape this parses.</exception>
    public static VictoryOutcome Evaluate(GameState state, VictoryCondition victory)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(victory);

        if (victory.Type != VictoryConditionType.Custom)
        {
            throw new ArgumentException("Not a custom victory condition.", nameof(victory));
        }

        if (string.IsNullOrWhiteSpace(victory.Goal))
        {
            // IC2.Engine.Serialization.GameDataValidation already rejects a loaded scenario whose custom
            // condition states no goal; a caller reaching this with an empty goal built the
            // VictoryCondition directly rather than through that loader.
            throw new InvalidOperationException(
                "A custom victory condition must state a \"goal\".");
        }

        var spec = Parse(victory.Goal);

        if (spec.TurnAtOrAfter is { } turn && state.Calendar.TurnIndex < turn)
        {
            return VictoryOutcome.Undecided(VictoryConditionType.Custom);
        }

        var nation = state.NationById(spec.Nation);
        if (nation is null || nation.Eliminated)
        {
            return VictoryOutcome.Undecided(VictoryConditionType.Custom);
        }

        foreach (var cityId in spec.HoldCities)
        {
            var city = state.CityById(cityId);
            if (city is null || !string.Equals(city.Owner, spec.Nation, StringComparison.Ordinal))
            {
                return VictoryOutcome.Undecided(VictoryConditionType.Custom);
            }
        }

        return VictoryOutcome.Won(VictoryConditionType.Custom, spec.Nation);
    }

    private static CustomVictoryGoalSpec Parse(string goal)
    {
        RawCustomVictoryGoal? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawCustomVictoryGoal>(goal, ParseOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                $"Custom victory goal '{goal}' is not the "
                + "{\"nation\":\"...\",\"holdCities\":[...],\"turnAtOrAfter\":...} JSON this evaluator "
                + "understands.",
                ex);
        }

        if (raw is null || string.IsNullOrWhiteSpace(raw.Nation) || raw.HoldCities is null)
        {
            throw new FormatException(
                $"Custom victory goal '{goal}' must name a non-empty \"nation\" and a \"holdCities\" array.");
        }

        if (raw.HoldCities.Length == 0)
        {
            // Review round 1, blocking finding 1: an empty array used to pass this check (only null was
            // rejected), so Evaluate's hold-check loop ran zero times and any live named nation won
            // instantly, holding nothing. A goal that names no city to hold is not a goal at all.
            throw new FormatException(
                $"Custom victory goal '{goal}' names an empty \"holdCities\" array; a goal must name at "
                + "least one city to hold, or it would be satisfied by holding nothing.");
        }

        foreach (var cityId in raw.HoldCities)
        {
            if (string.IsNullOrWhiteSpace(cityId))
            {
                throw new FormatException(
                    $"Custom victory goal '{goal}' has a null or blank entry in \"holdCities\".");
            }
        }

        if (raw.TurnAtOrAfter is < 0)
        {
            throw new FormatException(
                $"Custom victory goal '{goal}' has a negative \"turnAtOrAfter\" "
                + $"({raw.TurnAtOrAfter}); the turn counter never goes below 0.");
        }

        return new CustomVictoryGoalSpec(raw.Nation, raw.HoldCities, raw.TurnAtOrAfter);
    }

    /// <summary>The goal text's shape as JSON — every field optional until <see cref="Parse"/> validates it.</summary>
    private sealed record RawCustomVictoryGoal(
        [property: JsonPropertyName("nation")] string? Nation,
        [property: JsonPropertyName("holdCities")] string[]? HoldCities,
        [property: JsonPropertyName("turnAtOrAfter")] int? TurnAtOrAfter);

    /// <summary>
    /// The goal, validated: which nation must hold which cities, and (optionally) not before which turn.
    /// </summary>
    private sealed record CustomVictoryGoalSpec(
        string Nation,
        string[] HoldCities,
        int? TurnAtOrAfter);
}
