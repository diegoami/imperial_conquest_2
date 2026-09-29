using IC2.Engine.Battle;
using IC2.Engine.Model;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/tasks/T25.md</c>: "the battle-result screen presents the instant resolver's contents ... not
/// the original tactical dialog's per-type attrition table" and "Build this screen so a future optional
/// alternate battle presentation ... could later be swapped in without changing what T16 emits." This is
/// that seam — a plain, Godot-free copy of every field <see cref="BattleResult"/> carries (so
/// <see cref="BattleResultScreen"/> never reads a <see cref="BattleResult"/> directly), built by
/// <see cref="FromResult"/> alone. A future alternate presentation would read this same view model, or add
/// its own alongside it; neither needs <see cref="BattleResult"/>, <see cref="InstantBattleResolver"/> or
/// <c>T16</c> to change.
/// </summary>
/// <remarks>
/// <para>
/// Godot-free (no <c>using Godot</c> anywhere in this file) so
/// <c>tests/IC2.Engine.Tests/Ui/Screens/BattleResultViewModelTests.cs</c> can exercise it directly, the
/// same seam T24 established for <c>RulesetPresets.cs</c>/<c>NewGameSelection.cs</c> — see this task's own
/// PR body and <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>'s new <c>Compile Include</c> line.
/// </para>
/// <para>
/// <strong>bug #499: the screen's own wording lives here too.</strong> The text methods below are the
/// exact strings <see cref="BattleResultScreen"/> renders for both sides' casualties, a siege's
/// before/after city values, and a failed siege's shortfall — so the Godot-free test can assert the
/// same strings the screen shows, without a Godot run and without re-deriving a format the screen is
/// free to ignore. They add no field and change no number.
/// </para>
/// </remarks>
public sealed record BattleResultViewModel(
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
    IReadOnlyList<UnitCasualty> UnitCasualties,
    IReadOnlyList<UnitPromotion> Promotions,
    int AbsorbedMoney,
    int AbsorbedSupplyTons,
    int WinnerUnityDelta,
    int LoserUnityDelta,
    int WinnerShipsLost,
    int WinnerConditionLost,
    int WinnerUnitsLost,
    bool PeaceTreatyFired,
    bool PeaceTreatyOffered,
    ScatterOutcome? Scatter,
    int? CityLoyaltyBefore,
    int? CityLoyaltyAfter,
    int? CityFortificationPercentBefore,
    int? CityFortificationPercentAfter,
    int? CityPopulationThousandsBefore,
    int? CityPopulationThousandsAfter,
    bool AttackerWon,
    string WinnerId,
    string LoserId,
    string WinnerNationId,
    string LoserNationId,
    int WinnerPower,
    int LoserPower)
{
    /// <summary>
    /// Copies every field <paramref name="result"/> exposes (own and computed alike) — the one place this
    /// mapping happens, so <c>BattleResultViewModelTests</c>'s own reflection check
    /// (<c>ViewModel_exposes_every_public_property_BattleResult_has</c>) has one call site to point at
    /// when a future <see cref="BattleResult"/> field needs a matching line added here.
    /// </summary>
    public static BattleResultViewModel FromResult(BattleResult result) => new(
        result.Kind,
        result.AttackerId,
        result.DefenderId,
        result.AttackerNationId,
        result.DefenderNationId,
        result.AttackerPower,
        result.DefenderPower,
        result.Winner,
        result.AppliedDefeatOutcome,
        result.LoserFate,
        result.WinnerCasualties,
        result.LoserCasualties,
        result.UnitCasualties,
        result.Promotions,
        result.AbsorbedMoney,
        result.AbsorbedSupplyTons,
        result.WinnerUnityDelta,
        result.LoserUnityDelta,
        result.WinnerShipsLost,
        result.WinnerConditionLost,
        result.WinnerUnitsLost,
        result.PeaceTreatyFired,
        result.PeaceTreatyOffered,
        result.Scatter,
        result.CityLoyaltyBefore,
        result.CityLoyaltyAfter,
        result.CityFortificationPercentBefore,
        result.CityFortificationPercentAfter,
        result.CityPopulationThousandsBefore,
        result.CityPopulationThousandsAfter,
        result.AttackerWon,
        result.WinnerId,
        result.LoserId,
        result.WinnerNationId,
        result.LoserNationId,
        result.WinnerPower,
        result.LoserPower);

    /// <summary>
    /// The winning side's casualty line — the screen's existing wording, kept as the first half of the
    /// pair so the losing side's figure can finally sit beside it (bug #499).
    /// </summary>
    public string WinnerCasualtiesText(string winnerName) =>
        $"{winnerName}'s casualties: {WinnerCasualties}";

    /// <summary>
    /// The losing side's casualty line (bug #499). For a failed siege the loser is the <em>besieging
    /// army</em>, so this is that army's own attrition, which <see cref="BattleResult.LoserCasualties"/>
    /// holds — 1,872 troops in the reproduced Felsina siege — and which the unchanged screen never
    /// showed: "Gaul's casualties: 0" was the whole casualty report. For a field battle it is the
    /// loser's own losses, and for a naval battle its hulls, exactly as <see cref="BattleResult"/>
    /// documents — so a naval battle says "ships lost", because <see cref="WinnerCasualties"/> beside it
    /// counts the winning fleet's carried army's <em>troops</em> and the bare "casualties" read as the
    /// same unit (bug #499 review N2).
    /// </summary>
    public string LoserCasualtiesText(string loserName) =>
        Kind == BattleKind.Naval
            ? $"{loserName}'s ships lost: {LoserCasualties}"
            : $"{loserName}'s casualties: {LoserCasualties}";

    /// <summary>
    /// The besieged city's before/after loyalty, fortification and population (bug #499), or
    /// <see langword="null"/> when this was not a siege — the six <c>City*</c> fields are set together
    /// or not at all. The player never saw this before: every attempt erodes the city, and on the
    /// reproduced Felsina siege that is loyalty 79 -> 76, fortification 68% -> 65%, population
    /// 26k -> 25k.
    /// </summary>
    public string? SiegeCityText(string cityName) =>
        Kind != BattleKind.Siege || CityLoyaltyBefore is null
            ? null
            : $"{cityName}: loyalty {CityLoyaltyBefore} -> {CityLoyaltyAfter}, "
              + $"fortification {CityFortificationPercentBefore}% -> {CityFortificationPercentAfter}%, "
              + $"population {CityPopulationThousandsBefore}k -> {CityPopulationThousandsAfter}k";

    /// <summary>
    /// How far short a failed siege fell (bug #499), or <see langword="null"/> for anything else. The
    /// confirmed rule is "the attacker wins only if <c>atk &gt; def</c>, ties to the defender"
    /// (<c>docs/game-design.md</c> §Combat), so the shortfall is <c>DefenderPower - AttackerPower</c> —
    /// never negative. An exact tie says the tie goes to the defender rather than "fell short by 0"
    /// (review N1), and a besieger that out-powered the city but was emptied by its own losses
    /// (<see cref="EmptiedBesiegerText"/>) gets its own line instead of a negative shortfall (review B1).
    /// </summary>
    public string? FailedSiegeShortfallText() =>
        Kind == BattleKind.Siege && !AttackerWon && AttackerPower <= DefenderPower
            ? AttackerPower == DefenderPower
                ? $"The attack fell short: a tie goes to the defender "
                  + $"({AttackerPower} against {DefenderPower})."
                : $"The attack fell short by {DefenderPower - AttackerPower}: "
                  + $"{AttackerPower} against {DefenderPower}."
            : null;

    /// <summary>
    /// The distinct line for the T63 N7 state (bug #499 review B1): a siege the attacker lost even though
    /// its <see cref="AttackerPower"/> beat the <see cref="DefenderPower"/>, because this same attempt's
    /// own attrition emptied the besieging army — <c>InstantBattleResolver.ResolveSiege</c> then reports
    /// the defender as the winner rather than an unresolved capture
    /// (<c>SiegeAttritionTests.EmptiedBesieger_ReportsTheDefenderAsWinner_NotACaptureTheCityDidNotYield</c>).
    /// There is no shortfall to state here, so <see cref="FailedSiegeShortfallText"/> is
    /// <see langword="null"/> and this is <see langword="null"/> for every other battle.
    /// </summary>
    public string? EmptiedBesiegerText() =>
        Kind == BattleKind.Siege && !AttackerWon && AttackerPower > DefenderPower
            ? $"Strong enough ({AttackerPower} against {DefenderPower}), but the besieging army was "
              + "wiped out by its own losses, so the city held."
            : null;
}
