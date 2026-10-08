using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>
/// The Unit map's <strong>Fleet</strong> and <strong>City</strong> dialogs, Godot-free — each dialog's
/// limits and the command lines it composes. The Godot controls (<see cref="RepairDialog"/>,
/// <see cref="FleetTransferDialog"/>, <see cref="SplitFleetDialog"/>, <see cref="FortifyDialog"/>) own
/// only widgets and submit what these types compose; the test project compiles this file directly, so it
/// must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Task T112</strong> (<c>docs/tasks/T112.md</c>): the Unit map's Fleet and City orders, and the
/// selected unit's command strip. Every <em>limit</em> is read from the ruleset or the engine — the
/// 20-ship split floor (<see cref="NavalRules.SplitMinShips"/>), the 100-ship join cap
/// (<see cref="NavalRules.JoinMaxShips"/>), the repair cost divisor
/// (<see cref="NavalRules.RepairCostDivisor"/>), the maximum condition
/// (<see cref="NavalRules.MaxConditionPercent"/>) and the fortify order's
/// <see cref="CityOrderRule.CostPerPointPerPopulationThousand"/> and
/// <see cref="CityOrderRule.MaxPercent"/> — never a C# literal. Only the confirmed stepper steps are
/// named constants.
/// </para>
/// <para>
/// <strong>The refusals</strong> are the original's own lines where a report records them
/// <c>[Wine candidate: 2026-10-02-fleet-orders-live.md]</c>; the no-partner line is the clone's own
/// <c>[designed]</c> wording, the fleet twin of <see cref="ArmyDialogModels.NoPartnerMessage"/>.
/// </para>
/// </remarks>
public static class FleetCityDialogModels
{
    /// <summary>
    /// The message Join fleets and Transfer ships show when the game picks no partner — the task entry's
    /// own <c>[designed]</c> wording, the fleet twin of
    /// <see cref="ArmyDialogModels.NoPartnerMessage"/>.
    /// </summary>
    public const string NoPartnerMessage = "There is no fleet next to this one.";

    /// <summary>
    /// The original's repair refusal next to a city that is not the fleet's own
    /// <c>[Wine candidate: 2026-10-02-fleet-orders-live.md]</c>.
    /// </summary>
    public const string RepairNotAtCityMessage = "The fleet can only be repaired at one of your cities.";

    /// <summary>
    /// The original's repair refusal while carrying an army
    /// <c>[derived: 2026-10-05-refusal-texts-and-conditions.md row R14]</c>.
    /// </summary>
    public const string RepairCarryingArmyMessage = "A fleet cannot be repaired while it is carrying an army.";

    /// <summary>
    /// The original's scuttle refusal away from the fleet's own city
    /// <c>[Wine candidate: 2026-10-02-fleet-orders-live.md]</c>.
    /// </summary>
    public const string ScuttleNotNearCityMessage = "To scuttle a fleet it must be near one of your cities.";

    /// <summary>
    /// The original's scuttle refusal while carrying an army
    /// <c>[derived: 2026-10-05-refusal-texts-and-conditions.md row R21]</c>.
    /// </summary>
    public const string ScuttleCarryingArmyMessage = "A fleet cannot be scuttled while it is carrying an army.";

    /// <summary>
    /// The scuttle confirmation's question — the original's own text, with the space before the question
    /// mark the original uses <c>[Wine candidate: 2026-10-02-fleet-orders-live.md]</c>.
    /// </summary>
    public const string ScuttlePromptText = "Are you sure you want to scuttle this fleet ?";

    /// <summary>The split-fleet stepper's ship step <c>[Wine candidate: 2026-10-02-fleet-orders-live.md]</c>.</summary>
    public const int ShipStep = 1;

    /// <summary>The split-fleet stepper's large ship step.</summary>
    public const int ShipLargeStep = 10;

    /// <summary>The supply stepper's step, tons <c>[Wine candidate: the same report]</c>.</summary>
    public const int SupplyStepTons = 10;

    /// <summary>The supply stepper's large step, tons.</summary>
    public const int SupplyLargeStepTons = 100;

    /// <summary>The money stepper's step, talents.</summary>
    public const int MoneyStepTalents = 10;

    /// <summary>The money stepper's large step, talents.</summary>
    public const int MoneyLargeStepTalents = 100;

    /// <summary>The original's split refusal for a fleet below the minimum, its number from the ruleset
    /// <c>[derived: 2026-10-05-refusal-texts-and-conditions.md row R17]</c>.</summary>
    public static string SplitTooFewShipsMessage(Ruleset ruleset) =>
        $"You can not split a fleet containing less than {ruleset.Naval.SplitMinShips} ships.";

    /// <summary>The original's split refusal while carrying an army
    /// <c>[derived: 2026-10-05-refusal-texts-and-conditions.md row R18]</c>.</summary>
    public const string SplitCarryingArmyMessage = "You can not split a fleet carrying an army.";

    /// <summary>Join fleets' and Transfer ships' refusal above the cap — the engine's own line, its
    /// number from the ruleset.</summary>
    public static string CombinedShipsTooLargeMessage(Ruleset ruleset) =>
        $"There are more than {ruleset.Naval.JoinMaxShips} ships in these fleets combined.";

    /// <summary>Scuttle fleet composes <c>scuttle-fleet</c>.</summary>
    public static string ScuttleFleetLine(string fleetId) => $"scuttle-fleet {fleetId}";
}

/// <summary>
/// Join fleets' Godot-free rule — no dialog, the selected fleet is the survivor and the game's own
/// adjacent partner is absorbed. The combined ship count may not exceed
/// <see cref="NavalRules.JoinMaxShips"/> (100).
/// </summary>
public sealed class FleetJoinModel
{
    private readonly FleetState _selected;
    private readonly FleetState? _partner;
    private readonly Ruleset _ruleset;

    private FleetJoinModel(FleetState selected, FleetState? partner, Ruleset ruleset)
    {
        _selected = selected;
        _partner = partner;
        _ruleset = ruleset;
    }

    /// <summary>Builds the model for the selected fleet and the game's own partner (or none).</summary>
    public static FleetJoinModel ForFleet(FleetState selected, FleetState? partner, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new FleetJoinModel(selected, partner, ruleset);
    }

    /// <summary>Whether the two fleets may join: a partner exists and the combined ships are within the cap.</summary>
    public bool CanJoin => _partner is not null && _selected.Ships + _partner.Ships <= _ruleset.Naval.JoinMaxShips;

    /// <summary>The refusal to show when <see cref="CanJoin"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        _partner is null
            ? FleetCityDialogModels.NoPartnerMessage
            : CanJoin
                ? null
                : FleetCityDialogModels.CombinedShipsTooLargeMessage(_ruleset);

    /// <summary><c>join-fleets &lt;selected&gt; &lt;partner&gt;</c>, or <see langword="null"/> when refused.</summary>
    public string? ComposeOk() => CanJoin ? $"join-fleets {_selected.Id} {_partner!.Id}" : null;
}

/// <summary>
/// The Fleet menu's <strong>Repair fleet</strong> dialog, Godot-free — the points that may be ordered,
/// the cost <c>ships × points / 5</c> and the command line it composes.
/// </summary>
/// <remarks>
/// Repair is offered only at one of the fleet's own nation's cities and never while the fleet carries an
/// army <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md; Wine candidate:
/// 2026-10-02-fleet-orders-live.md]</c>. The engine clamps an over-cap request to the fleet's remaining
/// room, so the model clamps too and never rejects an over-cap press.
/// </remarks>
public sealed class RepairFleetModel
{
    private readonly FleetState _fleet;
    private readonly Ruleset _ruleset;

    private RepairFleetModel(FleetState fleet, Ruleset ruleset, bool atOwnCity)
    {
        _fleet = fleet;
        _ruleset = ruleset;
        IsAtOwnCity = atOwnCity;
    }

    /// <summary>Builds the model for <paramref name="fleet"/> on the live <paramref name="state"/>.</summary>
    public static RepairFleetModel ForFleet(GameState state, FleetState fleet, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(ruleset);

        var radius = ruleset.Economy.CommandAdjacencyRadiusTiles;
        var atOwnCity = false;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            if (LandingTile.ChebyshevDistance(
                    new GridPoint(city.X, city.Y), new GridPoint(fleet.X, fleet.Y)) <= radius)
            {
                atOwnCity = true;
                break;
            }
        }

        return new RepairFleetModel(fleet, ruleset, atOwnCity);
    }

    /// <summary>The fleet being repaired.</summary>
    public string FleetId => _fleet.Id;

    /// <summary>The fleet's current condition.</summary>
    public int ConditionPercent => _fleet.ConditionPercent;

    /// <summary>The ruleset's maximum condition.</summary>
    public int MaxConditionPercent => _ruleset.Naval.MaxConditionPercent;

    /// <summary>The most points the fleet can still take, <c>max − current</c>, floored at 0.</summary>
    public int MaxPoints => Math.Max(0, MaxConditionPercent - _fleet.ConditionPercent);

    /// <summary>Whether the fleet stands at one of its own nation's cities.</summary>
    public bool IsAtOwnCity { get; }

    /// <summary>Whether the fleet carries an army.</summary>
    public bool IsCarryingArmy => _fleet.IsCarryingArmy;

    /// <summary>Whether the repair may be offered at all.</summary>
    public bool CanRepair => IsAtOwnCity && !IsCarryingArmy;

    /// <summary>The refusal to show when <see cref="CanRepair"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        IsAtOwnCity
            ? IsCarryingArmy
                ? FleetCityDialogModels.RepairCarryingArmyMessage
                : null
            : FleetCityDialogModels.RepairNotAtCityMessage;

    /// <summary>The condition <paramref name="points"/> would leave, clamped to the room.</summary>
    public int NewConditionFor(int points) => ConditionPercent + Math.Clamp(points, 0, MaxPoints);

    /// <summary>The cost of <paramref name="points"/>, <c>ships × points / divisor</c>, clamped to the room.</summary>
    public int CostFor(int points) =>
        _fleet.Ships * Math.Clamp(points, 0, MaxPoints) / _ruleset.Naval.RepairCostDivisor;

    /// <summary><c>OK</c>: one <c>repair-fleet</c> of the clamped points, or <see langword="null"/>.</summary>
    public string? ComposeOk(int points) =>
        CanRepair && points > 0
            ? $"repair-fleet {FleetId} {Math.Min(points, MaxPoints)}"
            : null;

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;
}

/// <summary>
/// The Fleet menu's <strong>Split fleet</strong> dialog, Godot-free — the ships, supply and money staged
/// toward the new fleet, and the one <c>split-fleet</c> the dialog composes
/// (<c>docs/tasks/T141.md</c>'s form).
/// </summary>
/// <remarks>
/// The split needs at least <see cref="NavalRules.SplitMinShips"/> (20) ships and no army aboard, and the
/// selected fleet keeps at least one ship (the engine's own <c>naval.invalid-ship-count</c>). The engine
/// places the new fleet one tile away on water and the dialog only shows that; it places nothing itself.
/// </remarks>
public sealed class SplitFleetModel
{
    private readonly FleetState _fleet;
    private readonly Ruleset _ruleset;

    private SplitFleetModel(FleetState fleet, Ruleset ruleset, string newFleetId)
    {
        _fleet = fleet;
        _ruleset = ruleset;
        NewFleetId = newFleetId;
    }

    /// <summary>Builds the model for <paramref name="fleet"/> and the new fleet's id.</summary>
    public static SplitFleetModel ForFleet(GameState state, FleetState fleet, Ruleset ruleset, string newFleetId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new SplitFleetModel(fleet, ruleset, newFleetId);
    }

    /// <summary>The fleet being split.</summary>
    public string FleetId => _fleet.Id;

    /// <summary>The new fleet's id, chosen by the screen.</summary>
    public string NewFleetId { get; }

    /// <summary>The split floor, from the ruleset's own <see cref="NavalRules.SplitMinShips"/>.</summary>
    public int MinShips => _ruleset.Naval.SplitMinShips;

    /// <summary>The fleet's live ship count.</summary>
    public int ShipCount => _fleet.Ships;

    /// <summary>Whether the split can be offered: enough ships and no army aboard.</summary>
    public bool CanSplit => _fleet.Ships >= MinShips && !_fleet.IsCarryingArmy;

    /// <summary>The refusal the dialog shows when <see cref="CanSplit"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        CanSplit
            ? null
            : _fleet.Ships < MinShips
                ? FleetCityDialogModels.SplitTooFewShipsMessage(_ruleset)
                : FleetCityDialogModels.SplitCarryingArmyMessage;

    /// <summary>The most ships that can move: the selected fleet keeps at least one.</summary>
    public int MaxShips => Math.Max(0, _fleet.Ships - 1);

    /// <summary>The most supply that can move: what the parent holds.</summary>
    public int MaxSupply => Math.Max(0, _fleet.SupplyTons);

    /// <summary>The most money that can move: what the parent holds.</summary>
    public int MaxMoney => Math.Max(0, _fleet.Money);

    /// <summary>The ships staged toward the new fleet.</summary>
    public int Ships { get; private set; }

    /// <summary>The supply staged toward the new fleet, in tons.</summary>
    public int Supply { get; private set; }

    /// <summary>The money staged toward the new fleet, in talents.</summary>
    public int Money { get; private set; }

    /// <summary>One press of a ship arrow, clamped to <c>[0, <see cref="MaxShips"/>]</c>.</summary>
    public void AdjustShips(int delta) => Ships = Math.Clamp(Ships + delta, 0, MaxShips);

    /// <summary>One press of a supply arrow, clamped to <c>[0, <see cref="MaxSupply"/>]</c>.</summary>
    public void AdjustSupply(int delta) => Supply = Math.Clamp(Supply + delta, 0, MaxSupply);

    /// <summary>One press of a money arrow, clamped to <c>[0, <see cref="MaxMoney"/>]</c>.</summary>
    public void AdjustMoney(int delta) => Money = Math.Clamp(Money + delta, 0, MaxMoney);

    /// <summary>
    /// <c>OK</c>: exactly one <c>split-fleet</c> with the staged ships, supply and money, or
    /// <see langword="null"/> when refused or no ship is staged.
    /// </summary>
    public string? ComposeOk()
    {
        if (!CanSplit || Ships < 1)
        {
            return null;
        }

        var line = $"split-fleet {FleetId} {NewFleetId} {Ships}";
        if (Supply > 0)
        {
            line += $" supply={Supply}";
        }

        if (Money > 0)
        {
            line += $" money={Money}";
        }

        return line;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;
}

/// <summary>
/// The Fleet menu's <strong>Transfer ships</strong> dialog, Godot-free — the ships, supply and money
/// staged either way between the selected fleet and the game's own adjacent partner, and the
/// <c>fleet-transfer</c> line(s) one <c>OK</c> composes.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>TFleetToFleet</c> is the naval twin of <c>TArmyToArmy</c>: the steppers move
/// ships, supply and money <em>either</em> direction inside one screen
/// <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md line 81]</c>. The clone's
/// <c>fleet-transfer</c> is one-directional, so a mixed staging composes one command per direction
/// (each direction's amount is what was staged that way), and a one-directional staging composes exactly
/// one command.
/// </para>
/// <para>
/// The partner is <see cref="IC2.Engine.Armies.AdjacentPartner.Fleet"/> — the game's pick, never the
/// player's. With no partner the model composes nothing and refuses with the clone's no-partner message;
/// with the two fleets' combined ships above <see cref="NavalRules.JoinMaxShips"/> it refuses with the
/// engine's own combined-ships line.
/// </para>
/// </remarks>
public sealed class FleetTransferModel
{
    private readonly FleetState _selected;
    private readonly FleetState? _partner;
    private readonly Ruleset _ruleset;
    private int _shipsNet;
    private int _supplyNet;
    private int _moneyNet;

    private FleetTransferModel(FleetState selected, FleetState? partner, Ruleset ruleset)
    {
        _selected = selected;
        _partner = partner;
        _ruleset = ruleset;
    }

    /// <summary>Builds the model for the selected fleet A and the game's own partner B (or none).</summary>
    public static FleetTransferModel ForFleets(FleetState selected, FleetState? partner, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new FleetTransferModel(selected, partner, ruleset);
    }

    /// <summary>A, the selected fleet — the first id.</summary>
    public string SelectedFleetId => _selected.Id;

    /// <summary>B, the game-picked partner, or <see langword="null"/>.</summary>
    public string? PartnerFleetId => _partner?.Id;

    /// <summary>Whether the game picked a partner at all.</summary>
    public bool HasPartner => _partner is not null;

    /// <summary>Whether the combined ship count is within <see cref="NavalRules.JoinMaxShips"/>.</summary>
    public bool CombinedShipsWithinCap =>
        _partner is null || _selected.Ships + _partner.Ships <= _ruleset.Naval.JoinMaxShips;

    /// <summary>The refusal to show when the transfer cannot be composed, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        _partner is null
            ? FleetCityDialogModels.NoPartnerMessage
            : CombinedShipsWithinCap
                ? null
                : FleetCityDialogModels.CombinedShipsTooLargeMessage(_ruleset);

    /// <summary>The staged ship net: positive is A → B, negative is B → A.</summary>
    public int ShipsNet => _shipsNet;

    /// <summary>The staged supply net: positive is A → B, negative is B → A.</summary>
    public int SupplyNet => _supplyNet;

    /// <summary>The staged money net: positive is A → B, negative is B → A.</summary>
    public int MoneyNet => _moneyNet;

    /// <summary>The most ships A can send B — leaving B within the join cap.</summary>
    public int MaxShipsToPartner =>
        _partner is null || !CombinedShipsWithinCap
            ? 0
            : Math.Max(0, Math.Min(_selected.Ships, _ruleset.Naval.JoinMaxShips - _partner.Ships));

    /// <summary>The most ships B can send A — leaving A within the join cap.</summary>
    public int MaxShipsBack =>
        _partner is null || !CombinedShipsWithinCap
            ? 0
            : Math.Max(0, Math.Min(_partner.Ships, _ruleset.Naval.JoinMaxShips - _selected.Ships));

    /// <summary>The most supply A can send B.</summary>
    public int MaxSupplyToPartner => _partner is null || !CombinedShipsWithinCap ? 0 : Math.Max(0, _selected.SupplyTons);

    /// <summary>The most supply B can send A.</summary>
    public int MaxSupplyBack => _partner is null || !CombinedShipsWithinCap ? 0 : Math.Max(0, _partner.SupplyTons);

    /// <summary>The most money A can send B.</summary>
    public int MaxMoneyToPartner => _partner is null || !CombinedShipsWithinCap ? 0 : Math.Max(0, _selected.Money);

    /// <summary>The most money B can send A.</summary>
    public int MaxMoneyBack => _partner is null || !CombinedShipsWithinCap ? 0 : Math.Max(0, _partner.Money);

    /// <summary>One press of a ship arrow, clamped to both directions' bounds.</summary>
    public void AdjustShips(int delta) =>
        _shipsNet = Math.Clamp(_shipsNet + delta, -MaxShipsBack, MaxShipsToPartner);

    /// <summary>One press of a supply arrow, clamped to both directions' bounds.</summary>
    public void AdjustSupply(int delta) =>
        _supplyNet = Math.Clamp(_supplyNet + delta, -MaxSupplyBack, MaxSupplyToPartner);

    /// <summary>One press of a money arrow, clamped to both directions' bounds.</summary>
    public void AdjustMoney(int delta) =>
        _moneyNet = Math.Clamp(_moneyNet + delta, -MaxMoneyBack, MaxMoneyToPartner);

    /// <summary>
    /// <c>OK</c>: one <c>fleet-transfer</c> per direction that has a non-zero amount — one line for an
    /// ordinary staging, two when the player staged resources both ways, and none when refused or nothing
    /// is staged.
    /// </summary>
    public IReadOnlyList<string> ComposeOk()
    {
        if (_partner is null || !CombinedShipsWithinCap
            || (_shipsNet == 0 && _supplyNet == 0 && _moneyNet == 0))
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>(2);
        if (_shipsNet > 0 || _supplyNet > 0 || _moneyNet > 0)
        {
            lines.Add(
                $"fleet-transfer {_selected.Id} {_partner.Id} "
                + $"{Math.Max(0, _shipsNet)} {Math.Max(0, _supplyNet)} {Math.Max(0, _moneyNet)}");
        }

        if (_shipsNet < 0 || _supplyNet < 0 || _moneyNet < 0)
        {
            lines.Add(
                $"fleet-transfer {_partner.Id} {_selected.Id} "
                + $"{Math.Max(0, -_shipsNet)} {Math.Max(0, -_supplyNet)} {Math.Max(0, -_moneyNet)}");
        }

        return lines;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public IReadOnlyList<string> Cancel() => Array.Empty<string>();
}

/// <summary>
/// The City menu's <strong>Fortify city</strong> dialog, Godot-free — the orderable points and the
/// <c>order-city … fortify</c> line it composes.
/// </summary>
/// <remarks>
/// The original's <c>TFortifyCity</c> orders <c>0 … (max − current)</c> points at
/// <c>population(k) × points</c>, refused under siege or while an order is already pending
/// <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</c>. The engine keeps its own
/// treasury rule (bug #549, T115): <c>classical-faithful</c> may put the treasury into debt and
/// <c>improved</c> refuses it, so this dialog adds no affordability check of its own.
/// </remarks>
public sealed class FortifyCityModel
{
    /// <summary>The shipped fortify order's own id <c>[confirmed: the same report]</c>.</summary>
    public const string FortifyOrderId = "fortify";

    private readonly CityState _city;
    private readonly CityOrderRule? _rule;

    private FortifyCityModel(CityState city, Ruleset ruleset, CityOrderRule? rule)
    {
        _city = city;
        _rule = rule;
    }

    /// <summary>Builds the model for <paramref name="city"/> on the live <paramref name="state"/>.</summary>
    public static FortifyCityModel ForCity(GameState state, CityState city, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(ruleset);
        var rule = ruleset.CityOrders.Orders.FirstOrDefault(
            candidate => string.Equals(candidate.Id, FortifyOrderId, StringComparison.Ordinal));
        return new FortifyCityModel(city, ruleset, rule);
    }

    /// <summary>The city being fortified.</summary>
    public string CityId => _city.Id;

    /// <summary>The fortify order, or <see langword="null"/> when the ruleset has none.</summary>
    public string OrderId => FortifyOrderId;

    /// <summary>The completed fortification percentage the city already has.</summary>
    public int CurrentPercent =>
        _rule is null ? 0 : FortificationCode.FinishedPercent(_city.FortificationCode, _rule);

    /// <summary>The order's maximum, <c>100</c> in the shipped ruleset, read from the rule.</summary>
    public int MaxPercent => _rule?.MaxPercent ?? 0;

    /// <summary>The lowest orderable points — the range's own floor.</summary>
    public int MinPoints => 0;

    /// <summary>The highest orderable points, <c>(max − current)</c> and zero while an order is pending.</summary>
    public int MaxPoints =>
        _rule is null ? 0 : FortificationCode.MaxOrderablePoints(_city.FortificationCode, _rule);

    /// <summary>Whether a fortify order may be placed at all.</summary>
    public bool CanFortify =>
        _rule is not null
        && FortificationCode.CanPlaceOrder(_city.FortificationCode, _rule)
        && !(_rule.RefusedWhileUnderSiege && _city.UnderSiege);

    /// <summary>Whether the city is under siege.</summary>
    public bool UnderSiege => _city.UnderSiege;

    /// <summary>The refusal to show when <see cref="CanFortify"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage
    {
        get
        {
            if (_rule is null)
            {
                return "This ruleset has no fortify order.";
            }

            if (FortificationCode.IsOrderInProgress(_city.FortificationCode, _rule))
            {
                return "This city is already being fortified.";
            }

            if (CurrentPercent >= _rule.MaxPercent)
            {
                return "This city cannot be fortified any further.";
            }

            if (_rule.RefusedWhileUnderSiege && _city.UnderSiege)
            {
                return "You cannot fortify a city which is under siege.";
            }

            return null;
        }
    }

    /// <summary>The cost of <paramref name="points"/>, <c>population(k) × points × cost-per-point</c>.</summary>
    public int CostFor(int points) =>
        _rule is null
            ? 0
            : _rule.CostPerPointPerPopulationThousand * Math.Clamp(points, 0, MaxPoints) * _city.PopulationThousands;

    /// <summary><c>OK</c>: one <c>order-city … fortify</c>, or <see langword="null"/> when refused or zero.</summary>
    public string? ComposeOk(int points) =>
        CanFortify && points > 0
            ? $"order-city {CityId} {FortifyOrderId} {Math.Min(points, MaxPoints)}"
            : null;

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;
}
