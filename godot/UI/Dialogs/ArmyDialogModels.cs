using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// The Army menu's four dialogs, Godot-free — each dialog's limits and the command lines it composes.
/// <see cref="ArmyTransferModel"/> backs the Transfer unit dialog (and, reusing its two-list shape, Split
/// army), while <see cref="ChangeUnitsModel"/> carries the single-unit verbs. The Godot controls
/// (<see cref="ArmyTransferDialog"/>, <see cref="SplitArmyDialog"/>, <see cref="ChangeUnitsDialog"/>) own
/// only widgets and submit what these types compose; the test project compiles this file directly, so it
/// must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Task T111</strong> (<c>docs/tasks/T111.md</c>): the Unit map's Army entries, each in its own
/// dialog. All rules live here so a plain xunit test
/// (<c>tests/IC2.Engine.Tests/Ui/ArmyDialogModelsTests.cs</c>) pins every limit and every composed line
/// without the Godot SDK. Every <em>limit</em> is read from the ruleset or the engine — the 198-army cap
/// (<see cref="ArmyManagementRules.MaxArmies"/>), the two-unit split floor
/// (<see cref="ArmyManagementRules.SplitMinUnits"/>), the dialog supply room
/// (<see cref="SupplyCapacity.ArmyDialogCapacityTons"/>) and the purse cap
/// (<see cref="EconomyRules.PurseCapPerUnit"/>) — never a C# literal. Only the confirmed step sizes are
/// named constants.
/// </para>
/// <para>
/// <strong>Regiment quality captions.</strong> The report
/// <c>army-records-and-roman-roster.md</c> maps the roster's tiers 6–9 to <em>average</em>, <em>good</em>,
/// <em>very good</em> and <em>elite</em>, and <c>city-units-army-transfer-and-mercenaries.md</c> supports
/// 5 as <em>poor</em>. A tier below 5 shows its number, the task entry's own instruction, until the
/// original's captions for those tiers are read (<c>[open]</c>).
/// </para>
/// </remarks>
public static class ArmyDialogModels
{
    /// <summary>The 10s stepper's step, supply tons [confirmed: code,
    /// <c>2026-10-03-army-to-army-ok-supply-rebalancing.md</c> item 6].</summary>
    public const int SupplyStepTons = 10;

    /// <summary>The 100s stepper's step, supply tons [confirmed: code, the same report].</summary>
    public const int SupplyLargeStepTons = 100;

    /// <summary>The 10s stepper's step, talents [confirmed: code, the same report].</summary>
    public const int MoneyStepTalents = 10;

    /// <summary>The 100s stepper's step, talents [confirmed: code, the same report].</summary>
    public const int MoneyLargeStepTalents = 100;

    /// <summary>
    /// The message Transfer unit and Join armies show when the game picks no partner — the task entry's
    /// own <c>[designed]</c> wording.
    /// </summary>
    public const string NoPartnerMessage = "There is no army next to this one.";

    /// <summary>
    /// The original's refusal for Split army on a one-unit army — <c>TUnitMap_SplitArmy</c>
    /// <c>[derived: code]</c>.
    /// </summary>
    public const string SplitOneUnitRefusal = "You can not split an army containing only 1 unit.";

    /// <summary>
    /// What Split army says, and does nothing else, for an army aboard a fleet (or a carrying fleet
    /// selected) <c>[designed wording; the engine's own refusal is <c>armies.army-embarked</c>]</c>.
    /// </summary>
    public const string SplitAboardRefusal = "An army aboard a fleet cannot be split.";

    /// <summary>
    /// The quality caption the army panel and the unit list print for a raw quality tier — the roster's
    /// own words for 5–9, and the tier's number below 5 (see this class's remarks).
    /// </summary>
    public static string QualityCaption(int quality) => quality switch
    {
        5 => "poor",
        6 => "average",
        7 => "good",
        8 => "very good",
        9 => "elite",
        _ => quality.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Join armies composes nothing without a partner (Done-when 1); with one, the selected army is the
    /// survivor and the partner is absorbed.
    /// </summary>
    public static string? JoinArmiesLine(string selectedArmyId, ArmyState? partner) =>
        partner is null ? null : $"join-armies {selectedArmyId} {partner.Id}";

    /// <summary>Disband army, after the confirmation prompt answers Yes.</summary>
    public static string DisbandArmyLine(string armyId) => $"disband-army {armyId}";
}

/// <summary>
/// The Transfer unit dialog's Godot-free model — two armies' unit lists, the units staged each way, and
/// the supply and money spinners' limits. One dialog <c>OK</c> composes <em>exactly one</em>
/// <c>army-transfer</c> (<c>docs/tasks/T117.md</c>), with the selected army first.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The units and both directions.</strong> The dialog's first list is the selected army (A) and
/// its second is the game's pick, the partner (B). A unit staged from A is carried as <c>units=</c>, a
/// unit staged back from B as <c>back-units=</c>; the indexes are into each source list as it stands when
/// the dialog opened.
/// </para>
/// <para>
/// <strong>The supply and money spinners' limits</strong> <c>[confirmed: code, FUN_00442310 and
/// 2026-10-03-army-to-army-ok-supply-rebalancing.md item 6]</c>: the supply spinner moves
/// <c>min(step, receiver's room, giver's supply)</c> and the receiver's room is
/// <c>troops div 100 + 1 − supply</c> — exactly <see cref="SupplyCapacity.ArmyDialogCapacityTons"/> minus
/// the receiver's stock, floored at 0. The money spinner moves
/// <c>min(step, 1000 − receiver's money, giver's money)</c>, the 1,000 read from
/// <see cref="EconomyRules.PurseCapPerUnit"/>. The spinners share one signed net per resource, so the
/// command carries a single <c>supply=</c>/<c>back-supply=</c> and <c>money=</c>/<c>back-money=</c>.
/// </para>
/// </remarks>
public sealed class ArmyTransferModel
{
    private readonly Ruleset _ruleset;
    private readonly ArmyState _selected;
    private readonly ArmyState _partner;
    private readonly List<int> _unitsToPartner = new();
    private readonly List<int> _unitsBack = new();
    private int _supplyNet;
    private int _moneyNet;

    private ArmyTransferModel(ArmyState selected, ArmyState partner, Ruleset ruleset)
    {
        _selected = selected;
        _partner = partner;
        _ruleset = ruleset;
    }

    /// <summary>Builds the dialog's model for the selected army A and its game-picked partner B.</summary>
    public static ArmyTransferModel ForArmies(ArmyState selected, ArmyState partner, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new ArmyTransferModel(selected, partner, ruleset);
    }

    /// <summary>A, the selected army — the command's first id.</summary>
    public string SelectedArmyId => _selected.Id;

    /// <summary>B, the game-picked partner — the command's second id.</summary>
    public string PartnerArmyId => _partner.Id;

    /// <summary>Indexes into A's own list, moved A → B as <c>units=</c>.</summary>
    public IReadOnlyList<int> UnitsToPartner => _unitsToPartner;

    /// <summary>Indexes into B's own list, moved B → A as <c>back-units=</c>.</summary>
    public IReadOnlyList<int> UnitsBack => _unitsBack;

    /// <summary>The staged supply net: positive is A → B, negative is B → A.</summary>
    public int SupplyNet => _supplyNet;

    /// <summary>The staged money net: positive is A → B, negative is B → A.</summary>
    public int MoneyNet => _moneyNet;

    /// <summary>The most supply A can send B: <c>min(A's supply, B's room)</c>.</summary>
    public int MaxSupplyToPartner =>
        Math.Max(0, Math.Min(_selected.SupplyTons, Room(_partner)));

    /// <summary>The most supply B can send A: <c>min(B's supply, A's room)</c>.</summary>
    public int MaxSupplyBack =>
        Math.Max(0, Math.Min(_partner.SupplyTons, Room(_selected)));

    /// <summary>The most money A can send B: <c>min(A's money, 1000 − B's money)</c>.</summary>
    public int MaxMoneyToPartner =>
        Math.Max(0, Math.Min(_selected.Money, PurseCap - _partner.Money));

    /// <summary>The most money B can send A: <c>min(B's money, 1000 − A's money)</c>.</summary>
    public int MaxMoneyBack =>
        Math.Max(0, Math.Min(_partner.Money, PurseCap - _selected.Money));

    /// <summary>Stages one of A's units for the move to B; an already-staged index is ignored.</summary>
    public void StageUnitToPartner(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _selected.Units.Count)
        {
            return;
        }

        if (!_unitsToPartner.Contains(selectedIndex))
        {
            _unitsToPartner.Add(selectedIndex);
        }
    }

    /// <summary>Stages one of B's units for the move back to A; an already-staged index is ignored.</summary>
    public void StageUnitBack(int partnerIndex)
    {
        if (partnerIndex < 0 || partnerIndex >= _partner.Units.Count)
        {
            return;
        }

        if (!_unitsBack.Contains(partnerIndex))
        {
            _unitsBack.Add(partnerIndex);
        }
    }

    /// <summary>Puts back one of A's staged units (the dialog's second Transfer on it).</summary>
    public void UnstageUnitToPartner(int selectedIndex) => _unitsToPartner.Remove(selectedIndex);

    /// <summary>Puts back one of B's staged units (the dialog's second Transfer on it).</summary>
    public void UnstageUnitBack(int partnerIndex) => _unitsBack.Remove(partnerIndex);

    /// <summary>
    /// One press of a supply arrow: moves <paramref name="delta"/>, clamped to the giver's own stock and
    /// the receiver's room on that side. A press of 0 does nothing.
    /// </summary>
    public void AdjustSupply(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _supplyNet = Math.Clamp(_supplyNet + delta, -MaxSupplyBack, MaxSupplyToPartner);
    }

    /// <summary>
    /// One press of a money arrow: moves <paramref name="delta"/>, clamped to the giver's own purse and
    /// the receiver's remaining room under the purse cap on that side.
    /// </summary>
    public void AdjustMoney(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _moneyNet = Math.Clamp(_moneyNet + delta, -MaxMoneyBack, MaxMoneyToPartner);
    }

    /// <summary>
    /// <c>OK</c>: exactly one <c>army-transfer</c> for the whole dialog, or <see langword="null"/> when
    /// nothing is staged. The selected army is the first id, the units moved to the partner are
    /// <c>units=</c>, the units moved back are <c>back-units=</c>, and each resource is netted to a single
    /// direction.
    /// </summary>
    public string? ComposeOk()
    {
        var line = $"army-transfer {SelectedArmyId} {PartnerArmyId}";
        var any = false;

        if (_unitsToPartner.Count > 0)
        {
            line += $" units={Join(_unitsToPartner)}";
            any = true;
        }

        if (_unitsBack.Count > 0)
        {
            line += $" back-units={Join(_unitsBack)}";
            any = true;
        }

        if (_supplyNet > 0)
        {
            line += $" supply={_supplyNet}";
            any = true;
        }
        else if (_supplyNet < 0)
        {
            line += $" back-supply={-_supplyNet}";
            any = true;
        }

        if (_moneyNet > 0)
        {
            line += $" money={_moneyNet}";
            any = true;
        }
        else if (_moneyNet < 0)
        {
            line += $" back-money={-_moneyNet}";
            any = true;
        }

        return any ? line : null;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;

    private int PurseCap => _ruleset.Economy.PurseCapPerUnit;

    /// <summary>The receiver's dialog room: <c>troops div 100 + 1 − stock</c>, floored at 0.</summary>
    private int Room(ArmyState receiver) =>
        Math.Max(0, SupplyCapacity.ArmyDialogCapacityTons(receiver.TotalTroops, _ruleset) - receiver.SupplyTons);

    private static string Join(IEnumerable<int> indexes) =>
        string.Join(',', indexes.Select(i => i.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// The Split army dialog's Godot-free model. The dialog is the original's army-to-army form
/// (<c>TArmyToArmy</c>, titled <em>"Split army"</em>): A is the selected army and B the new army, which
/// opens with no units, supply or money. The units staged to B's list, and the supply and money moved
/// to it, compose <em>one</em> <c>split-army</c> on <c>OK</c> (<c>docs/tasks/T141.md</c>'s form).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limits</strong> <c>[confirmed: code, 2026-10-03-army-to-army-ok-supply-rebalancing.md,
/// "Split army uses the same form", and item 6]</c>: the supply spinner moves
/// <c>min(step, B's room, A's supply)</c> with B's room <c>troops div 100 + 1 − supply</c> (B's troops
/// being the staged units', its supply 0), and the money spinner
/// <c>min(step, 1000 − B's money, A's money)</c> with the 1,000 read from
/// <see cref="EconomyRules.PurseCapPerUnit"/>. Both steps are 10 and 100. The selected army keeps at
/// least one unit (the engine's <c>armies.invalid-unit-selection</c>), so the last unit cannot be staged.
/// </para>
/// <para>
/// <strong>An army aboard a fleet</strong> is refused (<see cref="ArmyDialogModels.SplitAboardRefusal"/>)
/// <c>[designed, no confirmed evidence either way]</c>, as <c>SplitArmyCommand</c>'s remarks say.
/// </para>
/// <para>
/// <strong>Disband</strong> under either list submits <c>disband-unit &lt;army&gt; &lt;index&gt;</c> at
/// once, the index being the selected army's own index of that unit; <see cref="ApplyDisband"/> then
/// re-reads the army and drops a staged unit from the staging.
/// </para>
/// </remarks>
public sealed class SplitArmyModel
{
    private readonly Ruleset _ruleset;
    private readonly int _nationArmyCount;
    private ArmyState _army;
    private readonly List<int> _staged = new();
    private int _supply;
    private int _money;

    private SplitArmyModel(ArmyState army, Ruleset ruleset, string newArmyId, int nationArmyCount)
    {
        _army = army;
        _ruleset = ruleset;
        NewArmyId = newArmyId;
        _nationArmyCount = nationArmyCount;
    }

    /// <summary>Builds the model for <paramref name="army"/>, the new army's id and the nation's live count.</summary>
    public static SplitArmyModel ForArmy(GameState state, ArmyState army, Ruleset ruleset, string newArmyId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);
        var count = state.Armies.Count(a => string.Equals(a.Nation, army.Nation, StringComparison.Ordinal));
        return new SplitArmyModel(army, ruleset, newArmyId, count);
    }

    /// <summary>The army being split (A).</summary>
    public string ArmyId => _army.Id;

    /// <summary>The new army's id (B), chosen by the dialog.</summary>
    public string NewArmyId { get; }

    /// <summary>The split floor, from the ruleset's own <see cref="ArmyManagementRules.SplitMinUnits"/>.</summary>
    public int MinUnits => _ruleset.ArmyManagement.SplitMinUnits;

    /// <summary>The army's live unit count.</summary>
    public int UnitCount => _army.Units.Count;

    /// <summary>Whether the army is aboard a fleet, which the engine refuses to split.</summary>
    public bool IsAboard => _army.IsEmbarked;

    /// <summary>
    /// Whether the split can be offered: not aboard a fleet, at least <see cref="MinUnits"/> units and the
    /// nation below its <see cref="ArmyManagementRules.MaxArmies"/> cap.
    /// </summary>
    public bool CanSplit =>
        !IsAboard && _army.Units.Count >= MinUnits && _nationArmyCount < _ruleset.ArmyManagement.MaxArmies;

    /// <summary>The refusal the dialog shows when <see cref="CanSplit"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        CanSplit
            ? null
            : IsAboard
                ? ArmyDialogModels.SplitAboardRefusal
                : _army.Units.Count < MinUnits
                    ? ArmyDialogModels.SplitOneUnitRefusal
                    : "You have reached your limit of armies.";

    /// <summary>Indexes into A's own list (as it stands now) staged for the new army, ascending.</summary>
    public IReadOnlyList<int> StagedUnits => _staged;

    /// <summary>The supply staged toward the new army, in tons.</summary>
    public int Supply => _supply;

    /// <summary>The money staged toward the new army, in talents.</summary>
    public int Money => _money;

    /// <summary>The new army's room for supply: <c>troops div 100 + 1 − 0</c>, from the staged units.</summary>
    public int NewArmyRoom =>
        _staged.Count == 0
            ? 0
            : SupplyCapacity.ArmyDialogCapacityTons(
                _staged.Sum(i => _army.Units[i].Troops), _ruleset);

    /// <summary>The most supply that can move: <c>min(A's supply, B's room)</c>.</summary>
    public int MaxSupply => Math.Max(0, Math.Min(_army.SupplyTons, NewArmyRoom));

    /// <summary>The most money that can move: <c>min(A's money, 1000 − B's money)</c> (B's money is 0).</summary>
    public int MaxMoney => Math.Max(0, Math.Min(_army.Money, _ruleset.Economy.PurseCapPerUnit));

    /// <summary>
    /// Stages one of A's units for the new army. Out-of-range and already-staged indexes are ignored,
    /// and so is the unit that would leave A empty.
    /// </summary>
    public void StageUnit(int index)
    {
        if (index < 0 || index >= _army.Units.Count || _staged.Contains(index)
            || _staged.Count + 1 >= _army.Units.Count)
        {
            return;
        }

        _staged.Add(index);
        _staged.Sort();
        Reclamp();
    }

    /// <summary>Puts a staged unit back in A's list.</summary>
    public void UnstageUnit(int index)
    {
        _staged.Remove(index);
        Reclamp();
    }

    /// <summary>One press of a supply arrow, clamped to <c>[0, <see cref="MaxSupply"/>]</c>.</summary>
    public void AdjustSupply(int delta) => _supply = Math.Clamp(_supply + delta, 0, MaxSupply);

    /// <summary>One press of a money arrow, clamped to <c>[0, <see cref="MaxMoney"/>]</c>.</summary>
    public void AdjustMoney(int delta) => _money = Math.Clamp(_money + delta, 0, MaxMoney);

    /// <summary>
    /// <c>OK</c>: exactly one <c>split-army</c> with every staged unit and the staged supply and money,
    /// or <see langword="null"/> when the split is refused or no unit is staged.
    /// </summary>
    public string? ComposeOk()
    {
        if (!CanSplit || _staged.Count == 0)
        {
            return null;
        }

        var line = $"split-army {ArmyId} {NewArmyId} "
            + string.Join(',', _staged.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        if (_supply > 0)
        {
            line += $" supply={_supply}";
        }

        if (_money > 0)
        {
            line += $" money={_money}";
        }

        return line;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;

    /// <summary>
    /// A Disband under either list: <c>disband-unit</c> naming the selected army's own index of the unit.
    /// </summary>
    public string DisbandLine(int armyIndex) => $"disband-unit {ArmyId} {armyIndex}";

    /// <summary>
    /// After a <c>disband-unit</c> on <paramref name="removedIndex"/> succeeded: takes the refreshed army,
    /// drops the unit from the staging if it was staged, and shifts the later staged indexes down.
    /// </summary>
    public void ApplyDisband(int removedIndex, ArmyState refreshed)
    {
        ArgumentNullException.ThrowIfNull(refreshed);
        _army = refreshed;
        _staged.Remove(removedIndex);
        for (var i = 0; i < _staged.Count; i++)
        {
            if (_staged[i] > removedIndex)
            {
                _staged[i]--;
            }
        }

        _staged.RemoveAll(i => i >= _army.Units.Count);
        Reclamp();
    }

    private void Reclamp()
    {
        _supply = Math.Clamp(_supply, 0, MaxSupply);
        _money = Math.Clamp(_money, 0, MaxMoney);
    }
}

/// <summary>
/// The Change units dialog's Godot-free model — the selected army's units, the selection the verbs act
/// on, and the command lines T107's <c>rename-unit</c>, <c>split-unit</c> and <c>disband-unit</c> plus the
/// existing <c>join-units</c> compose.
/// </summary>
/// <remarks>
/// The original's <c>TChangeArmyUnits</c> has Rename unit, Split unit, Join units, Disband, OK and
/// Cancel. This engine exposes each verb as its own command, so each button composes and submits the one
/// command it names; OK and Cancel simply close the dialog.
/// </remarks>
public sealed class ChangeUnitsModel
{
    private readonly ArmyState _army;

    private ChangeUnitsModel(ArmyState army)
    {
        _army = army;
    }

    /// <summary>Builds the model for the army whose units the dialog lists.</summary>
    public static ChangeUnitsModel ForArmy(ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(army);
        return new ChangeUnitsModel(army);
    }

    /// <summary>The army whose units are listed.</summary>
    public string ArmyId => _army.Id;

    /// <summary>The army's units, in the engine's own order.</summary>
    public IReadOnlyList<UnitSlot> Units => _army.Units;

    /// <summary>Rename unit composes <c>rename-unit</c>.</summary>
    public string RenameLine(int unitIndex, string name) => $"rename-unit {ArmyId} {unitIndex} {name}";

    /// <summary>Split unit composes <c>split-unit</c>.</summary>
    public string SplitUnitLine(int unitIndex, int troops) => $"split-unit {ArmyId} {unitIndex} {troops}";

    /// <summary>Join units composes <c>join-units</c>, the first index surviving.</summary>
    public string JoinUnitsLine(int firstUnitIndex, int secondUnitIndex) =>
        $"join-units {ArmyId} {firstUnitIndex} {secondUnitIndex}";

    /// <summary>Disband composes <c>disband-unit</c>.</summary>
    public string DisbandUnitLine(int unitIndex) => $"disband-unit {ArmyId} {unitIndex}";
}
