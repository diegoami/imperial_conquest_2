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
/// <c>docs/tasks/T24.md</c>'s own binding instruction: "read what actually differs between
/// <c>data/rulesets/classical-faithful.json</c> and <c>improved.json</c> (jq: <c>.flags</c>,
/// <c>.combat.onDefeat</c>, <c>.victory</c>, <c>.diplomacy.model</c> and similar) and describe only real
/// differences." Every line below was read directly off those two files (2026-09-28), not assumed.
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

    public static readonly RulesetCard ClassicalFaithful = new(
        RulesetPreset.ClassicalFaithful,
        RulesetId: "classical-faithful",
        ScenarioId: "classical-mediterranean",
        DisplayName: "Classical Faithful",
        Summary: new[]
        {
            "Diplomacy: the confirmed state machine only — no AI opinion-score layer (flags.diplomacyModel: confirmedStateMachine).",
            "Victory: total conquest — you must hold every city on the map (victory.defaultCondition: totalConquest).",
            "Human vs AI: reproduces the original's asymmetric seat rules exactly as coded (flags.seatAsymmetry: faithful).",
            "Bugs: reproduces two confirmed decompiled bugs — the 8-column thaw bug and the 16-bit siege-ratio clamp.",
            "A defeated army or fleet is destroyed outright (combat.onDefeat: destroyed).",
        });

    public static readonly RulesetCard Improved = new(
        RulesetPreset.Improved,
        RulesetId: "improved",
        ScenarioId: "example-classical-improved",
        DisplayName: "Improved",
        Summary: new[]
        {
            "Diplomacy: the confirmed state machine plus an AI opinion-score layer (flags.diplomacyModel: confirmedStateMachineWithOpinionScore).",
            "Victory: domination or score at a 150-turn limit, your choice (victory.defaultCondition: domination).",
            "Human vs AI: the same rules apply to every seat, human or AI (flags.seatAsymmetry: normalized).",
            "Bugs: the thaw-column and siege-ratio-clamp bugs are both fixed.",
            "A defeated army or fleet survives at reduced strength and scatters, so it can be pursued later (combat.onDefeat: scatter).",
        });

    /// <summary>Both cards, in the order the chooser renders them — Classical Faithful first.</summary>
    public static readonly IReadOnlyList<RulesetCard> Cards = new[] { ClassicalFaithful, Improved };

    public static RulesetCard CardFor(RulesetPreset preset) =>
        preset == RulesetPreset.ClassicalFaithful ? ClassicalFaithful : Improved;
}
