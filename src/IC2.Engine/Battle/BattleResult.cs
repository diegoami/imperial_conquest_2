using IC2.Engine.Model;

namespace IC2.Engine.Battle;

/// <summary>Which of the three instant-resolver variants produced a <see cref="BattleResult"/>.</summary>
public enum BattleKind
{
    /// <summary>Army versus army on the strategic map — <c>FUN_0044AEE4</c>.</summary>
    Field,

    /// <summary>Army versus city — <c>FUN_0044B27C</c>'s strength comparison.</summary>
    Siege,

    /// <summary>Fleet versus fleet — <c>FUN_0044B5D0</c>.</summary>
    Naval,
}

/// <summary>Which side of a battle a value refers to.</summary>
public enum BattleSide
{
    /// <summary>The side that initiated the attack.</summary>
    Attacker,

    /// <summary>The side that was attacked. Ties resolve here.</summary>
    Defender,
}

/// <summary>What became of the losing army or fleet.</summary>
public enum LoserFate
{
    /// <summary>
    /// Annihilated — the confirmed outcome, and the only one under
    /// <see cref="DefeatOutcome.Destroyed"/>.
    /// </summary>
    Destroyed,

    /// <summary>
    /// Survived at reduced strength and relocated — the <c>improved</c> ruleset's
    /// <see cref="DefeatOutcome.Scatter"/> outcome.
    /// </summary>
    Scattered,

    /// <summary>
    /// Neither: the siege variant never destroys or relocates anything. A siege's city outcome
    /// (capture, defection, or a repulsed attack) belongs to T17, and <c>combat.onDefeat</c> never
    /// touches it — see <see cref="InstantBattleResolver.ResolveSiege"/>.
    /// </summary>
    Unaffected,
}

/// <summary>One of the winner's unit slots and what the battle cost it.</summary>
/// <param name="SlotIndex">The unit's index in its army's slot list, before any slot was removed.</param>
/// <param name="UnitName">The unit's display name, so a presentation layer needs no second lookup.</param>
/// <param name="TroopsBefore">The unit's troop count before the battle.</param>
/// <param name="TroopsLost">The troops this unit lost.</param>
public sealed record UnitCasualty(int SlotIndex, string UnitName, int TroopsBefore, int TroopsLost);

/// <summary>One of the winner's unit slots and the quality tier it ended the battle at.</summary>
/// <param name="SlotIndex">The unit's index in its army's slot list.</param>
/// <param name="UnitName">The unit's display name.</param>
/// <param name="QualityBefore">The tier the unit held before the battle.</param>
/// <param name="QualityAfter">The tier it holds after the floor and the 1-in-4 roll.</param>
/// <param name="PromotedByRoll">
/// Whether the <em>further</em> 1-in-4 promotion fired for this unit, as distinct from merely being
/// raised to the <see cref="CombatRules.QualityFloor"/>. DoD 5 asserts exactly which units this is
/// true for under the fixed seed, which a bare before/after pair cannot express (a unit already at
/// the floor that wins the roll and a unit below the floor that loses it both move one tier).
/// </param>
public sealed record UnitPromotion(
    int SlotIndex,
    string UnitName,
    int QualityBefore,
    int QualityAfter,
    bool PromotedByRoll);

/// <summary>Where a scattered survivor went.</summary>
/// <param name="FromX">The tile it left — its own tile, where it stood when the battle resolved.</param>
/// <param name="FromY">See <paramref name="FromX"/>.</param>
/// <param name="ToX">The tile it now occupies.</param>
/// <param name="ToY">See <paramref name="ToX"/>.</param>
/// <param name="RequestedDistance">
/// The distance drawn from <see cref="ScatteredDefeatRules.ScatterTilesMin"/>..<see cref="ScatteredDefeatRules.ScatterTilesMax"/>.
/// </param>
/// <param name="ActualDistance">
/// The distance actually used, after shrinking one tile at a time past any fully-blocked ring
/// (<c>docs/game-design.md</c> §"The defeated side's fate"). Equal to
/// <paramref name="RequestedDistance"/> when the first ring had room.
/// </param>
public sealed record ScatterOutcome(
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    int RequestedDistance,
    int ActualDistance);

/// <summary>
/// Everything one resolved battle produced — the single, presentation-agnostic value
/// <c>docs/game-design.md</c> §Combat and <c>docs/task-catalogue.md</c> T16 require.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Presentation-agnostic, deliberately.</strong> T24's battle-result screen reads this record;
/// so would a future optional "detailed" battle presentation. Nothing here is a rendered string, a
/// colour, a sprite key or a sentence — the news text lives in <c>NewsMessageCatalog</c>, keyed off the
/// events this resolver publishes, and the screen composes its own wording from these fields. That is
/// what lets the screen change without the resolver changing.
/// </para>
/// <para>
/// <strong>What is deliberately absent.</strong> There is no per-unit-type attrition table, because the
/// instant resolver does not produce one — it annihilates the loser wholesale
/// (<c>design-audit.md</c> Q1). The Rome/Gaul per-type numbers came from the *tactical* path and are not
/// reproducible here; they are held in reserve for a possible future detailed resolver, along with the
/// type-effectiveness matrix, the melee caps, the tactical morale array and the rout mechanic. None of
/// those appear anywhere in this namespace.
/// </para>
/// </remarks>
/// <param name="Kind">Which variant resolved this battle.</param>
/// <param name="AttackerId">The attacking army's or fleet's id.</param>
/// <param name="DefenderId">The defending army's or fleet's id, or the besieged city's id.</param>
/// <param name="AttackerNationId">The attacking nation.</param>
/// <param name="DefenderNationId">The defending nation — for a siege, the city's owner.</param>
/// <param name="AttackerPower">The attacker's strength, including any random band already applied.</param>
/// <param name="DefenderPower">The defender's strength, on the same terms.</param>
/// <param name="Winner">Which side won. An exact tie is a <see cref="BattleSide.Defender"/> win.</param>
/// <param name="AppliedDefeatOutcome">
/// The <c>combat.onDefeat</c> setting this battle was resolved under, or <see langword="null"/> for a
/// siege, which the flag never reaches (DoD 12).
/// </param>
/// <param name="LoserFate">What became of the losing army or fleet.</param>
/// <param name="WinnerCasualties">
/// Troops the winner lost: the total of the per-slot losses <see cref="BattleCasualties.Apply"/> produced
/// from the <c>loserPower × 40 / winnerPower</c> <em>ratio</em>. Not the ratio itself — the ratio is
/// recoverable from <see cref="WinnerPower"/> and <see cref="LoserPower"/>, which this record already
/// carries, so nothing needs a second field for it.
/// </param>
/// <param name="LoserCasualties">
/// Troops the loser lost — or, for a naval battle, ships. The whole force under
/// <see cref="LoserFate.Destroyed"/>; under <see cref="LoserFate.Scattered"/> the total the mirrored ratio
/// took through the same per-unit expression, or, for a fleet, the hulls it cost.
/// </param>
/// <param name="UnitCasualties">
/// The per-slot losses of whichever force this battle actually damaged: the winner in a field or naval
/// battle, and the <em>attacking</em> army in a siege, whose attrition the original applies on every
/// attempt, win or lose (<c>tests/fixtures/corpus.json</c> <c>siege.attritionEveryAttempt</c>).
/// </param>
/// <param name="Promotions">The winner's per-slot quality outcome. Empty when the variant does not promote.</param>
/// <param name="AbsorbedMoney">Money the winner took from the loser.</param>
/// <param name="AbsorbedSupplyTons">Supplies the winner actually gained, after whichever cap applied.</param>
/// <param name="WinnerUnityDelta">The winner's nation's unity change, after the cap.</param>
/// <param name="LoserUnityDelta">The loser's nation's unity change. Negative.</param>
/// <param name="WinnerShipsLost">Ships the naval winner lost. Zero for the other two variants.</param>
/// <param name="WinnerConditionLost">Condition points the naval winner lost. Zero for the other two variants.</param>
/// <param name="WinnerUnitsLost">
/// Whole unit slots the naval winner's carried army lost to the <c>d &gt; 70</c> branch of
/// <c>FUN_0044B4F8</c>. Zero for the other two variants.
/// </param>
/// <param name="PeaceTreatyFired">
/// Whether this battle published <see cref="PeaceTreatyTriggered"/>. Named differently from the event so
/// that reading <c>result.PeaceTreatyFired</c> can never be confused with the event type itself.
/// </param>
/// <param name="Scatter">Where the survivor went, or <see langword="null"/> when nothing scattered.</param>
public sealed record BattleResult(
    BattleKind Kind,
    string AttackerId,
    string DefenderId,
    string AttackerNationId,
    string DefenderNationId,
    int AttackerPower,
    int DefenderPower,
    BattleSide Winner,
    DefeatOutcome? AppliedDefeatOutcome,
    LoserFate LoserFate,
    int WinnerCasualties,
    int LoserCasualties,
    ValueList<UnitCasualty> UnitCasualties,
    ValueList<UnitPromotion> Promotions,
    int AbsorbedMoney,
    int AbsorbedSupplyTons,
    int WinnerUnityDelta,
    int LoserUnityDelta,
    int WinnerShipsLost,
    int WinnerConditionLost,
    int WinnerUnitsLost,
    bool PeaceTreatyFired,
    ScatterOutcome? Scatter)
{
    /// <summary>Whether the attacking side won.</summary>
    public bool AttackerWon => Winner == BattleSide.Attacker;

    /// <summary>The winning army's, fleet's or city's id.</summary>
    public string WinnerId => AttackerWon ? AttackerId : DefenderId;

    /// <summary>The losing army's, fleet's or city's id.</summary>
    public string LoserId => AttackerWon ? DefenderId : AttackerId;

    /// <summary>The winning nation's id.</summary>
    public string WinnerNationId => AttackerWon ? AttackerNationId : DefenderNationId;

    /// <summary>The losing nation's id.</summary>
    public string LoserNationId => AttackerWon ? DefenderNationId : AttackerNationId;

    /// <summary>The winning side's strength.</summary>
    public int WinnerPower => AttackerWon ? AttackerPower : DefenderPower;

    /// <summary>The losing side's strength.</summary>
    public int LoserPower => AttackerWon ? DefenderPower : AttackerPower;
}

/// <summary>
/// One resolved battle: the state it produced, and what happened. Returned rather than applied so the
/// resolver stays a pure function of (state, ruleset, world, rng) — the same shape every command
/// handler in the engine already has.
/// </summary>
/// <param name="State">The state after the battle.</param>
/// <param name="Result">What happened.</param>
public sealed record BattleResolution(GameState State, BattleResult Result);
