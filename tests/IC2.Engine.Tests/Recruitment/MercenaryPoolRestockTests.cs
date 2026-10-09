using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Fixtures;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T56's quarterly mercenary restock: registration order (Done-when 1), the two distinct probabilities
/// (Done-when 2), the wholesale template copy with randomized troops and quality (Done-when 3), the
/// agreement with the model's own absence rule for empty slots (Done-when 4), and the 50-slot cap with
/// the save round-trip (Done-when 5). The New Game fill (Done-when 9) and the long-game reachability
/// (Done-when 6) live in <see cref="MercenaryPoolNewGameFillTests"/>; the table's own export from the
/// DAT (Done-when 10) in <see cref="MercenaryTemplateExportTests"/>.
/// </summary>
/// <remarks>
/// The rule's numbers are the shipped classical preset's own (the same values toy-ruleset.json carries):
/// rand(6) with a refill ceiling of 4 on an empty slot, rand(9) with a replace floor of 8 on a live
/// offer, a draw bound of 200 over the world's 201 templates, the 3/2 troop scale, and the 5–9 quality
/// clamp — every one transcribed into <see cref="FixtureCorpus"/> under
/// <c>mercenary.restock.*</c> from decompiled-mobilization-and-mercenary-restock.md §6 and
/// decompiled-new-game-mercenary-fill.md §2, and asserted against the corpus here so a ruleset edit and
/// the report can never silently disagree.
/// </remarks>
public sealed class MercenaryPoolRestockTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    /// <summary>An empty-pool starting state over the classical world (the fill disabled by clearing it).</summary>
    private static GameState EmptyPoolState() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario)
            with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };

    /// <summary>
    /// A live offer on every slot index, so a pass has something to replace — the starting point
    /// Done-when 5's cap and round-trip checks run from. Distinct per index so a replacement is visible.
    /// </summary>
    private static GameState FullPoolState()
    {
        var templates = Classical.World.MercenaryTemplates!;
        var slots = new MercenaryPoolSlot[Classical.Ruleset.Recruitment.MercenaryPoolSlots];
        for (var i = 0; i < slots.Length; i++)
        {
            var template = templates[i % templates.Count];
            slots[i] = new MercenaryPoolSlot(
                SlotIndex: i,
                X: template.X,
                Y: template.Y,
                NameLabel: template.Label,
                UnitTypeId: template.UnitTypeId,
                Troops: 100 + i,
                Quality: 5);
        }

        return EmptyPoolState() with { MercenaryPool = ValueList<MercenaryPoolSlot>.Of(slots) };
    }

    /// <summary>
    /// Runs one restock pass whose script leaves every slot but <paramref name="slotIndex"/> untouched
    /// (first roll 5 — the fall-through — then a replace roll of 0, which keeps any slot), giving
    /// <paramref name="slotScript"/>'s own draws to that one slot in order. For a state whose slots are
    /// all <em>live</em>, use <see cref="RestockOneLiveSlot"/> instead: a live slot draws only the
    /// replace roll, so the untouched slots' filler is one value each.
    /// </summary>
    private static GameState RestockOneSlot(GameState state, int slotIndex, params int[] slotScript) =>
        RestockOneSlot(state, slotIndex, slotIndex, slotScript, livePool: false);

    /// <summary>
    /// The live-pool twin of <see cref="RestockOneSlot"/>: every untouched slot draws one replace roll
    /// of 7 (below the floor) and keeps its offer.
    /// </summary>
    private static GameState RestockOneLiveSlot(GameState state, int slotIndex, params int[] slotScript) =>
        RestockOneSlot(state, slotIndex, slotIndex, slotScript, livePool: true);

    private static GameState RestockOneSlot(GameState state, int slotIndex, int fillerPerSlot, int[] slotScript, bool livePool)
    {
        var recruitment = Classical.Ruleset.Recruitment;
        var script = new List<int>();
        for (var i = 0; i < recruitment.MercenaryPoolSlots; i++)
        {
            if (i == slotIndex)
            {
                script.AddRange(slotScript);
            }
            else if (livePool)
            {
                script.Add(7); // replace roll 7 < 8: the live offer stays.
            }
            else
            {
                script.Add(5); // first roll: fall through to the replace roll.
                script.Add(0); // replace roll 0 < 8: the slot keeps whatever it had (nothing).
            }
        }

        return MercenaryPoolRestock.Apply(state, Classical.Ruleset, Classical.World, new ScriptedRestockRng(script.ToArray()));
    }

    // ---- Done-when 1: registered at the same boundary, after the quarterly economy ----

    [Fact]
    public void The_restock_is_registered_on_the_quarter_boundary_after_every_economy_handler()
    {
        var handlers = SystemRegistry.FromEngineAssembly().QuarterBoundaryHandlers;
        var ids = handlers.Select(h => h.Id).ToList();

        var restock = Assert.Single(ids, id => id == "recruitment.mercenary-restock");

        // The original calls FUN_00449130 immediately after the quarterly economy FUN_00451b40
        // (decompiled-mobilization-and-mercenary-restock.md §6, order [confirmed]). This engine's
        // quarterly tick is split into the four handlers below, so "after the economy, not before" is
        // asserted as: the restock's position is past every one of them. The assertion is on the
        // registration order itself, not on any effect the handler happens to have.
        foreach (var economyHandler in new[]
                 {
                     "economy.quarterly-billing",
                     "economy.quarterly-city-tick",
                     "economy.quarterly-nation-tick",
                     "economy.ai-deposition",
                 })
        {
            Assert.True(
                ids.IndexOf(economyHandler) < ids.IndexOf(restock),
                $"'{restock}' must run after '{economyHandler}'; the handler order is {string.Join(", ", ids)}.");
        }
    }

    // ---- Done-when 2: the two probabilities are distinct and both pinned ----

    [Fact]
    public void An_empty_slot_refills_on_a_first_roll_at_or_below_the_ceiling()
    {
        // Template 16 is one of the three whose scaled base already reaches its type's standard
        // battalion size (heavy_infantry 4000 -> base 6000 = the 6000 cap), so this slot also pins the
        // cap: troops must come out exactly 6000 whatever the troop roll draws.
        var template = Classical.World.MercenaryTemplates![16];
        var state = RestockOneSlot(EmptyPoolState(), 0, 0, 16, 5999, 0);

        var slot = Assert.Single(state.MercenaryPool);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(template.X, slot.X);
        Assert.Equal(template.Y, slot.Y);
        Assert.Equal(template.Label, slot.NameLabel);
        Assert.Equal(template.UnitTypeId, slot.UnitTypeId);
        Assert.Equal(6000, slot.Troops); // min(6000 + 5999, 6000): the cap wins.
        Assert.Equal(9, slot.Quality); // clamp(8 + 1 - 0, 5, 9).
    }

    [Fact]
    public void An_empty_slot_falls_through_to_the_replace_roll_and_refills_only_at_its_floor()
    {
        var template = Classical.World.MercenaryTemplates![0]; // light_infantry, base 3000 -> 4500.
        var state = RestockOneSlot(EmptyPoolState(), 0, 5, 8, 0, 4499, 1);

        var slot = Assert.Single(state.MercenaryPool);
        Assert.Equal(template.X, slot.X);
        Assert.Equal(template.Y, slot.Y);
        Assert.Equal(template.Label, slot.NameLabel);
        Assert.Equal(template.UnitTypeId, slot.UnitTypeId);
        Assert.Equal(8999, slot.Troops); // 4500 + 4499, under the 15000 cap: the full 1.5x-3x band.
        Assert.Equal(7, slot.Quality); // clamp(7 + 1 - 1, 5, 9).
    }

    [Fact]
    public void An_empty_slot_stays_empty_when_both_of_its_rolls_refuse()
    {
        var state = RestockOneSlot(EmptyPoolState(), 0, 5, 7);

        Assert.Empty(state.MercenaryPool);
    }

    [Fact]
    public void A_live_offer_is_replaced_at_the_replace_roll_floor()
    {
        var before = FullPoolState();
        var template = Classical.World.MercenaryTemplates![1]; // light_infantry, base 4000 -> 6000.
        var state = RestockOneLiveSlot(before, 7, 8, 1, 0, 0);

        var replaced = Assert.Single(state.MercenaryPool, s => s.SlotIndex == 7);
        Assert.Equal(template.X, replaced.X);
        Assert.Equal(template.Y, replaced.Y);
        Assert.Equal(template.Label, replaced.NameLabel);
        Assert.Equal(template.UnitTypeId, replaced.UnitTypeId);
        Assert.Equal(6000, replaced.Troops); // 6000 + 0, under the 15000 cap.
        Assert.Equal(7, replaced.Quality); // clamp(6 + 1 - 0, 5, 9).

        // Every other offer is untouched, record-identical -- a pass that rewrote more than the one
        // replaced slot would not leave these standing.
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.SlotIndex != 7)
            {
                Assert.Equal(before.MercenaryPool.Single(s => s.SlotIndex == slot.SlotIndex), slot);
            }
        }
    }

    [Fact]
    public void A_live_offer_survives_a_replace_roll_below_the_floor()
    {
        var before = FullPoolState();
        var state = RestockOneLiveSlot(before, 7, 7);

        // Below the floor nothing changes at all: the pass returns the state itself.
        Assert.Same(before, state);
    }

    [Fact]
    public void The_restock_constants_match_their_corpus_entries()
    {
        // The ruleset's own values, cross-checked against the T04 corpus so a future ruleset edit that
        // drifts from the reports fails here rather than only in the golden transcripts.
        var r = Classical.Ruleset.Recruitment;
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.emptyRollDenominator").AsInt(), r.MercenaryRestockEmptyRollDenominator);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.emptyRollRefillCeiling").AsInt(), r.MercenaryRestockEmptyRollRefillCeiling);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.replaceRollDenominator").AsInt(), r.MercenaryRestockReplaceRollDenominator);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.replaceRollFloor").AsInt(), r.MercenaryRestockReplaceRollFloor);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.templateDrawCount").AsInt(), r.MercenaryRestockTemplateDrawCount);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.troopsScaleNumerator").AsInt(), r.MercenaryRestockTroopsScaleNumerator);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.troopsScaleDenominator").AsInt(), r.MercenaryRestockTroopsScaleDenominator);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.qualityFloor").AsInt(), r.MercenaryRestockQualityFloor);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.qualityCeiling").AsInt(), r.MercenaryRestockQualityCeiling);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.qualityRaise").AsInt(), r.MercenaryRestockQualityRaise);
        Assert.Equal(FixtureCorpus.Get("mercenary.restock.qualityJitterSteps").AsInt(), r.MercenaryRestockQualityJitterSteps);
    }

    // ---- Done-when 3: the wholesale template copy, over a real seeded pass ----

    [Fact]
    public void A_seeded_pass_draws_every_offer_wholesale_from_the_template_table()
    {
        var templates = Classical.World.MercenaryTemplates!;
        var state = MercenaryPoolRestock.Apply(
            EmptyPoolState(),
            Classical.Ruleset,
            Classical.World,
            SplitMix64Rng.ForStream(0x7356, "test.restock"));

        Assert.NotEmpty(state.MercenaryPool);
        foreach (var slot in state.MercenaryPool)
        {
            // x, y, Label and type come from the template wholesale: some template shares the exact
            // four-field key. Only troops and quality may differ, and only inside their bands.
            var candidates = templates.Where(t =>
                    t.X == slot.X && t.Y == slot.Y && t.Label == slot.NameLabel && t.UnitTypeId == slot.UnitTypeId)
                .ToList();
            Assert.NotEmpty(candidates);

            var standardSize = Classical.Ruleset.UnitTypeById(slot.UnitTypeId)!.StandardBattalionSize;
            Assert.True(
                candidates.Any(t =>
                {
                    var slotBandMin = Math.Min((t.TroopsBase * 3) / 2, standardSize);
                    var slotBandMax = Math.Min(2 * ((t.TroopsBase * 3) / 2) - 1, standardSize);
                    var qualityBand = new[]
                    {
                        Math.Clamp(t.QualityBase, 5, 9),
                        Math.Clamp(t.QualityBase + 1, 5, 9),
                    };
                    return slot.Troops >= slotBandMin && slot.Troops <= slotBandMax && qualityBand.Contains(slot.Quality);
                }),
                $"Slot {slot.SlotIndex} ({slot.UnitTypeId}, {slot.Troops} troops, quality {slot.Quality}) fits no template "
                + "with the same (x, y, Label, type) key inside the troops and quality bands.");

            // Quality is always clamped to 5-9 (DoD 3's own wording: it matches every offer observed
            // in the corpus), and an offer is never empty.
            Assert.InRange(slot.Quality, 5, 9);
            Assert.True(slot.Troops > 0, "A restocked offer is never an empty record.");
        }

        // DoD 3's T76 clause: a refilled slot sits on its template's city tile.
        foreach (var slot in state.MercenaryPool)
        {
            Assert.Contains(Classical.World.Cities, c => c.X == slot.X && c.Y == slot.Y);
        }
    }

    // ---- Done-when 4: agreement with the model's own single emptiness rule ----

    [Fact]
    public void The_restock_treats_an_absent_slot_as_empty_and_writes_no_sentinel_of_its_own()
    {
        // Slot 3 is absent from the pool (T13's "empty slots are simply absent" -- the model's own
        // reading of the original's in-place 0xFFFF test); the pass fills exactly it.
        var state = RestockOneSlot(EmptyPoolState(), 3, 0, 2, 0, 0);

        var slot = Assert.Single(state.MercenaryPool);
        Assert.Equal(3, slot.SlotIndex);

        // And the fill is a real offer, never a sentinel record: the pass has no emptiness rule of its
        // own to write, so nothing it produces can read as empty afterwards.
        Assert.NotEqual(0xFFFF, slot.Troops);
        Assert.True(slot.Troops > 0);
    }

    [Fact]
    public void A_world_without_a_template_table_draws_nothing()
    {
        // The toy world carries no mercenaryTemplates; the pass must leave any pool it is given alone
        // rather than drawing from a table that does not exist.
        var toy = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city");
        var state = RecruitmentTestbed.InitialState();

        var result = MercenaryPoolRestock.Apply(state, toy.Ruleset, toy.World, SplitMix64Rng.ForStream(1, "unused"));
        Assert.Same(state, result);
    }

    // ---- Done-when 5: the cap and the round-trip ----

    [Fact]
    public void A_restocked_full_pool_never_exceeds_its_slot_count_and_round_trips_through_save_load()
    {
        var before = FullPoolState();
        var state = MercenaryPoolRestock.Apply(
            before,
            Classical.Ruleset,
            Classical.World,
            SplitMix64Rng.ForStream(0x7356, "test.full-pool"));

        Assert.InRange(state.MercenaryPool.Count, 1, Classical.Ruleset.Recruitment.MercenaryPoolSlots);

        // Slot indexes stay exactly the pool's own 0..49, each once: a restock replaces offers in
        // place and never appends past the cap.
        Assert.Equal(
            Enumerable.Range(0, Classical.Ruleset.Recruitment.MercenaryPoolSlots),
            state.MercenaryPool.Select(s => s.SlotIndex));

        // Save/load: the restocked pool is a fully serializable tree that deep-equals its reload,
        // unchanged by the trip (the same round-trip GameStateSerializationTests pins for a hand-built
        // state).
        var reloaded = GameDataLoader.Load<GameState>("restocked-state.json", GameJson.Serialize(state));
        Assert.Equal(state, reloaded);
        Assert.Equal(GameJson.Serialize(state), GameJson.Serialize(reloaded));
    }

    private sealed class ScriptedRestockRng : IRng
    {
        private readonly Queue<int> _values;

        public ScriptedRestockRng(params int[] values) => _values = new Queue<int>(values);

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

        public int NextInt(int exclusiveUpperBound)
        {
            var value = _values.Dequeue();
            Assert.True(value >= 0 && value < exclusiveUpperBound,
                $"Scripted value {value} is outside [0, {exclusiveUpperBound}).");
            return value;
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
            throw new NotSupportedException("Not scripted for this test.");

        public bool NextChance(int numerator, int denominator) =>
            throw new NotSupportedException("Not scripted for this test.");

        public IRng ForStream(string streamName) => throw new NotSupportedException("Not scripted for this test.");
    }
}
