using System.Reflection;
using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T25.md</c> Done-when: "a test asserts the battle-result view model exposes every field
/// of BattleResult (so a later resolver change cannot silently drop one), including the loser's-fate field
/// under both a destroyed and a scattered fixture."
/// </summary>
public sealed class BattleResultViewModelTests
{
    /// <summary>
    /// The reflection sweep itself: every public property <see cref="BattleResult"/> exposes (its own
    /// positional fields and its computed conveniences alike — <c>WinnerId</c>, <c>WinnerPower</c>, ...)
    /// must have a same-named public property on <see cref="BattleResultViewModel"/>. This is what makes
    /// "cannot silently drop one" true: adding a field to <see cref="BattleResult"/> without adding the
    /// matching line to <see cref="BattleResultViewModel.FromResult"/> fails this test the moment the new
    /// property's name is missing here, before anyone has to remember to update this file by hand.
    /// </summary>
    [Fact]
    public void ViewModel_exposes_every_public_property_BattleResult_has()
    {
        var resultProperties = typeof(BattleResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var viewModelProperties = typeof(BattleResultViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = resultProperties.Except(viewModelProperties).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            $"BattleResultViewModel is missing: {string.Join(", ", missing)}. "
            + "Add the field to BattleResultViewModel's own record and to FromResult's mapping.");
    }

    [Fact]
    public void FromResult_carries_every_field_through_for_a_destroyed_loser()
    {
        var result = MakeResult(LoserFate.Destroyed, scatter: null);

        var viewModel = BattleResultViewModel.FromResult(result);

        Assert.Equal(LoserFate.Destroyed, viewModel.LoserFate);
        Assert.Null(viewModel.Scatter);
        AssertSharedFieldsMatch(result, viewModel);
    }

    [Fact]
    public void FromResult_carries_every_field_through_for_a_scattered_loser()
    {
        var scatter = new ScatterOutcome(FromX: 3, FromY: 4, ToX: 5, ToY: 6, RequestedDistance: 2, ActualDistance: 2);
        var result = MakeResult(LoserFate.Scattered, scatter);

        var viewModel = BattleResultViewModel.FromResult(result);

        Assert.Equal(LoserFate.Scattered, viewModel.LoserFate);
        Assert.Equal(scatter, viewModel.Scatter);
        AssertSharedFieldsMatch(result, viewModel);
    }

    private static void AssertSharedFieldsMatch(BattleResult result, BattleResultViewModel viewModel)
    {
        Assert.Equal(result.Kind, viewModel.Kind);
        Assert.Equal(result.AttackerId, viewModel.AttackerId);
        Assert.Equal(result.DefenderId, viewModel.DefenderId);
        Assert.Equal(result.AttackerNationId, viewModel.AttackerNationId);
        Assert.Equal(result.DefenderNationId, viewModel.DefenderNationId);
        Assert.Equal(result.AttackerPower, viewModel.AttackerPower);
        Assert.Equal(result.DefenderPower, viewModel.DefenderPower);
        Assert.Equal(result.Winner, viewModel.Winner);
        Assert.Equal(result.WinnerNationId, viewModel.WinnerNationId);
        Assert.Equal(result.LoserNationId, viewModel.LoserNationId);
        Assert.Equal(result.WinnerCasualties, viewModel.WinnerCasualties);
        Assert.Equal(result.LoserCasualties, viewModel.LoserCasualties);
        Assert.Equal(result.Promotions.Count, viewModel.Promotions.Count);
        Assert.Equal(result.AbsorbedMoney, viewModel.AbsorbedMoney);
        Assert.Equal(result.AbsorbedSupplyTons, viewModel.AbsorbedSupplyTons);
        Assert.Equal(result.WinnerUnityDelta, viewModel.WinnerUnityDelta);
        Assert.Equal(result.LoserUnityDelta, viewModel.LoserUnityDelta);
        Assert.Equal(result.PeaceTreatyFired, viewModel.PeaceTreatyFired);
        Assert.Equal(result.PeaceTreatyOffered, viewModel.PeaceTreatyOffered);
    }

    private static BattleResult MakeResult(LoserFate fate, ScatterOutcome? scatter) => new(
        Kind: BattleKind.Field,
        AttackerId: "army-rome-1",
        DefenderId: "army-gaul-1",
        AttackerNationId: "rome",
        DefenderNationId: "gaul",
        AttackerPower: 120,
        DefenderPower: 80,
        Winner: BattleSide.Attacker,
        AppliedDefeatOutcome: fate == LoserFate.Destroyed ? DefeatOutcome.Destroyed : DefeatOutcome.Scatter,
        LoserFate: fate,
        WinnerCasualties: 12,
        LoserCasualties: 80,
        UnitCasualties: ValueList<UnitCasualty>.Empty,
        Promotions: ValueList<UnitPromotion>.Empty,
        AbsorbedMoney: 250,
        AbsorbedSupplyTons: 10,
        WinnerUnityDelta: 5,
        LoserUnityDelta: -10,
        WinnerShipsLost: 0,
        WinnerConditionLost: 0,
        WinnerUnitsLost: 0,
        PeaceTreatyFired: false,
        PeaceTreatyOffered: false,
        Scatter: scatter);
}
