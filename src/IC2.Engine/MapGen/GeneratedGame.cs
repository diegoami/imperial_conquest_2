using IC2.Engine.Model;

namespace IC2.Engine.MapGen;

/// <summary>
/// T294f: the New Game flow's generated game â€” the three candidate previews the owner decided on,
/// and the scenario a generated game starts from. Everything here is a pure function of
/// (style, seed): no shipped data, no campaign RNG, no wall clock. The chosen candidate's world is
/// embedded in the session's saves (T294e), so the flow never needs a preview cache and a reload
/// never regenerates.
/// </summary>
public static class GeneratedGame
{
    /// <summary>
    /// The one map size the first release offers (the T294 owner decision, docs/tasks/T294.md) â€”
    /// 280Ã—180, the size the recipe property tests already pin. Changing it is a data decision for
    /// the owner, not a screen-level knob.
    /// </summary>
    public static readonly MapSize ReleaseSize = new(280, 180);

    /// <summary>The candidates' stable id prefix, part of generated world and scenario identities.</summary>
    public const string WorldIdPrefix = "random-";

    /// <summary>
    /// The three candidate previews a generated New Game offers (the owner decision): seeds
    /// <paramref name="firstSeed"/>, +1 and +2, each generated independently â€” a pure function, so
    /// the same first seed always offers the same three candidates.
    /// </summary>
    /// <exception cref="MapGenerationFailed">A candidate exhausted its bounded attempts.</exception>
    public static IReadOnlyList<GeneratedWorld> Previews(string styleId, ulong firstSeed)
    {
        var style = MapStyles.ById(styleId)
                    ?? throw new ArgumentException($"no shipped style '{styleId}'.", nameof(styleId));
        var previews = new List<GeneratedWorld>();
        for (var i = 0; i < 3; i++)
        {
            var seed = firstSeed + (ulong)i;
            previews.Add(MapGenerator.Generate(
                "t294f", style, seed, ReleaseSize,
                $"{WorldIdPrefix}{styleId}-{seed}", $"{style.Id} candidate {i + 1} (seed {seed})"));
        }

        return previews;
    }

    /// <summary>
    /// The scenario a generated game starts from: every world nation seated AI (the seat step flips
    /// the ticked ones to human), TotalConquest â€” the shipped scenarios' condition and the original's
    /// only win (manual Â§1) â€” no turn limit, and the chosen candidate's own seed as the campaign
    /// seed, so the whole game is a pure function of (style, seed, seat choices). [designed: the
    /// seeded scenario for fictional maps is this project's own design; the manual's leaders form is
    /// the seat step's reference.]
    /// </summary>
    public static Scenario ScenarioFor(World world, string rulesetId, ulong seed) => new(
        GameDataSchema.CurrentVersion,
        Id: world.Id,
        Name: world.Name,
        WorldId: world.Id,
        RulesetId: rulesetId,
        Seats: new ValueList<Seat>(world.Nations.Select(n => new Seat(n.Id, SeatControl.Ai)).ToList()),
        Victory: new VictoryCondition(VictoryConditionType.TotalConquest),
        TurnLimit: null,
        BlindHotseat: false,
        RandomSeed: seed,
        Provenance: new ProvenanceMap(new List<ProvenanceEntry>
        {
            new("_group",
                $"designed: generated New Game scenario for world '{world.Id}' (MapGenerator t294f); "
                + "every seat starts AI, the seat screen's ticks choose the human seats."),
        }));
}
