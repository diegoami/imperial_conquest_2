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
/// Godot-free (no <c>using Godot</c> anywhere in this file) so
/// <c>tests/IC2.Engine.Tests/Ui/Screens/BattleResultViewModelTests.cs</c> can exercise it directly, the
/// same seam T24 established for <c>RulesetPresets.cs</c>/<c>NewGameSelection.cs</c> — see this task's own
/// PR body and <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>'s new <c>Compile Include</c> line.
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
}
