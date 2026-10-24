using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>One "How to play" section: its heading and its paragraphs.</summary>
public sealed record HowToPlaySection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// #1289 (F6 of the v0.6.2 play-through #1280): the Help page's "How to play" tab — the goal, a turn, moving,
/// attacking and sieges, the battle board, supply and money, diplomacy, and how a reign ends.
/// <strong>[designed]</strong> text in the project's own words, drafted from the v0.6.2 user manual
/// (<c>docs/investigations/v062-user-manual.md</c>); like the rest of <see cref="HelpPage"/> it never quotes or
/// paraphrases the original's help file (T100). Godot-free so the tests can link it.
/// </summary>
public static class HowToPlay
{
    /// <summary>
    /// The sections, with the victory line read from <paramref name="ruleset"/>'s own default condition when one
    /// is given (the main menu has no game yet, so it names both shipped presets' conditions).
    /// </summary>
    public static IReadOnlyList<HowToPlaySection> Sections(Ruleset? ruleset) => new[]
    {
        new HowToPlaySection("The goal", new[]
        {
            "You rule one nation of the ancient Mediterranean. Grow it by force and diplomacy, keep your people loyal, and outlast your rivals.",
            VictoryLine(ruleset),
        }),
        new HowToPlaySection("A turn", new[]
        {
            "A turn is two weeks. Give orders to your armies, fleets and cities in any order, then press End turn (the hourglass, or Game → End turn).",
            "The other nations then move. Reports of battles and offers open one at a time; close each one (Esc works) to start your next turn.",
        }),
        new HowToPlaySection("Moving", new[]
        {
            "Zoom the main map with the mouse wheel, including over the overview map. Drag to pan; hovering a marker names it. Ctrl+wheel over the details panel resizes its text.",
            "With a unit selected, left-click a tile to move there. Terrain costs moves: forest 2, mountains and rivers 4, rough sea 3, everything else 1. A unit stays selected while it has moves left.",
            "A click on empty ground always moves the selected unit. To avoid a mis-click marching an army away, turn on Settings → Map → \"Confirm moves with a second click\", or press Esc to drop the selection first.",
            "To carry an army by sea, move it onto your own fleet next to it; sail the fleet, then click a land tile beside it to land.",
        }),
        new HowToPlaySection("Attacking and sieges", new[]
        {
            "Move next to the target (diagonals count) with a move left, then click it: an army attacks an army or besieges a city, a fleet attacks a fleet.",
            "Attacking a nation you are not at war with asks first, and Yes declares war.",
            "A siege pits your army's strength against the city's loyalty, fortification and population; archers count extra. A failed siege still wears the city down, so you can try again next turn.",
        }),
        new HowToPlaySection("Field battles", new[]
        {
            "When your army fights an army, the battle board opens. The defender places units first, then the attacker, each in its own three rows.",
            "In your half-round, click a unit and then a cell to move, click an enemy to fight it in melee, or right-click an enemy in range to shoot (archers reach two cells; heavy units cannot shoot). Press the hourglass to end the half-round.",
            "A unit flees when too few troops or too little morale are left. The battle ends when one side has fled or surrendered. The Computer general button lets the computer fight for you; Settings → Battle speed sets how fast it plays.",
        }),
        new HowToPlaySection("Supply and money", new[]
        {
            "Armies eat supply every turn, more in winter; below 10% they lose a move. Refill with Supply army next to your own city (free) or a friendly one.",
            "Taxes fill the treasury once a season and pay your regular units and ships. High taxes cost unity, the people's loyalty to you; low unity or unpaid debts can cost you the throne.",
            "Recruit units and build fleets from the Strategy menu; Taxation and the Balance sheet show where the money goes.",
        }),
        new HowToPlaySection("Retreat and supply", new[]
        {
            "Select one of your armies or fleets, then press Retreat and supply below the command strip. The unit seeks stocked owned towns before each End turn; fleets can also repair at an owned coastal town.",
            "A nearby enemy army or fleet within ten tiles cancels the mode. It finishes when an army has full supplies, or a fleet has full supplies and condition, during the recovery pass before normal End-turn consumption.",
            "Suggest split opens an editable whole-unit split for an army; only your confirmation makes the split. The mode is saved with this campaign, but original-compatible SAV export leaves it out and reports that omission.",
        }),
        new HowToPlaySection("Diplomacy", new[]
        {
            "Strategy → International relations lists every nation. Declare War, Make Peace, Ally and Trade are there; a nation may refuse an offer, and a greyed button is not available now.",
            "Declaring war also puts you at war with the target's allies; the confirmation names them. Breaking a deal sours relations for a while.",
        }),
        new HowToPlaySection("How a reign ends", new[]
        {
            "You win as the goal above says. You lose if you lose your last city, if unity collapses and you are overthrown, or if you are deposed for not paying the army.",
            "Otherwise your reign ends when the scenario's time runs out. The End of Game window says why and compares your nation at the start and the end.",
            "Sitting still is not safe: the other nations will attack a passive ruler.",
        }),
    };

    /// <summary>The goal's victory line for <paramref name="ruleset"/>, or both presets' when there is none.</summary>
    public static string VictoryLine(Ruleset? ruleset) => ruleset?.Victory.DefaultCondition switch
    {
        VictoryConditionType.TotalConquest => "To win this game, hold every city on the map.",
        VictoryConditionType.Domination => "To win this game, hold every city of every nation still at war with you.",
        VictoryConditionType.ScoreAtTurnLimit => "To win this game, have the highest score when the turn limit is reached.",
        null => "To win, hold every city on the map (Classical Faithful), or every city of every nation still at war with you (Improved).",
        _ => "To win, meet this game's victory condition.",
    };
}
