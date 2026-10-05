using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>
/// One provider row as the Supply army dialog shows it — the entity's own id and the caption the
/// original's window prints ("Supplies at Antium: 330"), its stock, and whether a purchase there is free.
/// </summary>
/// <param name="Kind">City or fleet, so the dialog knows which <c>buy</c> form to compose.</param>
/// <param name="Id">The city or fleet id.</param>
/// <param name="Label">The list row's caption, with the stock.</param>
/// <param name="StockTons">The provider's supply stock, read from the live state.</param>
/// <param name="IsFree">True for an own city or own fleet; false for a foreign city at peace.</param>
public sealed record SupplyProviderChoice(
    SupplyProviderKind Kind,
    string Id,
    string Label,
    int StockTons,
    bool IsFree);

/// <summary>One row of the money panel's <c>via</c> picker: the treasury, or one own fleet within one tile.</summary>
/// <param name="Label">What the picker shows.</param>
/// <param name="FleetId">The fleet id, or <see langword="null"/> for the national treasury.</param>
public sealed record MoneyViaChoice(string Label, string? FleetId);

/// <summary>
/// The Army menu's <strong>Supply army</strong> dialog, Godot-free — its provider list, the free and paid
/// panels' limits and step clamps, and the command lines it composes. The Godot control
/// (<see cref="SupplyDialog"/>) owns widgets and delegates to this type; the test project compiles this
/// file directly, so it must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The player's decision of 2026-10-04</strong> put Supply army on the v0.4 line as its own task
/// [confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.6, Army → Supply army]. It
/// composes the two existing verbs and invents no rule: <c>buy</c> (<see cref="BuySupplyCommand"/>) for
/// the tons, <c>transfer-money</c> (<see cref="TransferMoneyCommand"/>) for the money panel. The free and
/// paid clamps are <see cref="SupplyPurchase"/>'s own, read here only to show a figure and to bound what
/// a press can stage.
/// </para>
/// <para>
/// <strong>Free provider: each press applies at once.</strong> An own city or own fleet composes one
/// <c>buy</c> of the step and the engine clamps it — including a giveback when the army is past its room
/// [confirmed: code, <c>TAFSupply_ChangeSupply</c>]. <strong>Paid provider: a press stages an amount</strong>
/// [confirmed: code, <c>TAFSupply_ChangeBuyAmount</c>], floored at 0 and clamped to the stock, the room
/// (<c>troops / armySupplyTonsPerTroops + supplyDialogArmyCapacityBonus − supply</c>, never floored at 0)
/// and, under per-unit purses, <c>money × supplyTonsPerTalent</c>; under <c>improved</c> the treasury pays
/// and there is no money clamp. The cost shown is <c>staged / supplyTonsPerTalent</c>. Transfer composes
/// the one <c>buy</c>; Close drops whatever is staged.
/// </para>
/// <para>
/// <strong>The steps are 10 and 100</strong> [confirmed: code, <c>TAFSupply_ChangeSupply</c> /
/// <c>TAFSupply_ChangeBuyAmount</c>]. No ruleset key holds them and this task may not change the ruleset,
/// so they live here as two named constants rather than as scattered literals; every actual <em>limit</em>
/// is read from the ruleset.
/// </para>
/// </remarks>
public sealed class SupplyDialogModel
{
    /// <summary>The 10s arrow's step, in tons [confirmed: code].</summary>
    public const int SupplyStepTons = 10;

    /// <summary>The 100s arrow's step, in tons [confirmed: code].</summary>
    public const int SupplyLargeStepTons = 100;

    private readonly Ruleset _ruleset;
    private readonly string _armyId;
    private readonly List<SupplyProviderChoice> _providers = new();
    private readonly List<MoneyViaChoice> _viaChoices = new();
    private int _selectedProviderIndex;
    private int _stagedTons;

    private SupplyDialogModel(GameState state, ArmyState army, Ruleset ruleset)
    {
        _ruleset = ruleset;
        _armyId = army.Id;
        Refresh(state, army);
    }

    /// <summary>Builds the dialog's model for <paramref name="army"/> on the live <paramref name="state"/>.</summary>
    public static SupplyDialogModel ForArmy(GameState state, ArmyState army, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new SupplyDialogModel(state, army, ruleset);
    }

    /// <summary>The buying army's id.</summary>
    public string ArmyId => _armyId;

    /// <summary>The provider rows, cities first, in the state's own order.</summary>
    public IReadOnlyList<SupplyProviderChoice> Providers => _providers;

    /// <summary>The <c>via</c> picker's rows: Treasury first, then each own fleet within one tile.</summary>
    public IReadOnlyList<MoneyViaChoice> MoneyViaChoices => _viaChoices;

    /// <summary>The selected provider's index; the first is selected on opening.</summary>
    public int SelectedProviderIndex => _selectedProviderIndex;

    /// <summary>The selected provider, or <see langword="null"/> when there is none.</summary>
    public SupplyProviderChoice? SelectedProvider =>
        _providers.Count == 0 ? null : _providers[_selectedProviderIndex];

    /// <summary>The army's own supply stock, tons.</summary>
    public int ArmySupplyTons { get; private set; }

    /// <summary>The army's room, <c>capacity − supply</c>, never floored at 0 [confirmed: code].</summary>
    public int ArmyRoomTons { get; private set; }

    /// <summary>The army's own purse.</summary>
    public int ArmyMoney { get; private set; }

    /// <summary>The army's nation's treasury.</summary>
    public int NationalTreasury { get; private set; }

    /// <summary>How many tons the paid panel has staged — 0 on the free path.</summary>
    public int StagedTons => _stagedTons;

    /// <summary>The cost of <see cref="StagedTons"/>, <c>staged / supplyTonsPerTalent</c> [confirmed: code].</summary>
    public int StagedCostTalents => _stagedTons / _ruleset.Economy.SupplyTonsPerTalent;

    /// <summary>Selects a provider row; out-of-range indices are ignored.</summary>
    public void SelectProvider(int index)
    {
        if (index < 0 || index >= _providers.Count)
        {
            return;
        }

        _selectedProviderIndex = index;
        _stagedTons = 0;
    }

    /// <summary>Selects the <c>via</c> row by the fleet's id, or the treasury when <paramref name="fleetId"/> is null.</summary>
    public void SelectVia(string? fleetId)
    {
        if (_viaChoices.Any(choice => string.Equals(choice.FleetId, fleetId, StringComparison.Ordinal)))
        {
            SelectedViaFleetId = fleetId;
        }
    }

    /// <summary>The selected <c>via</c> fleet, or <see langword="null"/> for the treasury.</summary>
    public string? SelectedViaFleetId { get; private set; }

    /// <summary>
    /// One press of a supply arrow. A free provider composes the <c>buy</c> line the caller submits at
    /// once; a paid provider stages the step and returns <see langword="null"/> (nothing is submitted until
    /// Transfer).
    /// </summary>
    /// <returns>The command line to submit, or <see langword="null"/> when the press only staged.</returns>
    public string? PressSupply(int stepTons)
    {
        if (SelectedProvider is not { } provider || stepTons == 0)
        {
            return null;
        }

        if (!provider.IsFree)
        {
            AdjustStaged(stepTons);
            return null;
        }

        // The free path only adds: the engine clamps the request and gives supply back when the army is
        // already past its room, which is the original's own behaviour.
        return stepTons > 0 ? BuyLine(provider, stepTons) : null;
    }

    /// <summary>
    /// Moves the paid panel's staged amount by <paramref name="deltaTons"/>, floored at 0 and clamped to
    /// the provider's stock, the army's room and (per-unit purses only) the army's affordable tons.
    /// </summary>
    public void AdjustStaged(int deltaTons)
    {
        if (SelectedProvider is not { IsFree: false } provider)
        {
            _stagedTons = 0;
            return;
        }

        var upper = Math.Min(provider.StockTons, ArmyRoomTons);
        if (_ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
        {
            upper = Math.Min(upper, ArmyMoney * _ruleset.Economy.SupplyTonsPerTalent);
        }

        // The room may be negative (an army past its capacity): floored at 0, so nothing can be staged.
        upper = Math.Max(0, upper);

        // Clamp against the upper bound, then the 0 floor, so a negative room never inverts the bounds.
        _stagedTons = Math.Clamp(_stagedTons + deltaTons, 0, upper);
    }

    /// <summary>Transfer: one <c>buy</c> of the staged amount, or nothing when none is staged.</summary>
    public string? TransferStaged() =>
        SelectedProvider is { IsFree: false } provider && _stagedTons > 0
            ? BuyLine(provider, _stagedTons)
            : null;

    /// <summary>
    /// The dialog's Close: drops any staged amount and composes nothing [task Done-when 4; the task's
    /// design call 5]. The screen closes the overlay itself.
    /// </summary>
    public string? Close()
    {
        _stagedTons = 0;
        return null;
    }

    /// <summary>
    /// One press of a money arrow — one <c>transfer-money</c> at once, the signed amount from the step.
    /// </summary>
    /// <param name="signedTalents">Positive moves talents into the army's purse; negative moves them out.</param>
    /// <param name="viaFleetId">The fleet standing in for the treasury, or <see langword="null"/> for the treasury.</param>
    public string? PressMoney(int signedTalents, string? viaFleetId)
    {
        if (signedTalents == 0)
        {
            return null;
        }

        var via = string.IsNullOrEmpty(viaFleetId) ? string.Empty : $" via {viaFleetId}";
        return $"transfer-money {_armyId} {signedTalents.ToString(System.Globalization.CultureInfo.InvariantCulture)}{via}";
    }

    /// <summary>
    /// Re-reads the figures from the state after a command was submitted: the provider list (its stocks
    /// move), the army's supply and money, the treasury, and the cleared staged amount.
    /// </summary>
    public void Refresh(GameState state, ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);

        var providers = SupplyProviders.ForArmy(state, army.Id, _ruleset);
        _providers.Clear();
        foreach (var provider in providers)
        {
            var (label, stock) = provider.Kind == SupplyProviderKind.City
                ? LabelForCity(state, provider.Id)
                : LabelForFleet(state, provider.Id);
            _providers.Add(new SupplyProviderChoice(provider.Kind, provider.Id, label, stock, provider.IsFree));
        }

        if (_selectedProviderIndex >= _providers.Count)
        {
            _selectedProviderIndex = 0;
        }

        ArmySupplyTons = army.SupplyTons;
        ArmyMoney = army.Money;
        ArmyRoomTons = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, _ruleset) - army.SupplyTons;
        NationalTreasury = state.NationById(army.Nation)?.Treasury ?? 0;

        _viaChoices.Clear();
        _viaChoices.Add(new MoneyViaChoice("Treasury", null));
        foreach (var fleet in state.Fleets)
        {
            if (!string.Equals(fleet.Nation, army.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = LandingTile.ChebyshevDistance(
                new GridPoint(army.X, army.Y), new GridPoint(fleet.X, fleet.Y));
            if (distance > _ruleset.Economy.CommandAdjacencyRadiusTiles)
            {
                continue;
            }

            _viaChoices.Add(new MoneyViaChoice(fleet.Id, fleet.Id));
        }

        _stagedTons = 0;
    }

    private string BuyLine(SupplyProviderChoice provider, int tons)
    {
        var providerToken = provider.Kind == SupplyProviderKind.Fleet ? $"fleet {provider.Id}" : provider.Id;
        return $"buy {_armyId} {providerToken} {tons}";
    }

    private static (string Label, int Stock) LabelForCity(GameState state, string cityId)
    {
        var city = state.CityById(cityId);
        return city is null
            ? ($"Supplies at {cityId}: 0", 0)
            : ($"Supplies at {city.Name}: {city.SupplyTons}", city.SupplyTons);
    }

    private static (string Label, int Stock) LabelForFleet(GameState state, string fleetId)
    {
        var fleet = state.FleetById(fleetId);
        return fleet is null
            ? ($"Supplies at {fleetId}: 0", 0)
            : ($"Supplies at {fleetId}: {fleet.SupplyTons}", fleet.SupplyTons);
    }
}
