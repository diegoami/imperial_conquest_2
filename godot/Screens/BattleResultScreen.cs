using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/game-design.md</c> §"User interface" item 3: "the battle result summary (a real payoff moment,
/// worth a dedicated screen)". Presents the confirmed aggregate the instant resolver actually produced —
/// both power values, the winner, the loser's fate (whichever <see cref="BattleResultViewModel.LoserFate"/>
/// actually reports, never a hardcoded "destroyed" string), <em>both</em> sides' casualties, promotions,
/// absorbed money and supplies, the unity swing, and whether the automatic peace fired.
/// </summary>
/// <remarks>
/// <para>
/// Reads only <see cref="BattleResultViewModel"/> — never <see cref="BattleResult"/> directly — so a
/// future alternate battle presentation (<c>docs/game-design.md</c>'s own "reserved, not built now" seam)
/// can read the same view model, or a different one built the same way, without this screen or
/// <see cref="InstantBattleResolver"/> having to change. <see cref="Session"/> is read only to resolve
/// nation ids to display names; nothing here mutates <see cref="GameSession.State"/>.
/// </para>
/// <para>
/// <strong>bug #499's additions, all siege-relevant.</strong> The winning side's "Gaul's casualties: 0"
/// was the whole casualty report, so the besieging army's own attrition (the loser's casualties, 1,872
/// troops in the reproduced Felsina siege) is now shown beside it; a siege also shows the besieged
/// city's before/after loyalty, fortification and population — each attempt wears the city down — and a
/// siege the attacker lost says how far short the attack fell. The strings themselves live on
/// <see cref="BattleResultViewModel"/> so the Godot-free test reads exactly what this screen renders.
/// </para>
/// </remarks>
public partial class BattleResultScreen : Control
{
    public required GameSession Session { get; init; }

    public required BattleResultViewModel Result { get; init; }

    /// <summary>Raised when the player dismisses this screen.</summary>
    public event Action? Closed;

    /// <summary>Exposed for <c>godot/Screens/Checks/**</c> to assert on the rendered text without re-deriving it.</summary>
    public Label FateLabel { get; private set; } = null!;

    /// <summary>Exposed for the same reason as <see cref="FateLabel"/>.</summary>
    public Label WinnerLabel { get; private set; } = null!;

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, new Color(0f, 0f, 0f, 0.65f));
        AddChild(root);

        var attackerName = DisplayNation(Result.AttackerNationId);
        var defenderName = DisplayNation(Result.DefenderNationId);
        var winnerName = DisplayNation(Result.WinnerNationId);
        var loserName = DisplayNation(Result.LoserNationId);

        content.AddChild(UiKit.MakeLabel("Battle Result", 22, UiKit.AccentColor));
        content.AddChild(UiKit.MakeLabel(
            $"{attackerName} ({Result.AttackerPower}) vs {defenderName} ({Result.DefenderPower})", 15, UiKit.TextColor));

        WinnerLabel = UiKit.MakeLabel($"Winner: {winnerName}", 17, UiKit.TextColor);
        content.AddChild(WinnerLabel);

        FateLabel = UiKit.MakeLabel($"{loserName}'s fate: {FateText(Result.LoserFate)}", 15, UiKit.TextColor);
        content.AddChild(FateLabel);

        if (Result.FailedSiegeShortfallText() is { } shortfall)
        {
            content.AddChild(UiKit.MakeLabel(shortfall, 15, UiKit.TextColor));
        }

        content.AddChild(new HSeparator());
        content.AddChild(UiKit.MakeLabel(Result.WinnerCasualtiesText(winnerName), 14, UiKit.MutedTextColor));
        content.AddChild(UiKit.MakeLabel(Result.LoserCasualtiesText(loserName), 14, UiKit.MutedTextColor));

        if (Result.SiegeCityText(CityName(Result.DefenderId)) is { } siegeCity)
        {
            content.AddChild(UiKit.MakeLabel(siegeCity, 14, UiKit.MutedTextColor));
        }

        content.AddChild(UiKit.MakeLabel(
            Result.Promotions.Count == 0
                ? "No promotions."
                : $"Promotions: {Result.Promotions.Count} unit(s).",
            14, UiKit.MutedTextColor));
        content.AddChild(UiKit.MakeLabel(
            $"Absorbed: {Result.AbsorbedMoney} money, {Result.AbsorbedSupplyTons}t supply", 14, UiKit.MutedTextColor));
        content.AddChild(UiKit.MakeLabel(
            $"Unity swing: {winnerName} {SignedInt(Result.WinnerUnityDelta)}, {loserName} {SignedInt(Result.LoserUnityDelta)}",
            14, UiKit.MutedTextColor));
        content.AddChild(UiKit.MakeLabel(PeaceLineText(), 13, UiKit.MutedTextColor));

        content.AddChild(new HSeparator());
        content.AddChild(UiKit.MakeButton("Close", Close));
    }

    /// <summary>Dismisses this screen — what the "Close" button does, exposed under its own name so a
    /// headless check can call it directly, the same convention <c>RulesetChooserScreen.ConfirmSelection</c>
    /// and <c>ScenarioSeatScreen.ConfirmSeatAndStart</c> already use rather than simulating a click.</summary>
    public void Close() => Closed?.Invoke();

    private string DisplayNation(string nationId) => Session.State.NationById(nationId)?.Name ?? nationId;

    /// <summary>
    /// The besieged city's display name for <see cref="BattleResultViewModel.SiegeCityText"/> — a siege's
    /// <see cref="BattleResultViewModel.DefenderId"/> is the city, and the id is the honest fallback if a
    /// save ever carries a city this state no longer knows.
    /// </summary>
    private string CityName(string cityId) => Session.State.CityById(cityId)?.Name ?? cityId;

    private string PeaceLineText() =>
        Result.PeaceTreatyFired
            ? "An automatic peace treaty followed this battle."
            : Result.PeaceTreatyOffered
                ? "A peace treaty has been offered."
                : "No peace treaty followed.";

    private static string SignedInt(int value) => value > 0 ? $"+{value}" : value.ToString();

    /// <summary>
    /// <c>docs/tasks/T25.md</c>: "present whichever BattleResult actually reports, not a hardcoded
    /// 'destroyed' string" — this is the one place that wording happens, off <see cref="LoserFate"/>
    /// itself, never a second guess at what the resolver decided.
    /// </summary>
    private static string FateText(LoserFate fate) => fate switch
    {
        LoserFate.Destroyed => "Destroyed",
        LoserFate.Scattered => "Scattered",
        LoserFate.Unaffected => "Unaffected (siege)",
        _ => fate.ToString(),
    };
}
