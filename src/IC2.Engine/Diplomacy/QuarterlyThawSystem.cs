using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The quarterly diplomatic thaw (DoD 6, DoD 9) — <c>FUN_00451B40</c>'s relation loop
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: every negative
/// (cooldown) entry gains <c>+1</c>, and with probability <c>1/3</c> a further
/// <c>v = min(0, v + 3)</c> — subscribed to T06's <c>OnQuarterBoundary</c> hook
/// (<c>docs/task-catalogue.md</c> "The turn coordinator" note: "subscribed by T08's economy and T19's
/// thaw").
/// </summary>
/// <remarks>
/// <para>
/// <strong>One draw per pair, not one per nation-row.</strong> The original stores each nation's own
/// 16-entry row and its thaw loop walks rows independently, so a pair whose two physical copies both sit
/// under the column cap could in principle roll <em>twice</em> (once from each nation's own row) and
/// desynchronise the two copies of what this model treats as one symmetric cell. <see cref="DiplomaticRelations"/>
/// has no such duplication — one cell, one value, kept symmetric by <see cref="DiplomaticRelations.WithRelation"/>
/// — and DoD 1 requires the matrix to <em>stay</em> symmetric under every transition, so this system draws
/// exactly once per unordered pair <c>(i, j)</c>, <c>i &lt; j</c>, in <see cref="DiplomaticRelations.NationIds"/>
/// order (deterministic, and independent of how many nations the world has). <c>[derived]</c>: the source
/// report states the per-entry rule and the column bug, not whether two rows' independent rolls on the
/// same logical pair could ever disagree; searched the same report's quarterly-tick section and
/// <c>decompiled-quarterly-billing-and-economy.md</c> for a joint-roll description and found none.
/// </para>
/// <para>
/// <strong>DoD 9's column bug, restated as the report's own plain-English condition.</strong> The report:
/// "a cooldown between two nations both indexed &#8805; 8 never decays". Read per unordered pair rather
/// than per physical row (see above), that is exactly: skip the pair when <em>both</em> indices are at or
/// past <see cref="DiplomacyRules.FaithfulThawColumnLimit"/>; process it when at least one is under the
/// limit. <see cref="RulesetFlags.FaithfulThawColumnBug"/> selects it: <c>true</c> reproduces the bug,
/// <c>false</c> thaws every pair regardless of index.
/// </para>
/// <para>
/// Only negative entries draw at all — a pair already at or above zero is skipped entirely, drawing
/// nothing, which is also what lets the cooldown converge to exactly <c>0</c> and stop (DoD 6): once a
/// cell reaches <c>0</c> it is no longer negative and is never touched again.
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

        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                if (faithfulBug && i >= limit && j >= limit)
                {
                    continue;
                }

                var current = relations.Get(ids[i], ids[j]);
                if (current >= 0)
                {
                    continue;
                }

                var thawed = current + diplomacy.ThawPerQuarter;
                if (rng.NextChance(1, diplomacy.ThawBonusChanceDenominator))
                {
                    thawed = Math.Min(0, thawed + diplomacy.ThawBonus);
                }

                relations = relations.WithRelation(ids[i], ids[j], thawed);
            }
        }

        return state with { Relations = relations };
    }
}
