using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Recruitment;

namespace IC2.Slice.UI;

/// <summary>
/// The Strategy menu's four dialogs, Godot-free — the tax preview, the build-fleet refusals, the
/// build-fleet order, and the recruit-unit cost preview. Each is a small, pure data structure the
/// <see cref="TaxationDialog"/>, <see cref="BalanceSheetDialog"/>, <see cref="RecruitUnitDialog"/> and
/// <see cref="BuildFleetDialog"/> controls read to build their UI; the test project compiles this file
/// directly, so it must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Task T109</strong> (<c>docs/tasks/T109.md</c>): "the four Strategy entries that T100 left
/// disabled open the original's dialogs, from the menu and from the toolbar alike." All rules live here
/// so a plain xunit test (<c>tests/IC2.Engine.Tests/Ui/StrategyDialogModelsTests.cs</c>) can pin every
/// limit and every composed line without the Godot SDK. The four dialogs and the table that drives
/// their menu entries are the audit's own inventory: <c>TChangeTax</c>, <c>TBalanceSheet</c>,
/// <c>TArmyRecruits</c> and <c>TBuildFleet</c>
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.3).
/// </para>
/// <para>
/// <strong>No design numbers are invented here.</strong> Every value traces to the ruleset, the
/// corpus, the cited report, or the engine's own formula. Where the original UI's own shape (a
/// stepper step, a default, a bound) is named in the brief, it comes from the ruleset fields the
/// engine already reads, so a ruleset change moves the dialog with it.
/// </para>
/// </remarks>
public static class StrategyDialogModels
{
    /// <summary>
    /// The Taxation dialog's own per-arrow step. The original's <c>TChangeTax</c> slider reports line
    /// size 1 and page size 5
    /// <strong>[Wine candidate: <c>2026-10-02-unit-map-mouse-orders-and-tax-range.md</c>, (g)]</strong>;
    /// the page size belongs to the dialog's keyboard handling, not the ruleset, so it is the dialog's
    /// own constant.
    /// </summary>
    public const int TaxArrowStep = 1;

    /// <summary>The Taxation dialog's own per-Page-key step.</summary>
    public const int TaxPageStep = 5;

    /// <summary>
    /// The Taxation dialog's verb: the engine's <c>set-tax</c>, which writes
    /// <see cref="NationState.TaxRatePercent"/> on OK
    /// <strong>[confirmed: <c>decompiled-fleet-tax-and-mercenary-formulas.md</c>]</strong>.
    /// </summary>
    public const string SetTaxCommandVerb = "set-tax";

    /// <summary>
    /// The Recruit unit dialog's verb for placing a new standing-recruitment order, the engine's
    /// <c>recruit-standing</c>. <see cref="RecruitStandingUnitCommandHandler"/> is the rule; the dialog
    /// composes the line, the handler decides.
    /// </summary>
    public const string RecruitStandingCommandVerb = "recruit-standing";

    /// <summary>
    /// The Recruit unit dialog's verb for turning a ready slot into an army unit, the engine's
    /// <c>mobilize</c>. <see cref="IC2.Engine.Recruitment.Commands.MobilizeRecruitSlotCommandHandler"/>
    /// names the receiving army (or creates one beside the city) — the dialog says where the regiment
    /// will appear (issue #520 item 5) by reading the same helper's result before it submits.
    /// </summary>
    public const string MobilizeCommandVerb = "mobilize";

    /// <summary>
    /// The Recruit unit dialog's verb for disbanding a queued recruitment, the engine's
    /// <c>disband-slot</c>. <see cref="IC2.Engine.Recruitment.Commands.DisbandRecruitmentSlotCommandHandler"/>
    /// applies the slot shift, the dialog owns the confirmation prompt
    /// <strong>[derived: code, <c>TArmyRecruits_DisbandUnits</c> :56133-56148; no refund: Wine candidate,
    /// <c>2026-10-05-disbanding-a-queued-recruitment.md</c>, research <c>9ae8924</c>]</strong>.
    /// </summary>
    public const string DisbandSlotCommandVerb = "disband-slot";

    /// <summary>
    /// The Build fleet dialog's verb, the engine's <c>order-fleet</c>.
    /// <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> applies the under-construction
    /// write, the 24-tick countdown, and the treasury debit. The dialog composes the line.
    /// </summary>
    public const string OrderFleetCommandVerb = "order-fleet";

    /// <summary>
    /// The <em>Only nations with coastal cities can build fleets.</em> refusal — the original's
    /// <c>TBuildFleet</c> message when the nation owns no coastal city
    /// <strong>[derived: code, <c>TPremierForm_BuildNewFleet</c>; literal text: form, see
    /// <c>2026-10-05-player-facing-feature-inventory.md</c>]</strong>. The engine's
    /// <see cref="CoastalCity.IsCoastal"/> reads the terrain grid the same way; in the shipped
    /// <c>classical-mediterranean</c> world it agrees with the original for Dacia and Galatia and
    /// accepts Media's two Caspian cities (bug
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/600">#600</see>).
    /// </summary>
    public const string NoCoastalCitiesRefusal = "Only nations with coastal cities can build fleets.";

    /// <summary>
    /// The <em>You do not have a free coastal city at this time.</em> refusal — the original's
    /// <c>TBuildFleet</c> message when the nation owns coastal cities but every one of them is
    /// already building a fleet
    /// <strong>[derived: code, <c>TPremierForm_BuildNewFleet</c>; literal text: form]</strong>. A
    /// coastal city is "busy" when it has a fleet under construction
    /// (<see cref="FleetState.ConstructionTicksRemaining"/> non-null, with this city as its
    /// <see cref="FleetState.BuildCityId"/>).
    /// </summary>
    public const string NoFreeCoastalCityRefusal = "You do not have a free coastal city at this time.";

    /// <summary>
    /// The <em>You cannot build a fleet at this time.</em> refusal — the original's <c>TBuildFleet</c>
    /// message for the catch-all that follows the first two
    /// <strong>[derived: code, <c>TPremierForm_BuildNewFleet</c>; literal text: form]</strong>. The
    /// dialog never composes this; the engine returns it for an order the dialog did not gate, and the
    /// dialog shows whatever the engine says. The literal is here so a unit test can pin it.
    /// </summary>
    public const string CannotBuildFleetRefusal = "You cannot build a fleet at this time.";

    /// <summary>
    /// The "Yes" button's caption on the Disband confirmation prompt — the original's
    /// <c>TArmyRecruits_DisbandUnits</c> own form button text. The "No / Cancel" pair share the same
    /// captions; only Yes acts
    /// <strong>[derived: code, <c>TArmyRecruits_DisbandUnits</c> :56133-56148]</strong>.
    /// </summary>
    public const string DisbandYesCaption = "Yes";

    /// <summary>The "No" button's caption on the Disband confirmation prompt.</summary>
    public const string DisbandNoCaption = "No";

    /// <summary>The "Cancel" button's caption on the Disband confirmation prompt.</summary>
    public const string DisbandCancelCaption = "Cancel";

    /// <summary>
    /// The Taxation dialog's title — the audit's own transcription of the original's form caption
    /// <strong>[confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.3]</strong>.
    /// </summary>
    public const string TaxationTitle = "Change tax level";

    /// <summary>The Recruit unit dialog's title — the audit's own "Army recruits".</summary>
    public const string RecruitUnitTitle = "Army recruits";

    /// <summary>The Build fleet dialog's title.</summary>
    public const string BuildFleetTitle = "Build fleet";

    /// <summary>The Balance sheet dialog's title.</summary>
    public const string BalanceSheetTitle = "Balance sheet";

    private const string InvariantCultureName = "en-US";
}

/// <summary>
/// The Taxation dialog's Godot-free model — the slider's inclusive bounds, the new rate and the
/// income the preview shows, and the <c>set-tax</c> line the OK button submits. The Godot control
/// (<see cref="TaxationDialog"/>) owns widgets and the page-key handler; this type owns every value
/// the dialog renders.
/// </summary>
/// <remarks>
/// <para>
/// The two inclusive bounds live in the ruleset's <c>economy</c> block
/// (<see cref="EconomyRules.TaxRateMinPercent"/> and <see cref="EconomyRules.TaxRateMaxPercent"/>);
/// <see cref="IC2.Engine.Economy.Commands.SetTaxRateCommandHandler"/> reads them from there, the
/// dialog reads them from the same place, so the slider and the engine's range check can never
/// disagree.
/// </para>
/// <para>
/// <strong>The preview is the engine's own <see cref="TaxIncome"/>.</strong> The original's
/// <c>TChangeTax_PrintNewNumbers</c> shows <c>taxBase × taxRate / 100</c>
/// <strong>[confirmed: <c>decompiled-fleet-tax-and-mercenary-formulas.md</c>]</strong>; the dialog's
/// preview calls <see cref="TaxIncome.Compute"/> rather than restating the formula, so a ruleset
/// change to <see cref="EconomyRules.TaxRateDivisor"/> moves the engine and the preview together.
/// </para>
/// </remarks>
public sealed class TaxationDialogModel
{
    private readonly Ruleset _ruleset;

    private TaxationDialogModel(GameState state, Ruleset ruleset)
    {
        _ruleset = ruleset;
        State = state;
        Nation = state.NationById(state.ActiveNationId)
            ?? throw new ArgumentException(
                $"Active nation '{state.ActiveNationId}' is not in the state.", nameof(state));
    }

    /// <summary>The Taxation dialog's model for the active seat's nation.</summary>
    /// <exception cref="ArgumentException">The state's active nation is not a known nation.</exception>
    public static TaxationDialogModel ForActiveNation(GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new TaxationDialogModel(state, ruleset);
    }

    /// <summary>The state the model was built against — for downstream UI calls.</summary>
    public GameState State { get; }

    /// <summary>The nation whose tax rate the dialog edits — the active seat, never a foreign nation.</summary>
    public NationState Nation { get; }

    /// <summary>The slider's minimum — the ruleset's <see cref="EconomyRules.TaxRateMinPercent"/>.</summary>
    public int MinimumRate => _ruleset.Economy.TaxRateMinPercent;

    /// <summary>The slider's maximum — the ruleset's <see cref="EconomyRules.TaxRateMaxPercent"/>.</summary>
    public int MaximumRate => _ruleset.Economy.TaxRateMaxPercent;

    /// <summary>The nation's current rate, exactly as the engine stored it.</summary>
    public int CurrentRate => Nation.TaxRatePercent;

    /// <summary>
    /// The income the slider's "current" row shows — <see cref="TaxIncome.Compute"/> for the
    /// nation's own tax base and the ruleset's own divisor. The original's preview prints this same
    /// figure on entry; the dialog recomputes it on every rate change so the "new" row updates with
    /// the slider.
    /// </summary>
    public int CurrentIncome => TaxIncome.Compute(Nation.TaxBase, CurrentRate, _ruleset);

    /// <summary>The income the "new" row would show for <paramref name="rate"/>.</summary>
    public int IncomeFor(int rate) => TaxIncome.Compute(Nation.TaxBase, rate, _ruleset);

    /// <summary>
    /// Clamps <paramref name="rate"/> to the ruleset's inclusive <c>[min, max]</c> range, exactly as
    /// the slider does at value-set time. A page key that would move the rate past a bound stops at
    /// the bound (Done-when 3).
    /// </summary>
    public int Clamp(int rate) => Math.Clamp(rate, MinimumRate, MaximumRate);

    /// <summary>
    /// The command line the OK button submits, in the form <c>"set-tax &lt;percent&gt;"</c> — the
    /// engine's own <see cref="IC2.Engine.Economy.Commands.SetTaxRateCommandHandler"/> reads the rate
    /// back from the second token and range-checks it against the same ruleset bounds.
    /// </summary>
    public string SetTaxLine(int rate) =>
        $"{StrategyDialogModels.SetTaxCommandVerb} {Clamp(rate).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The Build fleet dialog's Godot-free model — the three pre-open refusals, the ship range and
/// cost, the line an OK button composes, and the list of fleets this nation is already building
/// (the dialog's "under construction" rows). The Godot control (<see cref="BuildFleetDialog"/>)
/// owns widgets and submits; this type owns every value the dialog renders.
/// </summary>
/// <remarks>
/// <para>
/// The refusal order is the original's <c>TBuildFleet</c> own order
/// <strong>[derived: code, <c>TPremierForm_BuildNewFleet</c>]</strong>:
/// no-coastal-cities first, no-free-coastal-city second, catch-all last. The dialog shows the
/// first that fires, or the order form when none does.
/// </para>
/// <para>
/// <strong>The fleet port is the engine's own pick.</strong> The original's "The fleet will be built
/// at &lt;C&gt;." line names a single port; in the 13 nations' starts the report lists one port
/// each (<c>2026-10-02-start-as-each-nation.md</c>), and whether a player could choose another
/// remains <strong>[open]</strong> (task Hazards, the research question the review may ask).
/// <see cref="FreeCoastalCityId"/> therefore offers the free coastal cities
/// <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> itself accepts, and the OK
/// button submits to the first one — the engine's own coastal test, never the dialog's. The
/// shipped ruleset fixes the candidate count to one for nearly every nation, so the dialog and
/// the engine agree.
/// </para>
/// </remarks>
public sealed class BuildFleetDialogModel
{
    private readonly GameState _state;
    private readonly Ruleset _ruleset;
    private readonly World _world;

    private BuildFleetDialogModel(GameState state, Ruleset ruleset, World world)
    {
        _state = state;
        _ruleset = ruleset;
        _world = world;
        Nation = state.NationById(state.ActiveNationId)
            ?? throw new ArgumentException(
                $"Active nation '{state.ActiveNationId}' is not in the state.", nameof(state));
    }

    /// <summary>The Build fleet dialog's model for the active seat's nation.</summary>
    public static BuildFleetDialogModel ForActiveNation(GameState state, Ruleset ruleset, World world)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        return new BuildFleetDialogModel(state, ruleset, world);
    }

    /// <summary>The nation whose fleet the dialog orders — the active seat, never a foreign nation.</summary>
    public NationState Nation { get; }

    /// <summary>The minimum ship count — the ruleset's <see cref="NavalRules.OrderMinShips"/>.</summary>
    public int MinShips => _ruleset.Naval.OrderMinShips;

    /// <summary>The maximum ship count — the ruleset's <see cref="NavalRules.OrderMaxShips"/>.</summary>
    public int MaxShips => _ruleset.Naval.OrderMaxShips;

    /// <summary>The initial cost of <paramref name="ships"/> ships — <c>ships × BuildCostPerShip</c>.</summary>
    public int InitialCostFor(int ships) => ships * _ruleset.Naval.BuildCostPerShip;

    /// <summary>
    /// The first refusal the dialog shows, in the brief's order, or <see langword="null"/> when the
    /// nation may place an order. The engine applies its own refusals on the order path; the dialog
    /// adds none of its own.
    /// </summary>
    public string? PreOpenRefusalMessage
    {
        get
        {
            if (FreeCoastalCities.Count == 0 && CoastalCities.Count == 0)
            {
                return StrategyDialogModels.NoCoastalCitiesRefusal;
            }

            if (FreeCoastalCities.Count == 0)
            {
                return StrategyDialogModels.NoFreeCoastalCityRefusal;
            }

            return null;
        }
    }

    /// <summary>
    /// The nation's own coastal cities, in the world's order — the cities
    /// <see cref="CoastalCity.IsCoastal"/> recognises. The list is empty for Dacia and Galatia in the
    /// shipped <c>classical-mediterranean</c> world, so they see the first refusal; Media's two
    /// Caspian cities are here too (bug
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/600">#600</see>), and the
    /// engine accepts the order.
    /// </summary>
    public IReadOnlyList<CityState> CoastalCities
    {
        get
        {
            var list = new List<CityState>();
            foreach (var city in _state.Cities)
            {
                if (!string.Equals(city.Owner, Nation.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (CoastalCity.IsCoastal(city, _world))
                {
                    list.Add(city);
                }
            }

            return list;
        }
    }

    /// <summary>
    /// The coastal cities this nation owns that are not already building a fleet — the cities
    /// <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> will accept an
    /// <c>order-fleet</c> for. The dialog's OK composes the line against the first one; the engine
    /// applies the same test, so the order never reaches the handler for a city not on this list.
    /// </summary>
    public IReadOnlyList<CityState> FreeCoastalCities
    {
        get
        {
            var busy = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fleet in _state.Fleets)
            {
                if (!string.Equals(fleet.Nation, Nation.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (fleet.IsUnderConstruction && fleet.BuildCityId is { } buildCity)
                {
                    busy.Add(buildCity);
                }
            }

            var list = new List<CityState>();
            foreach (var city in CoastalCities)
            {
                if (!busy.Contains(city.Id))
                {
                    list.Add(city);
                }
            }

            return list;
        }
    }

    /// <summary>
    /// The fleets this nation is already building — the dialog's "under construction" rows, each
    /// rendered as <em>"A fleet of N ships will be ready in W weeks at C."</em> (the original's
    /// literal, see the brief's <c>What the original's dialogs show</c> for the audit row).
    /// </summary>
    /// <remarks>
    /// The "weeks" figure is the fleet's <see cref="FleetState.ConstructionTicksRemaining"/> —
    /// <c>FleetTickSystem</c> decrements it every weekly tick
    /// (<see cref="NavalRules.ConstructionTicks"/>, 24 in the shipped ruleset, so a fresh order shows
    /// "24 weeks"). Two weeks per turn: the report says 24 ticks for 12 turns, and the ruleset has
    /// <c>Calendar.WeekStep = 2</c>, so a fresh fleet is "ready in 12 turns" or, in the dialog's
    /// weekly-tick wording, "24 weeks" — the dialog picks the tick count the engine stores.
    /// </remarks>
    public IReadOnlyList<UnderConstructionFleet> UnderConstructionFleets
    {
        get
        {
            var list = new List<UnderConstructionFleet>();
            foreach (var fleet in _state.Fleets)
            {
                if (!string.Equals(fleet.Nation, Nation.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!fleet.IsUnderConstruction || fleet.BuildCityId is null)
                {
                    continue;
                }

                var city = _state.CityById(fleet.BuildCityId);
                if (city is null)
                {
                    continue;
                }

                list.Add(new UnderConstructionFleet(
                    fleet.Id, fleet.Ships, fleet.ConstructionTicksRemaining!.Value, city.Name));
            }

            return list;
        }
    }

    /// <summary>
    /// The <c>order-fleet</c> line the OK button submits for <paramref name="ships"/> at
    /// <paramref name="cityId"/>, with a fresh fleet id. The engine's
    /// <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> reads the tokens back and
    /// applies its own refusals.
    /// </summary>
    public string OrderFleetLine(int ships, string cityId, string newFleetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(cityId);
        ArgumentException.ThrowIfNullOrEmpty(newFleetId);
        return $"{StrategyDialogModels.OrderFleetCommandVerb} {cityId} {ships.ToString(System.Globalization.CultureInfo.InvariantCulture)} {newFleetId}";
    }
}

/// <summary>
/// One row of the Build fleet dialog's "under construction" listing — the id the engine stored it
/// under, the ship count, the ticks left on its countdown, and the build city's name.
/// </summary>
/// <param name="FleetId">The fleet's own id, as the engine stored it.</param>
/// <param name="Ships">The ordered ship count, copied from the engine's record.</param>
/// <param name="WeeksRemaining">
/// The ticks left on <see cref="FleetState.ConstructionTicksRemaining"/>. The dialog renders this as
/// <em>W weeks</em> in the original's "A fleet of N ships will be ready in W weeks at C." literal.
/// </param>
/// <param name="CityName">The build city's name, or its id when the city has no name.</param>
public sealed record UnderConstructionFleet(
    string FleetId,
    int Ships,
    int WeeksRemaining,
    string CityName);
