using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T25.md</c> Done-when: "a headless run opens each of the three screens from a scripted
/// state and exits 0," widened 2026-09-28: "a scripted headless run of the main game screen reaches each
/// screen through play: an attack shows the battle result for that battle, the diplomacy control opens
/// the grid, and ending a human seat's turn in a two-human hotseat game shows the handoff." Run headless
/// via <c>Godot_..._console.exe --headless --path godot res://Screens/Checks/ScreensCheck.tscn
/// --quit-after 4</c> (see this task's own PR body for the exact captured command and output).
/// </summary>
/// <remarks>
/// <para>
/// The first four checks satisfy the original Done-when directly: each of the three screens is
/// constructed from a scripted fixture/state and added to the tree (Godot headless still builds a real
/// <see cref="Control"/> tree and runs real layout code; only pixel readback needs a window — see
/// <c>godot/Checks/ScreenshotTour.cs</c>'s own remarks). The last three drive the real
/// <see cref="MainGameScreen"/> through <see cref="MainGameScreen.SubmitForCheck"/> and
/// <see cref="MainGameScreen.OpenDiplomacyScreen"/> — the same entry points a real map click, a real
/// "Diplomacy" button press, or a real "End Turn" press reach — and assert on
/// <see cref="MainGameScreen.ActiveOverlay"/>, never a hand-rolled mirror of that wiring.
/// </para>
/// <para>
/// <strong>Never two Godot processes at once</strong> (this task's own binding instruction) — this check
/// and <c>godot/Checks/**</c>'s own checks are run one at a time, never concurrently.
/// </para>
/// </remarks>
public partial class ScreensCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            CheckBattleResultScreenOpensFromAScriptedDestroyedFixture();
            CheckBattleResultScreenOpensFromAScriptedScatteredFixture();
            CheckDiplomacyScreenOpensFromAScriptedState();
            CheckHotseatHandoffScreenOpensBlindAndNotBlindFromAScriptedState();
            CheckAttackThroughMainGameScreenOpensTheBattleResultScreen();
            CheckDiplomacyControlOpensTheGridThroughMainGameScreen();
            CheckEndingATurnInTwoHumanHotseatShowsTheHandoff();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ScreensCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"ScreensCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    // ---- original Done-when: each screen opens from a scripted state ----

    private void CheckBattleResultScreenOpensFromAScriptedDestroyedFixture()
    {
        var screen = new BattleResultScreen
        {
            Session = ToySession(),
            Result = BattleResultViewModel.FromResult(MakeBattleResult(LoserFate.Destroyed)),
        };
        AddChild(screen);

        Check(
            screen.FateLabel.Text.Contains("Destroyed", StringComparison.Ordinal),
            $"the battle-result screen shows 'Destroyed' for a destroyed-loser fixture (got '{screen.FateLabel.Text}')");

        screen.Close();
        RemoveChild(screen);
        screen.QueueFree();
    }

    private void CheckBattleResultScreenOpensFromAScriptedScatteredFixture()
    {
        var screen = new BattleResultScreen
        {
            Session = ToySession(),
            Result = BattleResultViewModel.FromResult(MakeBattleResult(LoserFate.Scattered)),
        };
        AddChild(screen);

        Check(
            screen.FateLabel.Text.Contains("Scattered", StringComparison.Ordinal),
            $"the battle-result screen shows 'Scattered' for a scattered-loser fixture (got '{screen.FateLabel.Text}')");

        screen.Close();
        RemoveChild(screen);
        screen.QueueFree();
    }

    private void CheckDiplomacyScreenOpensFromAScriptedState()
    {
        var screen = new DiplomacyScreen { Session = ToySession() };
        AddChild(screen);

        // The header row alone is 6 cells (Nation/Relation/War/Peace/Ally/Trade); the toy scenario's one
        // other nation ("south") adds a second row of 6 -- 12 or more proves at least one nation row
        // rendered, not only the header.
        Check(
            screen.Grid.GetChildCount() >= 12,
            $"the diplomacy grid renders a header row plus at least one nation row (found {screen.Grid.GetChildCount()} cells)");

        screen.Close();
        RemoveChild(screen);
        screen.QueueFree();
    }

    private void CheckHotseatHandoffScreenOpensBlindAndNotBlindFromAScriptedState()
    {
        var notBlind = new HotseatHandoffScreen { Info = new HotseatHandoffInfo("south", "Southern Realm", Blind: false) };
        AddChild(notBlind);
        Check(notBlind.BlindNoticeLabel is null, "the non-blind handoff shows no blind notice");
        Check(
            notBlind.MessageLabel.Text.Contains("Southern Realm", StringComparison.Ordinal),
            $"the handoff names the incoming nation (got '{notBlind.MessageLabel.Text}')");
        notBlind.Continue();
        RemoveChild(notBlind);
        notBlind.QueueFree();

        var blind = new HotseatHandoffScreen { Info = new HotseatHandoffInfo("south", "Southern Realm", Blind: true) };
        AddChild(blind);
        Check(blind.BlindNoticeLabel is not null, "the blind handoff shows the blind notice -- the toggle is read from the scenario");
        blind.Continue();
        RemoveChild(blind);
        blind.QueueFree();
    }

    // ---- widened Done-when: each screen reached through real play ----

    private void CheckAttackThroughMainGameScreenOpensTheBattleResultScreen()
    {
        var mainGame = new MainGameScreen { Session = BattleReadySession(), RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(mainGame);

        mainGame.SubmitForCheck("attack-army north-army-1 south-army-1");

        var opened = mainGame.ActiveOverlay is BattleResultScreen;
        Check(
            opened,
            $"an attack through MainGameScreen opens the battle-result screen "
            + $"(got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");

        if (mainGame.ActiveOverlay is BattleResultScreen battleScreen)
        {
            Check(
                battleScreen.Result.AttackerId == "north-army-1" && battleScreen.Result.DefenderId == "south-army-1",
                "the opened battle-result screen carries the battle this attack actually resolved");
            battleScreen.Close();
        }

        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private void CheckDiplomacyControlOpensTheGridThroughMainGameScreen()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var mainGame = new MainGameScreen
        {
            Session = new GameSession(toy.World, toy.Ruleset, toy.Scenario),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        AddChild(mainGame);

        mainGame.OpenDiplomacyScreen();

        Check(
            mainGame.ActiveOverlay is DiplomacyScreen,
            $"the Diplomacy control opens the diplomacy grid (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");

        if (mainGame.ActiveOverlay is DiplomacyScreen diplomacyScreen)
        {
            diplomacyScreen.Close();
        }

        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private void CheckEndingATurnInTwoHumanHotseatShowsTheHandoff()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");

        // docs/tasks/T25.md: "For the two-human DoD run, build the scenario variant in code, a copy of a
        // shipped scenario with two human seats" -- toy-3city's own "south" (shipped AI) switched to
        // human, everything else (world, ruleset, the human "north" seat) untouched.
        var twoHumanScenario = toy.Scenario with
        {
            Seats = ValueList.From(toy.Scenario.Seats.Select(s =>
                s.Nation == "south" ? s with { Control = SeatControl.Human } : s)),
        };

        var mainGame = new MainGameScreen
        {
            Session = new GameSession(toy.World, toy.Ruleset, twoHumanScenario),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        AddChild(mainGame);

        mainGame.SubmitForCheck("end");

        var opened = mainGame.ActiveOverlay is HotseatHandoffScreen;
        Check(
            opened,
            $"ending north's turn in a two-human hotseat game shows the handoff "
            + $"(got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");

        if (mainGame.ActiveOverlay is HotseatHandoffScreen handoff)
        {
            Check(
                handoff.Info.NextNationId == "south",
                $"the handoff names the incoming seat 'south' (got '{handoff.Info.NextNationId}')");
            handoff.Continue();
        }

        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    // ---- fixtures ----

    private static GameSession ToySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        return new GameSession(toy.World, toy.Ruleset, toy.Scenario);
    }

    /// <summary>
    /// The shipped toy world, with <c>south-army-1</c> moved from its shipped (4,4) to (4,2) -- one tile
    /// from <c>north-army-1</c>'s own (3,2) -- so <c>attack-army</c>'s adjacency gate passes without an
    /// extra move. The same repositioning
    /// <c>tests/IC2.Engine.Tests/Presentation/GameSessionBattleResultsTests.cs</c>'s own fixture uses
    /// (itself following <c>PeaceTreatyOfferTests.OfferFixture</c>'s lead) -- duplicated here rather than
    /// shared, since <c>godot/IC2.MapViewer.csproj</c> cannot reference the xunit test assembly.
    /// </summary>
    private static GameSession BattleReadySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };

        return new GameSession(customWorld, toy.Ruleset, toy.Scenario);
    }

    private static BattleResult MakeBattleResult(LoserFate fate) => new(
        Kind: BattleKind.Field,
        AttackerId: "north-army-1",
        DefenderId: "south-army-1",
        AttackerNationId: "north",
        DefenderNationId: "south",
        AttackerPower: 120,
        DefenderPower: 80,
        Winner: BattleSide.Attacker,
        AppliedDefeatOutcome: fate == LoserFate.Destroyed ? DefeatOutcome.Destroyed : DefeatOutcome.Scatter,
        LoserFate: fate,
        WinnerCasualties: 12,
        LoserCasualties: 80,
        UnitCasualties: ValueList<UnitCasualty>.Empty,
        Promotions: ValueList<UnitPromotion>.Empty,
        AbsorbedMoney: 250,
        AbsorbedSupplyTons: 10,
        WinnerUnityDelta: 5,
        LoserUnityDelta: -10,
        WinnerShipsLost: 0,
        WinnerConditionLost: 0,
        WinnerUnitsLost: 0,
        PeaceTreatyFired: false,
        PeaceTreatyOffered: false,
        Scatter: fate == LoserFate.Scattered
            ? new ScatterOutcome(FromX: 4, FromY: 2, ToX: 5, ToY: 3, RequestedDistance: 2, ActualDistance: 2)
            : null);

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
