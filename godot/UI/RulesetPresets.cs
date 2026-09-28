namespace IC2.Slice.UI;

/// <summary>
/// T24 "the ruleset chooser as the flow's first, most prominent screen" (<c>docs/tasks/T24.md</c> Scope;
/// <c>docs/game-design.md</c> §"User interface" item 1): the two rulesets New Game lets a player choose
/// between, before scenario or seat selection is ever shown.
/// </summary>
/// <remarks>
/// Deliberately <strong>Godot-free</strong> (no <c>using Godot</c>) so it can be exercised by a plain
/// xunit test in <c>tests/IC2.Engine.Tests/Ui/</c> — the same seam T48's
/// <c>godot/Assets/AssetKeyResolver.cs</c> already established (see that file's own remarks and
/// <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>'s <c>&lt;Compile Include&gt;</c> for it).
/// </remarks>
public enum RulesetPreset
{
    ClassicalFaithful,
    Improved,
}

/// <summary>
/// One ruleset card's content: which shipped ruleset/scenario it resolves to, and the plain-language
/// summary of what it changes. <see cref="Summary"/> lists exactly the five dimensions
/// <c>docs/tasks/T24.md</c> asks for ("diplomacy, victory condition, human/AI symmetry, bug
/// reproduction, and ... what happens to a defeated army"), each line read off the real difference
/// between <c>data/rulesets/classical-faithful.json</c> and <c>data/rulesets/improved.json</c> — never
/// an invented claim. See <see cref="RulesetPresets"/> for the citations.
/// </summary>
public sealed record RulesetCard(
    RulesetPreset Preset,
    string RulesetId,
    string ScenarioId,
    string DisplayName,
    IReadOnlyList<string> Summary);

/// <summary>
/// The two shipped cards New Game's ruleset chooser renders, and which scenario each one bootstraps —
/// this task's own binding instruction: read what actually differs between
/// <c>data/rulesets/classical-faithful.json</c> and <c>improved.json</c> (real paths:
/// <c>.flags.combatOnDefeat</c>, <c>.flags.diplomacyModel</c>, <c>.flags.seatAsymmetry</c>,
/// <c>.victory.defaultCondition</c> and similar — every field the shipped rulesets actually declare
/// lives under <c>.flags</c> or <c>.victory</c>, never a top-level <c>.combat</c> or <c>.diplomacy</c>)
/// and describe only real differences. Every line below was read directly off those two files
/// (2026-09-28), then re-checked against the code path that reads each field (rework round 1, PR #466
/// review) rather than the field's mere presence — see <see cref="ClassicalFaithful"/>'s own remarks for
/// what that re-check changed.
/// </summary>
/// <remarks>
/// <strong>Why each card names a fixed scenario id, not merely a ruleset id.</strong> A <see cref="IC2.Engine.Model.Scenario"/>
/// declares its own <c>rulesetId</c> (<c>IC2.Engine.Serialization.GameDataRepository.Resolve</c> reads
/// the ruleset <em>through</em> the scenario, never independently) — there is no "load this world under
/// a chosen ruleset" seam in the engine to swap. <c>data/scenarios/classical-mediterranean.json</c> and
/// <c>data/scenarios/example-classical-improved.json</c> are the two shipped scenarios that put the same
/// world (<c>classical-mediterranean</c>) under each of the two rulesets respectively, so picking a card
/// here picks a scenario id, and the scenario's own <c>rulesetId</c> is what actually determines which
/// <see cref="IC2.Engine.Model.Ruleset"/> <see cref="IC2.Engine.Presentation.GameSession"/> loads —
/// exactly what <c>docs/tasks/T24.md</c> Done-when 4 requires ("picking either value is what the
/// scenario bootstrap actually reads, not a cosmetic control disconnected from the loaded Ruleset").
/// </remarks>
public static class RulesetPresets
{
    /// <summary><c>docs/tasks/T24.md</c> Scope: "Classical Faithful is the default highlight."</summary>
    public const RulesetPreset Default = RulesetPreset.ClassicalFaithful;

    /// <remarks>
    /// <para>
    /// <strong>Rework round 1 (PR #466 review, blocking finding 2; also finding 3): every line below was
    /// re-checked against both the JSON field value AND the code path that actually reads it, not the
    /// field alone</strong> — see <c>tests/IC2.Engine.Tests/Ui/RulesetPresetsTests.cs</c> for the pinned
    /// proof of each one. Two corrections:
    /// </para>
    /// <list type="bullet">
    /// <item><description><strong>The diplomacy line, both cards.</strong> <c>flags.diplomacyModel</c> is
    /// declared on <see cref="IC2.Engine.Model.RulesetFlags"/> but read by no consumer anywhere in
    /// <c>src/IC2.Engine</c> (confirmed: <c>grep -rn "DiplomacyModel" src/IC2.Engine</c> hits only its own
    /// declaration in <c>Ruleset.cs</c>) — there is no AI opinion-score layer implemented for either
    /// ruleset to differ by today. The previous wording stated the intended difference as though it were
    /// live behaviour; both lines now say the flag is recorded but not yet wired to anything.</description></item>
    /// <item><description><strong>The Improved card's victory line.</strong> The live turn pipeline
    /// (<see cref="IC2.Engine.Victory.VictoryCheckSystem.Execute"/>) always calls
    /// <see cref="IC2.Engine.Victory.VictoryEvaluator.Evaluate"/> with <c>scenario: null</c>, which
    /// evaluates exactly <c>ruleset.Victory.DefaultCondition</c> — for <c>improved.json</c> that is a
    /// single fixed <c>domination</c>, never <c>scoreAtTurnLimit</c>. <c>victory.defaultTurnLimit</c>
    /// (150) is read only inside <see cref="IC2.Engine.Victory.VictoryEvaluator.EvaluateScoreAtTurnLimit"/>,
    /// which that fixed <c>domination</c> default never reaches — so the previous "domination or score at
    /// a 150-turn limit, your choice" was false: there is no victory-condition picker anywhere in this
    /// PR's own screens (grepped <c>godot/UI</c> for <c>Victory</c>/<c>victory</c>: the only hit is this
    /// file), and the shipped data's own turn limit is dead for the condition the live pipeline actually
    /// evaluates. The line now states only the condition that is actually live.</description></item>
    /// </list>
    /// <para>
    /// <strong>Path labels (finding 3, non-blocking): <c>flags.combatOnDefeat</c>, not
    /// <c>combat.onDefeat</c></strong> — the field lives under the ruleset's <c>flags</c> object (jq
    /// confirms: <c>jq '.flags.combatOnDefeat' data/rulesets/classical-faithful.json</c>), never a
    /// top-level <c>combat</c> object. Every citation below now names the real path.
    /// </para>
    /// </remarks>
    public static readonly RulesetCard ClassicalFaithful = new(
        RulesetPreset.ClassicalFaithful,
        RulesetId: "classical-faithful",
        ScenarioId: "classical-mediterranean",
        DisplayName: "Classical Faithful",
        Summary: new[]
        {
            "Diplomacy: recorded as the confirmed state machine (flags.diplomacyModel: confirmedStateMachine) — no consumer in the engine reads this flag yet, so it makes no live difference from Improved today.",
            "Victory: total conquest — you must hold every city on the map (victory.defaultCondition: totalConquest).",
            "Human vs AI: reproduces the original's asymmetric seat rules exactly as coded (flags.seatAsymmetry: faithful).",
            "Bugs: reproduces two confirmed decompiled bugs — the 8-column thaw bug and the 16-bit siege-ratio clamp.",
            "A defeated army or fleet is destroyed outright (flags.combatOnDefeat: destroyed).",
        });

    public static readonly RulesetCard Improved = new(
        RulesetPreset.Improved,
        RulesetId: "improved",
        ScenarioId: "example-classical-improved",
        DisplayName: "Improved",
        Summary: new[]
        {
            "Diplomacy: recorded as intending an AI opinion-score layer on top of the state machine (flags.diplomacyModel: confirmedStateMachineWithOpinionScore) — not implemented anywhere in the engine yet, so diplomacy behaves the same as Classical Faithful today.",
            "Victory: domination — hold every city belonging to any nation still at war with you (victory.defaultCondition: domination). The ruleset's own 150-turn score limit is recorded but not evaluated by the live turn pipeline for this default, and no in-game choice between the two exists yet.",
            "Human vs AI: the same rules apply to every seat, human or AI (flags.seatAsymmetry: normalized).",
            "Bugs: the thaw-column and siege-ratio-clamp bugs are both fixed.",
            "A defeated army or fleet survives at reduced strength and scatters, so it can be pursued later (flags.combatOnDefeat: scatter).",
        });

    /// <summary>Both cards, in the order the chooser renders them — Classical Faithful first.</summary>
    public static readonly IReadOnlyList<RulesetCard> Cards = new[] { ClassicalFaithful, Improved };

    public static RulesetCard CardFor(RulesetPreset preset) =>
        preset == RulesetPreset.ClassicalFaithful ? ClassicalFaithful : Improved;
}
