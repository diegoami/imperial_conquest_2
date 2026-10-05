using IC2.Engine.Calendar;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;

namespace IC2.Slice.UI;

/// <summary>
/// One regiment in a nation's standing-recruitment queue, as a screen shows it: the type being raised,
/// its troop count, the city training it, and how far it is from mobilization.
/// </summary>
/// <remarks>
/// <strong>Fix #513</strong>: before this, nothing under <c>godot/UI/**</c> displayed
/// <see cref="NationState.RecruitmentSlots"/> at all, so a regiment ordered with <em>Recruit</em> was
/// invisible until it was mobilized. <see cref="StateCode"/> is the engine's own counter
/// (<see cref="CityUnitStateCode"/> steps it +2 a week, holding at the ruleset's cap);
/// <see cref="IsReady"/> and <see cref="WeeksUntilReady"/> are both read from the engine's
/// <see cref="MobilizationReadiness"/>, never reimplemented here.
/// </remarks>
/// <param name="UnitTypeId">Key into <see cref="Ruleset.UnitTypes"/>, the type being recruited.</param>
/// <param name="Troops">Troops raised so far — the slot's own <see cref="RecruitmentSlot.Troops"/>.</param>
/// <param name="TargetCityId">The city training this regiment.</param>
/// <param name="StateCode">The raw readiness counter, shown by no screen but carried for tests/tools.</param>
/// <param name="IsReady">Whether this seat may mobilize the slot right now.</param>
/// <param name="WeeksUntilReady">
/// How many weekly ticks until <paramref name="IsReady"/> turns true, or <c>0</c> when it already has;
/// <see langword="null"/> when the ruleset's step cannot reach its own threshold (a zero step, or a
/// threshold above the state-code cap).
/// </param>
public sealed record TrainingRegimentView(
    string UnitTypeId,
    int Troops,
    string TargetCityId,
    int StateCode,
    bool IsReady,
    int? WeeksUntilReady)
{
    /// <summary>The words a panel shows for this regiment's readiness: <c>"ready"</c>, or
    /// <c>"4 weeks"</c>, or <c>"not reachable"</c> for the ruleset shape above.</summary>
    public string ReadinessText =>
        IsReady
            ? "ready"
            : WeeksUntilReady is { } weeks
                ? weeks == 1 ? "1 week" : $"{weeks} weeks"
                : "not reachable";
}

/// <summary>
/// What the army panel's "Mobilize first ready slot" button should do right now: the index of the first
/// recruitment slot this nation may actually mobilize, or a reason why no slot qualifies.
/// </summary>
/// <remarks>
/// <strong>Fix #513, Defect 2.</strong> The old button issued <c>mobilize 0 …</c> whatever slot 0's
/// state was, so an order given while slot 0 was still training was refused by
/// <c>MobilizeRecruitSlotCommandHandler</c> — and #484 made the refusal silent. This record is the
/// panel's own answer instead: a real index, or <see cref="IsEnabled"/> false with
/// <see cref="Reason"/> for the player to read.
/// </remarks>
/// <param name="SlotIndex">The index into <see cref="NationState.RecruitmentSlots"/> to pass to
/// <c>mobilize</c>, or <see langword="null"/> when none is ready.</param>
/// <param name="Reason">Why the choice is disabled, or <see langword="null"/> when it is enabled.</param>
public sealed record MobilizeChoice(int? SlotIndex, string? Reason)
{
    /// <summary>Whether the button should be clickable.</summary>
    public bool IsEnabled => SlotIndex.HasValue;

    /// <summary>The enabled choice: mobilize <paramref name="slotIndex"/>, the first ready slot.</summary>
    public static MobilizeChoice ForSlot(int slotIndex) => new(slotIndex, null);

    /// <summary>The disabled choice, with the words the panel shows beside the button.</summary>
    public static MobilizeChoice None(string reason) => new(null, reason);
}

/// <summary>
/// Fix #513's Godot-free view of a nation's training regiments — the model behind the city panel's
/// "In training here" list, the nation overview's "Regiments in training" list, and the army panel's
/// mobilize choice.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <strong>Godot-free</strong> (no <c>using Godot</c>), so it is exercised by a plain
/// xunit test in <c>tests/IC2.Engine.Tests/Ui/</c> — the same seam <c>godot/UI/RulesetPresets.cs</c>
/// already established, linked into that test project by one <c>&lt;Compile Include&gt;</c> line in
/// <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>.
/// </para>
/// <para>
/// <strong>Readiness is the engine's, not this class's.</strong> Both <see cref="TrainingRegimentView.IsReady"/>
/// and <see cref="MobilizeChoice"/> call <see cref="MobilizationReadiness.IsReady"/> with the nation's own
/// <see cref="NationState.Control"/> and the ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> — the same
/// gate <c>MobilizeRecruitSlotCommandHandler</c> applies, including the faithful AISeat threshold of 24.
/// <see cref="WeeksUntilReady"/> projects the engine's own <see cref="CityUnitStateCode.Advance"/> step
/// forward rather than dividing by it, so a ruleset with a modified step or cap still reads correctly.
/// </para>
/// </remarks>
public static class RecruitmentPanelViewModel
{
    /// <summary>The reason shown when the nation holds no recruitment slots at all.</summary>
    public const string NoSlotsReason = "No recruitment slot to mobilize.";

    /// <summary>The reason shown when the nation holds slots but none has reached its mobilization threshold.</summary>
    public const string NoReadySlotReason = "No recruitment slot is ready.";

    /// <summary>Every regiment this nation is training at <paramref name="cityId"/>, in slot order.</summary>
    /// <param name="state">The live state.</param>
    /// <param name="ruleset">Supplies the recruitment thresholds, the seat-asymmetry model and the calendar step.</param>
    /// <param name="nationId">The nation whose queue to read; a foreign city shows its own owner's queue.</param>
    /// <param name="cityId">The city whose training list to read.</param>
    public static IReadOnlyList<TrainingRegimentView> TrainingAtCity(
        GameState state, Ruleset ruleset, string nationId, string cityId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId);
        if (nation is null)
        {
            return Array.Empty<TrainingRegimentView>();
        }

        var rows = new List<TrainingRegimentView>(nation.RecruitmentSlots.Count);
        foreach (var slot in nation.RecruitmentSlots)
        {
            if (string.Equals(slot.TargetCityId, cityId, StringComparison.Ordinal))
            {
                rows.Add(Describe(slot, nation, ruleset));
            }
        }

        return rows;
    }

    /// <summary>Every regiment this nation is training, at every city, in slot order — the nation
    /// overview's list, and the order <see cref="MobilizeChoice.SlotIndex"/> indexes into.</summary>
    /// <param name="state">The live state.</param>
    /// <param name="ruleset">Supplies the recruitment thresholds, the seat-asymmetry model and the calendar step.</param>
    /// <param name="nationId">The nation whose queue to read.</param>
    public static IReadOnlyList<TrainingRegimentView> TrainingForNation(
        GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId);
        if (nation is null)
        {
            return Array.Empty<TrainingRegimentView>();
        }

        var rows = new List<TrainingRegimentView>(nation.RecruitmentSlots.Count);
        foreach (var slot in nation.RecruitmentSlots)
        {
            rows.Add(Describe(slot, nation, ruleset));
        }

        return rows;
    }

    /// <summary>
    /// The slot the army panel's mobilize button should name: the first slot in
    /// <see cref="NationState.RecruitmentSlots"/> whose state passes the engine's own readiness gate.
    /// </summary>
    /// <remarks>
    /// <strong>This scans rather than assuming index 0</strong> — Defect 2's fix. In a state reached by
    /// play the scan and index 0 select the same slot whenever anything is ready (slots are appended at
    /// <c>StateCode 0</c> and <see cref="RecruitmentSlotReadinessSystem"/> advances every slot in
    /// lockstep, so an earlier index is never less ready), but a table that arrived from elsewhere —
    /// an imported save's <c>SaveRecruitmentTable</c>, which keeps the DAT's own per-slot state codes in
    /// file order — need not be monotone. The scan is therefore the correct general rule, and the
    /// "none ready" answer is what stops the button issuing an order the handler will refuse.
    /// </remarks>
    /// <param name="state">The live state.</param>
    /// <param name="ruleset">Supplies the recruitment thresholds and the seat-asymmetry model.</param>
    /// <param name="nationId">The nation that would mobilize.</param>
    public static MobilizeChoice ChooseMobilization(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId);
        if (nation is null || nation.RecruitmentSlots.Count == 0)
        {
            return MobilizeChoice.None(NoSlotsReason);
        }

        for (var slotIndex = 0; slotIndex < nation.RecruitmentSlots.Count; slotIndex++)
        {
            if (IsReady(nation.RecruitmentSlots[slotIndex], nation, ruleset))
            {
                return MobilizeChoice.ForSlot(slotIndex);
            }
        }

        return MobilizeChoice.None(NoReadySlotReason);
    }

    private static TrainingRegimentView Describe(RecruitmentSlot slot, NationState nation, Ruleset ruleset) =>
        new(
            UnitTypeId: slot.UnitTypeId,
            Troops: slot.Troops,
            TargetCityId: slot.TargetCityId,
            StateCode: slot.StateCode,
            IsReady: IsReady(slot, nation, ruleset),
            WeeksUntilReady: WeeksUntilReady(slot.StateCode, nation, ruleset));

    private static bool IsReady(RecruitmentSlot slot, NationState nation, Ruleset ruleset) =>
        MobilizationReadiness.IsReady(
            slot.StateCode, nation.Control, ruleset.Recruitment, ruleset.Flags.SeatAsymmetry);

    private static int? WeeksUntilReady(int stateCode, NationState nation, Ruleset ruleset)
    {
        var recruitment = ruleset.Recruitment;
        var asymmetry = ruleset.Flags.SeatAsymmetry;
        if (MobilizationReadiness.IsReady(stateCode, nation.Control, recruitment, asymmetry))
        {
            return 0;
        }

        // A non-positive step could never move the counter, so the threshold above is unreachable; the
        // zero-step case is guarded before the loop so the loop's own progress argument (every pass
        // either advances by at least one or returns) always holds.
        if (ruleset.Calendar.CityUnitStateCodeStep <= 0)
        {
            return null;
        }

        var current = stateCode;
        for (var weeks = 1; ; weeks++)
        {
            var stepped = CityUnitStateCode.Advance(current, ruleset.Calendar);
            if (stepped == current)
            {
                // The cap is below this seat's threshold: the counter holds where it is, never arriving.
                return null;
            }

            if (MobilizationReadiness.IsReady(stepped, nation.Control, recruitment, asymmetry))
            {
                return weeks;
            }

            current = stepped;
        }
    }
}
