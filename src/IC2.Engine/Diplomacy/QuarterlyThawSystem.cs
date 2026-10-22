using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The quarterly diplomatic thaw (DoD 6, DoD 9), <c>FUN_00451B40</c>'s relation loop
/// <strong>[confirmed: code, <c>decompiled-war-cascade-and-peace-paths.md</c> "Detail of row 8", research
/// <c>df8b6a9</c>; seen in atlas increment 11]</strong>: every negative (cooldown) cell visited becomes
/// <c>v + 1</c>, or, when the 1-in-3 draw succeeds, <c>min(0, v + 3)</c> instead (#1339), written to both
/// sides of the pair. Subscribed to T06's <c>OnQuarterBoundary</c> hook.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The original's visits</strong> (<see cref="RulesetFlags.FaithfulThawColumnBug"/> <c>true</c>). The
/// loop walks rows <c>a = 0..N−1</c> and only columns <c>b = 0..7</c>
/// (<see cref="DiplomacyRules.FaithfulThawColumnLimit"/>), reads the directed cell <c>[a][b]</c>, and writes the
/// result through the pairwise setter, which stores <c>[a][b]</c> and <c>[b][a]</c>. So a pair with exactly one
/// index below 8 is visited once, from the high index's row; a pair with both below 8 is visited twice, the
/// second visit reading the first's result; and a pair with both at 8 or above is never visited. A
/// one-sided cooldown (after a rebirth) thaws only if its <c>[high][low]</c> cell is the negative one.
/// </para>
/// <para>
/// <strong>The fixed policy</strong> (<c>false</c>, <c>improved</c>): every unordered pair is visited once, in
/// <see cref="DiplomaticRelations.NationIds"/> order, regardless of index.
/// </para>
/// <para>
/// Only negative cells draw at all: a cell at or above zero is skipped, drawing nothing, which is also what
/// lets the cooldown converge to exactly <c>0</c> and stop (DoD 6).
/// </para>
/// </remarks>
[QuarterBoundaryHandler("diplomacy.quarterly-thaw")]
public sealed class QuarterlyThawSystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Apply(context.State, context.Ruleset, context.Rng);
    }

    /// <summary>
    /// The pure thaw pass, directly callable so a test can pin an exact result under a fixed seed without
    /// building a full quarter-boundary context.
    /// </summary>
    public static GameState Apply(GameState state, Ruleset ruleset, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(rng);

        var diplomacy = ruleset.Diplomacy;
        var ids = state.Relations.NationIds;
        var relations = state.Relations;
        var limit = diplomacy.FaithfulThawColumnLimit;
        var faithfulBug = ruleset.Flags.FaithfulThawColumnBug;

        if (faithfulBug)
        {
            var columns = Math.Min(ids.Count, limit);
            for (var a = 0; a < ids.Count; a++)
            {
                for (var b = 0; b < columns; b++)
                {
                    if (a != b)
                    {
                        relations = Visit(relations, ids[a], ids[b], diplomacy, rng);
                    }
                }
            }
        }
        else
        {
            for (var i = 0; i < ids.Count; i++)
            {
                for (var j = i + 1; j < ids.Count; j++)
                {
                    relations = Visit(relations, ids[i], ids[j], diplomacy, rng);
                }
            }
        }

        return state with { Relations = relations };
    }

    /// <summary>One visit: a negative <c>[a][b]</c> becomes <c>v + 1</c>, or <c>min(0, v + 3)</c> on the bonus draw.</summary>
    private static DiplomaticRelations Visit(
        DiplomaticRelations relations, string a, string b, DiplomacyRules diplomacy, IRng rng)
    {
        var current = relations.Get(a, b);
        if (current >= 0)
        {
            return relations;
        }

        var thawed = rng.NextChance(1, diplomacy.ThawBonusChanceDenominator)
            ? Math.Min(0, current + diplomacy.ThawBonus)
            : current + diplomacy.ThawPerQuarter;
        return relations.WithRelation(a, b, thawed);
    }
}
