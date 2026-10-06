using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T139.md</c> Done-when 4 and 5: the real <see cref="MainGameScreen"/> on scripted
/// classical states whose ruleset override makes the post-battle offer of peace certain, so the Offer of
/// peace window is shown, answered and Save is checked. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/PeaceOfferCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// The <c>classical-mediterranean</c> world with the <c>improved</c> ruleset (as
/// <c>AiPhaseBattleCheck</c>), the all-land pocket at (63,0)–(76,11), and the ruleset override
/// <c>PeaceTreatyOfferTests.OfferFixture</c> uses: <c>combat.autoPeaceChanceNumerator</c> equal to its
/// denominator and the two threshold gates relaxed. The army-strength gate (the winner's remaining armies are weaker than the
/// loser's) is met by scripting an extra, strong army for the side that must come out ahead.
/// </remarks>
public partial class PeaceOfferCheck : Node
{
    private const int PocketX = 67;
    private const int PocketY = 4;
    private const ulong Seed = 20261005UL;

    private bool _ok = true;
    private readonly List<string> _savedPaths = new();

    public override void _Ready()
    {
        try
        {
            CheckHumanWinsThenYes();
            CheckFullNewsLogThenYes();
            CheckHumanWinsThenNo();
            CheckAiPhaseOfferIsAnsweredAtTurnStart();
            CheckHotseatSaveAndOrder();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"PeaceOfferCheck: unhandled exception: {ex}");
            _ok = false;
        }

        foreach (var path in _savedPaths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"PeaceOfferCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    // ---- Done-when 4(a) ----

    private void CheckHumanWinsThenYes()
    {
        var (mainGame, issued) = Build(HumanWinsSession(), "t139-yes");
        var session = mainGame.Session;
        var ruleset = session.Ruleset;
        var cooldown = ruleset.Diplomacy.CooldownAfterEndedWar;

        mainGame.SubmitForCheck("attack-army army-0 army-2");
        Check(mainGame.ActiveOverlay is BattleResultScreen, "(a) the human's attack opens the battle window first");
        if (mainGame.ActiveOverlay is BattleResultScreen battle)
        {
            battle.Close();
        }

        var offer = session.PendingPeaceOfferFor("rome");
        Check(offer is not null, "(a) the engine holds an offer for rome after the attack");
        if (offer is null || mainGame.ActiveOverlay is not PeaceOfferScreen window)
        {
            Check(false, $"(a) after the battle window is closed a peace window opens (got {Describe(mainGame)})");
            return;
        }

        CheckWindowText(window, offer, "(a) ");
        Check(
            offer.Lines[0].StartsWith("After losing to you in battle", StringComparison.Ordinal),
            "(a) a human win uses the 'After losing to you' wording");

        // T147 (bug #781 point 3): the order that raised the offer leaves the offer's own lines and the
        // CLI prompt out of the output area -- the window is the way to answer.
        var afterAttack = mainGame.LastCommandText;
        Check(
            !afterAttack.Contains("peace-yes", StringComparison.Ordinal)
            && offer.Lines.All(dialogLine => !afterAttack.Contains(dialogLine, StringComparison.Ordinal)),
            $"(a) the output area shows neither the CLI prompt nor the offer's own lines (got '{afterAttack}')");

        var newsBefore = NewsTexts(session);
        var winner = session.State.NationById(offer.WinnerNationId)!.Name;
        var loser = session.State.NationById(offer.LoserNationId)!.Name;
        var line = $"{winner} and {loser} have agreed to end their war.";
        Check(!newsBefore.Contains(line), "(a) the agreed-war news line is not in the log before the answer");

        var countBefore = issued.Count;
        PressButton(window, "Yes");

        Check(issued.Count == countBefore + 1, $"(a) Yes is counted once at CommandIssued ({issued.Count - countBefore})");
        Check(issued.Count > 0 && issued[^1].Any(l => l.Contains("accepted", StringComparison.Ordinal)), "(a) the reply line shows the engine's acceptance");
        Check(mainGame.ActiveOverlay is null, $"(a) the window closes on the answer (got {Describe(mainGame)})");

        // T147 (bug #781 point 4): Yes shows a readable line for its command kind and the news the answer
        // added, never the raw result key.
        Check(
            mainGame.LastCommandText.Contains("You accepted the peace treaty.", StringComparison.Ordinal),
            $"(a) after Yes the label shows the readable wording (got '{mainGame.LastCommandText}')");
        Check(
            mainGame.LastCommandText.Contains(line, StringComparison.Ordinal),
            $"(a) after Yes the label shows the agreed-war news line (got '{mainGame.LastCommandText}')");
        Check(
            !mainGame.LastCommandText.Contains("diplomacy.accept-peace-treaty accepted.", StringComparison.Ordinal),
            $"(a) after Yes the label never shows the raw result key (got '{mainGame.LastCommandText}')");
        Check(
            session.State.Relations.Get("rome", "carthage") == cooldown
            && session.State.Relations.Get("carthage", "rome") == cooldown,
            $"(a) Yes leaves the relation at cooldownAfterEndedWar ({cooldown}) in both directions "
            + $"({session.State.Relations.Get("rome", "carthage")}, {session.State.Relations.Get("carthage", "rome")})");
        Check(NewsTexts(session).Contains(line), $"(a) the news log now holds '{line}'");
        Check(!session.HasPendingPeaceOffers, "(a) no offer is pending after Yes");

        CheckSaveWrites(mainGame, "(a) ");
        Dispose(mainGame);
    }

    /// <summary>
    /// Sol's review R1: the news log is a ring buffer, so once it is full its slot count stops growing
    /// and a count-based "the entries after the previous count" range finds none of the accepted
    /// order's news. Starting from a deliberately full log, the same attack/offer/Yes as (a) must still
    /// show the agreed-war news line below the readable wording.
    /// </summary>
    private void CheckFullNewsLogThenYes()
    {
        var (mainGame, _) = Build(HumanWinsSession(fullNewsLog: true), "t147-full");
        var session = mainGame.Session;
        var capacity = session.Ruleset.NewsLog.RingBufferSlots;

        Check(
            session.State.NewsLog.Slots.Count == capacity,
            $"(full log) the news log starts full ({session.State.NewsLog.Slots.Count} of {capacity} slots)");

        mainGame.SubmitForCheck("attack-army army-0 army-2");
        (mainGame.ActiveOverlay as BattleResultScreen)?.Close();

        var offer = session.PendingPeaceOfferFor("rome");
        if (offer is null || mainGame.ActiveOverlay is not PeaceOfferScreen window)
        {
            Check(false, $"(full log) a peace window opens (got {Describe(mainGame)})");
            return;
        }

        var winner = session.State.NationById(offer.WinnerNationId)!.Name;
        var loser = session.State.NationById(offer.LoserNationId)!.Name;
        var line = $"{winner} and {loser} have agreed to end their war.";

        PressButton(window, "Yes");

        Check(
            mainGame.LastCommandText.Contains("You accepted the peace treaty.", StringComparison.Ordinal),
            $"(full log) after Yes the label shows the readable wording (got '{mainGame.LastCommandText}')");
        Check(
            mainGame.LastCommandText.Contains(line, StringComparison.Ordinal),
            $"(full log) after Yes the label still shows the added news line through a full ring buffer "
            + $"(got '{mainGame.LastCommandText}')");
        Check(
            session.State.NewsLog.Slots.Count == capacity,
            $"(full log) the log stays full after the answer ({session.State.NewsLog.Slots.Count} slots)");

        Dispose(mainGame);
    }

    // ---- Done-when 4(b) ----

    private void CheckHumanWinsThenNo()
    {
        var (mainGame, issued) = Build(HumanWinsSession(), "t139-no");
        var session = mainGame.Session;
        var war = session.Ruleset.Diplomacy.StateCodes.War;

        mainGame.SubmitForCheck("attack-army army-0 army-2");
        (mainGame.ActiveOverlay as BattleResultScreen)?.Close();

        if (mainGame.ActiveOverlay is not PeaceOfferScreen window)
        {
            Check(false, $"(b) a peace window opens (got {Describe(mainGame)})");
            return;
        }

        var newsBefore = NewsTexts(session);
        var countBefore = issued.Count;
        PressButton(window, "No");

        Check(issued.Count == countBefore + 1, $"(b) No is counted once at CommandIssued ({issued.Count - countBefore})");
        Check(mainGame.ActiveOverlay is null, $"(b) the window closes on the answer (got {Describe(mainGame)})");
        Check(
            session.State.Relations.Get("rome", "carthage") == war
            && session.State.Relations.Get("carthage", "rome") == war,
            $"(b) No leaves the relation at war ({war}) in both directions");
        Check(NewsTexts(session).SequenceEqual(newsBefore), "(b) the news log is as it was before the answer");
        Check(!session.HasPendingPeaceOffers, "(b) no offer is pending after No");

        CheckSaveWrites(mainGame, "(b) ");
        Dispose(mainGame);
    }

    // ---- Done-when 4(c) ----

    private void CheckAiPhaseOfferIsAnsweredAtTurnStart()
    {
        var (mainGame, issued) = Build(AiAttacksSession(humanSeats: new[] { "rome" }), "t139-ai");
        var session = mainGame.Session;

        Check(!session.HasPendingPeaceOffers, "(c) no offer before the turn ends");
        mainGame.MenuBar.PressItemForCheck("game.end_turn");

        Check(
            mainGame.ActiveOverlay is BattleResultScreen,
            $"(c) End turn from the menu: the AI phase's battle window opens first (got {Describe(mainGame)})");
        while (mainGame.ActiveOverlay is BattleResultScreen more)
        {
            more.Close();
        }

        var offer = session.PendingPeaceOfferFor("rome");
        Check(offer is not null, "(c) the engine holds an offer for rome");
        if (offer is null || mainGame.ActiveOverlay is not PeaceOfferScreen window)
        {
            Check(false, $"(c) the peace window follows the battle window (got {Describe(mainGame)})");
            return;
        }

        CheckWindowText(window, offer, "(c) ");
        Check(
            offer.Lines[0].StartsWith("After defeating you in battle", StringComparison.Ordinal),
            "(c) a human loss uses the 'After defeating you' wording");

        var countBefore = issued.Count;
        PressButton(window, "Yes");
        Check(issued.Count == countBefore + 1, $"(c) the answer is counted once ({issued.Count - countBefore})");
        Check(!session.HasPendingPeaceOffers && mainGame.ActiveOverlay is null, "(c) answered: no offer, no overlay");
        Dispose(mainGame);
    }

    // ---- Done-when 5 ----

    private void CheckHotseatSaveAndOrder()
    {
        var (mainGame, issued) = Build(AiAttacksSession(humanSeats: new[] { "rome", "seleucid" }), "t139-hot");
        var session = mainGame.Session;

        mainGame.SubmitForCheck("end");
        Check(session.State.ActiveNationId == "seleucid", $"hotseat: seleucid is active (got {session.State.ActiveNationId})");
        Check(session.PendingPeaceOfferFor("rome") is not null, "hotseat: the engine holds an offer for rome");
        Check(session.PendingPeaceOfferFor("seleucid") is null, "hotseat: none for seleucid");
        Check(
            mainGame.ActiveOverlay is HotseatHandoffScreen { Info.NextNationId: "seleucid" },
            $"hotseat: the handoff to seleucid comes first (got {Describe(mainGame)})");
        (mainGame.ActiveOverlay as HotseatHandoffScreen)?.Continue();
        Check(mainGame.ActiveOverlay is null, $"hotseat: no window for seleucid (got {Describe(mainGame)})");

        var countBefore = issued.Count;
        mainGame.MenuBar.PressItemForCheck("file.save");
        var romeName = session.State.NationById("rome")!.Name;
        Check(
            mainGame.SaveConfirmationText == $"{romeName} must answer an offer of peace at its turn before the game can be saved.",
            $"hotseat: Save shows the designed message naming rome (got '{mainGame.SaveConfirmationText}')");
        Check(issued.Count == countBefore, "hotseat: Save issued no command");
        Check(mainGame.LastSavedPath is null, "hotseat: Save wrote no file");

        mainGame.SubmitForCheck("end");
        Check(
            mainGame.ActiveOverlay is HotseatHandoffScreen { Info.NextNationId: "rome" },
            $"hotseat: the handoff to rome follows seleucid's end (got {Describe(mainGame)})");
        (mainGame.ActiveOverlay as HotseatHandoffScreen)?.Continue();
        Check(mainGame.ActiveOverlay is BattleResultScreen, $"hotseat: rome's battle window comes first (got {Describe(mainGame)})");
        (mainGame.ActiveOverlay as BattleResultScreen)?.Close();
        Check(
            mainGame.ActiveOverlay is PeaceOfferScreen,
            $"hotseat: rome's peace window follows its battle windows (got {Describe(mainGame)})");
        if (mainGame.ActiveOverlay is PeaceOfferScreen hotWindow)
        {
            PressButton(hotWindow, "No");
        }

        Check(!session.HasPendingPeaceOffers, "hotseat: answered, nothing pending");
        Dispose(mainGame);
    }

    // ---- helpers ----

    private (MainGameScreen Screen, List<IReadOnlyList<string>> Issued) Build(GameSession session, string suffix)
    {
        var mainGame = new MainGameScreen
        {
            Session = session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
            CheckSaveSuffix = suffix,
        };
        var issued = new List<IReadOnlyList<string>>();
        mainGame.CommandIssued += lines => issued.Add(lines);
        AddChild(mainGame);
        return (mainGame, issued);
    }

    private void Dispose(MainGameScreen mainGame)
    {
        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private void CheckWindowText(PeaceOfferScreen window, PendingPeaceOffer offer, string prefix)
    {
        Check(window.TitleLabel.Text == "Offer of peace", $"{prefix}the window is titled 'Offer of peace' (got '{window.TitleLabel.Text}')");
        var shown = window.BodyLabels.Select(l => l.Text).ToArray();
        Check(
            shown.SequenceEqual(offer.Lines),
            $"{prefix}the window shows every engine line, in order ({shown.Length} of {offer.Lines.Count})");
        Check(
            window.Buttons.Select(b => b.Text).SequenceEqual(new[] { "Yes", "No" }),
            $"{prefix}the buttons are Yes and No");
        Check(window.IsInsideTree(), $"{prefix}the window is laid out in the tree");
    }

    private void CheckSaveWrites(MainGameScreen mainGame, string prefix)
    {
        mainGame.MenuBar.PressItemForCheck("file.save");
        var path = mainGame.LastSavedPath;
        if (path is not null)
        {
            _savedPaths.Add(path);
        }

        Check(
            mainGame.SaveConfirmationText.StartsWith("Saved to", StringComparison.Ordinal),
            $"{prefix}File -> Save then writes a save (got '{mainGame.SaveConfirmationText}')");
    }

    /// <summary>Fires the laid-out button's own <c>pressed</c> signal, as a click does (R1).</summary>
    private void PressButton(PeaceOfferScreen window, string label)
    {
        var button = window.Buttons.FirstOrDefault(b => b.Text == label);
        Check(button is not null, $"the window has a '{label}' button to press");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static string[] NewsTexts(GameSession session) =>
        session.State.NewsLog.Slots.Select(e => e.Text).ToArray();

    private static string Describe(MainGameScreen mainGame) => mainGame.ActiveOverlay?.GetType().Name ?? "null";

    // ---- fixtures ----

    private static (ResolvedScenarioHolder Resolved, Ruleset Ruleset) Load()
    {
        var resolved = GameDataContext.Repository.Resolve("example-classical-improved");
        var ruleset = resolved.Ruleset with
        {
            Combat = resolved.Ruleset.Combat with
            {
                AutoPeaceChanceNumerator = resolved.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };
        return (new ResolvedScenarioHolder(resolved.World, resolved.Scenario), ruleset);
    }

    /// <summary>Rome (human) beats a weak carthage army; carthage's power outside the fight exceeds rome's.</summary>
    /// <param name="fullNewsLog">
    /// Sol's review R1: when true the world starts with a completely full ring buffer, so the news
    /// additions of the accepted peace cannot be found by any count-based range.
    /// </param>
    private static GameSession HumanWinsSession(bool fullNewsLog = false)
    {
        var (resolved, ruleset) = Load();
        var war = ruleset.Diplomacy.StateCodes.War;
        var scenario = WithHumans(resolved.Scenario, "rome");
        var armies = resolved.World.StartingArmies
            .Select(a => a.Id switch
            {
                "army-0" => Army(a, PocketX, PocketY, 80, Units(20000, 6)),
                "army-2" => Army(a, PocketX + 1, PocketY, 40, Units(3000, 4)),
                _ => a,
            })
            .Append(Extra("check-carthage-1", "carthage", PocketX + 5, PocketY + 6))
            .Append(Extra("check-carthage-2", "carthage", PocketX + 6, PocketY + 6));
        var world = resolved.World with
        {
            StartingRelations = resolved.World.StartingRelations!.WithRelation("rome", "carthage", war),
            StartingArmies = ValueList.From(armies),
        };
        if (fullNewsLog)
        {
            world = world with { StartingNews = FullNewsLog(ruleset) };
        }

        return new GameSession(world, ruleset, scenario, seedOverride: Seed);
    }

    /// <summary>A ring buffer filled to its ruleset capacity with ordinary printable-ASCII lines.</summary>
    private static NewsLog FullNewsLog(Ruleset ruleset)
    {
        var log = NewsLog.Empty;
        for (var i = 0; i < ruleset.NewsLog.RingBufferSlots; i++)
        {
            log = log.Append(new NewsEntry($"Earlier report {i}"), ruleset.NewsLog);
        }

        return log;
    }

    /// <summary>Carthage's AI attacks rome's weak army; rome's power outside the fight exceeds carthage's.</summary>
    private static GameSession AiAttacksSession(string[] humanSeats)
    {
        var (resolved, ruleset) = Load();
        var war = ruleset.Diplomacy.StateCodes.War;
        var scenario = WithHumans(resolved.Scenario, humanSeats);
        var armies = resolved.World.StartingArmies
            .Select(a => a.Id switch
            {
                "army-0" => Army(a, PocketX, PocketY, 45, Units(5000, 4)),
                "army-2" => Army(a, PocketX + 1, PocketY, 60, Units(7000, 6)),
                _ => a,
            })
            .Append(Extra("check-rome-1", "rome", PocketX + 5, PocketY + 6))
            .Append(Extra("check-rome-2", "rome", PocketX + 6, PocketY + 6));
        var world = resolved.World with
        {
            StartingRelations = resolved.World.StartingRelations!.WithRelation("rome", "carthage", war),
            StartingArmies = ValueList.From(armies),
        };
        return new GameSession(world, ruleset, scenario, seedOverride: Seed);
    }

    private static Scenario WithHumans(Scenario scenario, params string[] humans) =>
        scenario with
        {
            Seats = ValueList.From(scenario.Seats.Select(seat => seat with
            {
                Control = humans.Contains(seat.Nation) ? SeatControl.Human : SeatControl.Ai,
            })),
        };

    private static ValueList<UnitSlot> Units(int troops, int quality) =>
        ValueList.Of(new UnitSlot(0, "light_infantry", Troops: troops, Quality: quality, Name: "Check Warband"));

    private static StartingArmy Army(StartingArmy army, int x, int y, int morale, ValueList<UnitSlot> units) =>
        army with { X = x, Y = y, Morale = morale, Units = units };

    private static StartingArmy Extra(string id, string nation, int x, int y) =>
        new(id, nation, X: x, Y: y, Morale: 60, Money: 0, SupplyTons: 0, Moves: 8, Units: Units(150000, 6));

    private sealed record ResolvedScenarioHolder(World World, Scenario Scenario);

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
