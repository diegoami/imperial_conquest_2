using IC2.Engine.Model;

namespace IC2.Slice.Screens;

/// <summary>
/// One row of <see cref="DiplomacyScreen"/>'s grid — <c>docs/tasks/T25.md</c>: "the original's
/// peace/trade/ally/war grid per nation ... reused as-is." One other nation, its current relation to the
/// active seat, and which of the four actions that relation still allows.
/// </summary>
/// <param name="NationId">The other nation's id — what the grid's own buttons submit as the command's target.</param>
/// <param name="NationName">Its display name.</param>
/// <param name="RelationLabel">The current relation, already worded for display (never a bare numeric code).</param>
/// <param name="CanDeclareWar">Whether "Declare War" should be enabled — not already at war.</param>
/// <param name="CanMakePeace">Whether "Make Peace" should be enabled — currently at war.</param>
/// <param name="CanProposeAlliance">Whether "Propose Alliance" should be enabled — at peace or trade, not already allied.</param>
/// <param name="CanProposeTrade">Whether "Propose Trade" should be enabled — at peace, not already trading.</param>
public sealed record DiplomacyRelationRow(
    string NationId,
    string NationName,
    string RelationLabel,
    bool CanDeclareWar,
    bool CanMakePeace,
    bool CanProposeAlliance,
    bool CanProposeTrade);

/// <summary>
/// Builds <see cref="DiplomacyScreen"/>'s grid rows from the live <see cref="GameState"/> — Godot-free (no
/// <c>using Godot</c> in this file) so <c>tests/IC2.Engine.Tests/Ui/Screens/DiplomacyGridViewModelTests.cs</c>
/// can exercise the row logic directly, against <c>CoreTestbed</c>'s own toy scenario, without a Godot
/// runtime. Never mutates <see cref="GameState"/> or issues any command itself — <see cref="DiplomacyScreen"/>
/// is what turns a row's own buttons into a real <c>GameSession.Submit</c> call, exactly as
/// <c>docs/tasks/T25.md</c>'s own Owns note requires ("issues orders only through Submit ... Never mutate
/// GameState from the UI").
/// </summary>
public static class DiplomacyGridViewModel
{
    /// <summary>
    /// One row per other, non-eliminated nation, in <see cref="GameState.Nations"/>'s own list order —
    /// never re-sorted, the same "list order, not re-sorted" convention every other T24/T25 view builder
    /// in this codebase already follows (see <c>ContextPanel.BuildCityPanel</c>'s own army picker, for one).
    /// </summary>
    public static IReadOnlyList<DiplomacyRelationRow> BuildRows(GameState state, Ruleset ruleset, string activeNationId)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        var rows = new List<DiplomacyRelationRow>();

        foreach (var nation in state.Nations)
        {
            if (string.Equals(nation.Id, activeNationId, StringComparison.Ordinal) || nation.Eliminated)
            {
                continue;
            }

            if (state.Relations.IndexOf(activeNationId) < 0 || state.Relations.IndexOf(nation.Id) < 0)
            {
                continue;
            }

            var value = state.Relations.Get(activeNationId, nation.Id);
            var isWar = value == codes.War;
            var isAlliance = value == codes.Alliance;
            var isTrade = value == codes.Trade;

            rows.Add(new DiplomacyRelationRow(
                nation.Id,
                nation.Name,
                RelationLabel(value, codes),
                CanDeclareWar: !isWar,
                CanMakePeace: isWar,
                CanProposeAlliance: !isWar && !isAlliance,
                CanProposeTrade: !isWar && !isTrade));
        }

        return rows;
    }

    /// <summary>
    /// Words a raw relation cell for display. A negative value is a cooldown counter
    /// (<see cref="Model.DiplomaticRelations"/>'s own remarks), not a fourth state, so it is worded as one
    /// rather than matched against <paramref name="codes"/> at all.
    /// </summary>
    private static string RelationLabel(int value, RelationStateCodes codes)
    {
        if (value < 0)
        {
            return $"Cooldown ({-value})";
        }

        if (value == codes.War)
        {
            return "War";
        }

        if (value == codes.Alliance)
        {
            return "Alliance";
        }

        if (value == codes.Trade)
        {
            return "Trade";
        }

        if (value == codes.Peace)
        {
            return "Peace";
        }

        return $"Unknown ({value})";
    }
}
