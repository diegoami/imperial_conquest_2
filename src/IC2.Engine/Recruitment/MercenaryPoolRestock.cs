using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// T56: the quarterly mercenary restock — <c>FUN_00449130</c>, decompiled in
/// <c>decompiled-mobilization-and-mercenary-restock.md</c> §6 and re-verified against turn-1 saves by
/// <c>decompiled-new-game-mercenary-fill.md</c>. One pass over the pool's
/// <see cref="RecruitmentRules.MercenaryPoolSlots"/> slots: an <em>empty</em> slot refills with
/// probability <c>46/54 ≈ 85%</c> (a <c>rand(6)</c> draw, falling through to the replace roll on a 5);
/// a <em>live offer</em> is replaced with probability <c>1/9</c>. A refill copies x, y, Label and type
/// wholesale from a random record of the world's template table, and randomizes only troops
/// (1.5×–3× the template base, capped at the type's standard battalion size) and quality (the
/// template's value or one above, clamped to 5–9).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Emptiness is absence, the model's own single rule.</strong> The original tests
/// <c>troops == 0xFFFF</c> in place; this engine already models an absent slot as a slot simply missing
/// from <see cref="GameState.MercenaryPool"/> (T13, <see cref="MercenaryPoolSlot"/>'s own remarks), and
/// this pass agrees with that model rather than introducing a second emptiness rule: a slot index with
/// no entry in the pool <em>is</em> the empty slot the original's <c>0xFFFF</c> marks.
/// </para>
/// <para>
/// <strong>Draws, exactly.</strong> Per slot: an empty slot draws <c>NextInt(6)</c>, then (only on a
/// draw above the refill ceiling) <c>NextInt(9)</c>; a live slot draws <c>NextInt(9)</c> only. A refill
/// then draws the template index, the troop roll and the quality roll — three more draws. A quarter over
/// a full pool therefore costs between 50 draws (no replacement) and 200 (every offer replaced); the
/// New Game fill over 50 empty slots costs about 186 on average (3.72 per slot,
/// decompiled-new-game-mercenary-fill.md §2).
/// </para>
/// <para>
/// <strong>Registration.</strong> <see cref="MercenaryPoolRestockSystem"/> subscribes to the quarter
/// boundary after the quarterly economy family, matching the original's call order
/// (<c>FUN_004514ec</c> calls <c>FUN_00451b40</c>'s quarterly tick, then <c>FUN_00449130</c>
/// immediately — order <c>[confirmed]</c>). See that handler's own remarks for the order assertion.
/// </para>
/// </remarks>
public static class MercenaryPoolRestock
{
    /// <summary>
    /// The stream name the New Game fill derives from the state's root seed — the fill's own stream, so
    /// it moves no other seeded outcome and no other New Game draw moves it (the original draws the fill
    /// first from the fresh seed; algorithm parity, not seed-for-seed parity, as with
    /// <see cref="Model.NewGameLeaders.StreamName"/>).
    /// </summary>
    public const string NewGameFillStreamName = "new-game.mercenary-fill";

    /// <summary>
    /// The New Game fill (bug #457): one restock pass over the 50 empty slots a fresh state starts with,
    /// from the state's own seed — the same single <c>FUN_00449130</c> call the original's
    /// <c>FUN_00448AA4</c> makes right after the DAT reload empties the pool
    /// <strong>[confirmed: decompiled-new-game-mercenary-fill.md §1]</strong>.
    /// </summary>
    /// <remarks>
    /// A world with no template table (the toy world) draws nothing and the pool stays empty, exactly as
    /// before this task existed. Called from <see cref="GameStateFactory.CreateInitial"/>, which Owns
    /// grants exactly this fill; a save import or a resume never runs it.
    /// </remarks>
    public static GameState FillNewGamePool(GameState state, Ruleset ruleset, World world)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);

        return Apply(state, ruleset, world, SplitMix64Rng.ForStream(state.RandomSeed, NewGameFillStreamName));
    }

    /// <summary>
    /// The pure restock pass, directly callable so a test can pin an exact result under a fixed stream
    /// without building a full quarter-boundary context (the same seam
    /// <c>Diplomacy.QuarterlyThawSystem.Apply</c> exposes).
    /// </summary>
    public static GameState Apply(GameState state, Ruleset ruleset, World world, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);

        if (world.MercenaryTemplates is not { Count: > 0 } templates)
        {
            // No table to draw from: the original always has one, but a world this engine loads is not
            // obliged to carry one (the toy world does not). Draw nothing, change nothing.
            return state;
        }

        var recruitment = ruleset.Recruitment;
        if (recruitment.MercenaryRestockTemplateDrawCount > templates.Count)
        {
            throw new InvalidOperationException(
                $"World '{world.Id}' carries {templates.Count} mercenary templates, fewer than the "
                + $"ruleset's draw bound of {recruitment.MercenaryRestockTemplateDrawCount}; the restock "
                + "would draw past the end of the table.");
        }

        var pool = state.MercenaryPool;
        var slots = new MercenaryPoolSlot[pool.Count];
        for (var i = 0; i < pool.Count; i++)
        {
            slots[i] = pool[i];
        }

        var changed = false;
        for (var slotIndex = 0; slotIndex < recruitment.MercenaryPoolSlots; slotIndex++)
        {
            var existing = FindSlot(slots, slotIndex);

            // The two probabilities are distinct and both pinned by the report: an empty slot's
            // rand(6) with its fallthrough, a live offer's rand(9). A pass that treated every slot
            // alike would be a different rule.
            var refill = existing is null
                ? rng.NextInt(recruitment.MercenaryRestockEmptyRollDenominator)
                    <= recruitment.MercenaryRestockEmptyRollRefillCeiling
                    || rng.NextInt(recruitment.MercenaryRestockReplaceRollDenominator)
                    >= recruitment.MercenaryRestockReplaceRollFloor
                : rng.NextInt(recruitment.MercenaryRestockReplaceRollDenominator)
                    >= recruitment.MercenaryRestockReplaceRollFloor;

            if (!refill)
            {
                continue;
            }

            var templateIndex = rng.NextInt(recruitment.MercenaryRestockTemplateDrawCount);
            var template = templates[templateIndex];

            var troopsBase = (template.TroopsBase * recruitment.MercenaryRestockTroopsScaleNumerator)
                / recruitment.MercenaryRestockTroopsScaleDenominator;
            if (troopsBase <= 0)
            {
                throw new InvalidOperationException(
                    $"World '{world.Id}' mercenary template at index {templateIndex} scales to a "
                    + $"non-positive troop base ({template.TroopsBase} troops); the troop roll cannot "
                    + "draw from it.");
            }

            var standardSize = ruleset.UnitTypeById(template.UnitTypeId)?.StandardBattalionSize
                ?? throw new InvalidOperationException(
                    $"World '{world.Id}' mercenary template at index {templateIndex} names unit type "
                    + $"'{template.UnitTypeId}', which the ruleset does not define.");

            var troops = Math.Min(
                troopsBase + rng.NextInt(troopsBase),
                standardSize);
            var quality = Math.Clamp(
                template.QualityBase
                    + recruitment.MercenaryRestockQualityRaise
                    - rng.NextInt(recruitment.MercenaryRestockQualityJitterSteps),
                recruitment.MercenaryRestockQualityFloor,
                recruitment.MercenaryRestockQualityCeiling);

            var slot = new MercenaryPoolSlot(
                SlotIndex: slotIndex,
                X: template.X,
                Y: template.Y,
                NameLabel: template.Label,
                UnitTypeId: template.UnitTypeId,
                Troops: troops,
                Quality: quality);

            if (existing is null)
            {
                Array.Resize(ref slots, slots.Length + 1);
                slots[^1] = slot;
            }
            else
            {
                var at = Array.IndexOf(slots, existing);
                slots[at] = slot;
            }

            changed = true;
        }

        if (!changed)
        {
            return state;
        }

        // The pool is rebuilt in slot-index order, so a restocked pool always round-trips through
        // save/load unchanged whatever order slots were hired out of.
        Array.Sort(slots, static (a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        return state with { MercenaryPool = ValueList<MercenaryPoolSlot>.Of(slots) };
    }

    private static MercenaryPoolSlot? FindSlot(MercenaryPoolSlot[] slots, int slotIndex)
    {
        foreach (var slot in slots)
        {
            if (slot.SlotIndex == slotIndex)
            {
                return slot;
            }
        }

        return null;
    }
}
