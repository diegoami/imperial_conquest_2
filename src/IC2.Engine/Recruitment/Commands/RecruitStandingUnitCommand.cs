using IC2.Engine.Core;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Orders standing recruitment of a new unit at one of the issuing nation's own cities, paid from the
/// national treasury — <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Scope: "Standing
/// recruitment from the treasury, into the per-nation recruitment slots T35 adds to the model".
/// </summary>
/// <remarks>
/// Creates one <see cref="Model.RecruitmentSlot"/> on the issuing nation, at <see cref="StandingRecruitmentCost.InitialCost"/>,
/// starting at <c>StateCode 0</c>. <see cref="RecruitmentSlotReadinessSystem"/> advances that state code
/// once per week through T06's <see cref="Calendar.CityUnitStateCode"/>, and
/// <see cref="MobilizeRecruitSlotCommand"/> collects the slot once it is ready — <c>FUN_0044a4e0</c>,
/// decompiled in full in <c>decompiled-mobilization-and-mercenary-restock.md</c> and implemented by T55.
/// The earlier note here, that the helper was undecompiled and so deliberately not implemented, was
/// correct when it was written and is superseded by that report.
/// <para>
/// Placing this order is also what raises the nation's <see cref="Model.NationState.MobilizedPercent"/>
/// — see <see cref="MobilizationRate"/>, and this handler's own remarks for the 40-slot refusal the
/// same report confirms.
/// </para>
/// </remarks>
/// <param name="CityId">The training city — must be owned by <see cref="IssuingNationId"/>.</param>
/// <param name="UnitTypeId">Key into <see cref="Model.Ruleset.UnitTypes"/>.</param>
/// <param name="Troops">The order's troop count.</param>
public sealed record RecruitStandingUnitCommand(
    string IssuingNationId, string CityId, string UnitTypeId, int Troops) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "recruitment.recruit-standing-unit";
}

/// <summary>Rejection codes <see cref="RecruitStandingUnitCommandHandler"/> declares.</summary>
public static class RecruitStandingUnitRejections
{
    /// <summary>The command names a city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("recruitment.unknown-city");

    /// <summary>The named city belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourCity = new("recruitment.not-your-city");

    /// <summary>The command names a unit type id the ruleset does not define.</summary>
    public static readonly RejectionCode UnknownUnitType = new("recruitment.unknown-unit-type");

    /// <summary>The requested troop count is not positive.</summary>
    public static readonly RejectionCode InvalidTroops = new("recruitment.invalid-troops");

    /// <summary>The issuing nation's treasury cannot afford the order's <see cref="StandingRecruitmentCost.InitialCost"/>.</summary>
    public static readonly RejectionCode InsufficientTreasury = new("recruitment.insufficient-treasury");

    /// <summary>
    /// The issuing nation already has <see cref="Model.RecruitmentRules.MaxSlots"/> units in training —
    /// the original's <em>"You have reached your limit of 40 units."</em>
    /// (T55 Done-when 6; see <see cref="RecruitStandingUnitCommandHandler"/>'s remarks).
    /// </summary>
    public static readonly RejectionCode RecruitmentTableFull = new("recruitment.table-full");
}

/// <summary>
/// Published by <see cref="RecruitStandingUnitCommandHandler"/> once an order is accepted.
/// </summary>
/// <remarks>
/// <strong>Not marked news-worthy.</strong> The exhaustive 24-call-site accounting of every news literal
/// the original EXE can emit (<c>news-log-format-and-messages.md</c> Q4, "the EXE has 24 calls to the
/// writer, using 21 templates") contains no entry for standing recruitment completing or being ordered —
/// only <see cref="Model.NewsLog"/>-worthy events the original itself raises belong in
/// <see cref="News.NewsMessageCatalog"/>, which this task's Owns list does not cover in any case
/// (<c>src/IC2.Engine/News/**</c> belongs to T10). See this task's PR body for the full note on Done-when 6.
/// </remarks>
/// <param name="NationId">The ordering nation.</param>
/// <param name="CityId">The training city.</param>
/// <param name="UnitTypeId">The unit type ordered.</param>
/// <param name="Troops">The order's troop count.</param>
/// <param name="TalentsPaid">The treasury debit — <see cref="StandingRecruitmentCost.InitialCost"/>.</param>
[DomainEvent("recruitment.standing-unit-ordered")]
public sealed record RecruitmentOrdered(
    string NationId,
    string CityId,
    string UnitTypeId,
    int Troops,
    int TalentsPaid) : DomainEvent;
