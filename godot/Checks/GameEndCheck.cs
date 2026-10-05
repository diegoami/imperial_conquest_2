using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T138.md</c> Done-when 6: the real <see cref="MainGameScreen"/> on scripted states, so the
/// game-end windows land on the right seat at the right moment — after the call's battle windows and before
/// a hotseat hand-off. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/GameEndCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>a — holding every city.</strong> <c>toy-3city</c> with Meridia moved to <c>north</c>, so the
/// human seat owns all three cities at construction; pressing Game → End turn flushes that prelude fall
/// and shows the window. <strong>b — conquered in the AI phase.</strong> <c>SeatCliTests</c>' own conquest
/// fixture (south human, north's overwhelming army) — the siege's battle window opens first, then the
/// window naming the captor. <strong>c — hotseat.</strong> The same conquest with a third, human
/// <c>east</c> seat after the AI: north's turn takes south's last city, and south's window with Continue
/// comes before the hand-off to east, in that order.
/// </para>
/// </remarks>
public partial class GameEndCheck : Node
{
    private const ulong Seed = 2UL;

    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            CheckHoldingEveryCity();
            CheckConqueredInTheAiPhase();
            CheckHotseatFallThenHandoff();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameEndCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"GameEndCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    /// <summary>
    /// Done-when 6(a): a human seat holding every city at its next turn start sees the "End of Game"
    /// window with the conquest text and the state's own start/end table; its Main menu raises the screen's
    /// return-to-menu request.
    /// </summary>
    private void CheckHoldingEveryCity()
    {
        var mainGame = new MainGameScreen
        {
            Session = AllCitiesSession(),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        AddChild(mainGame);

        try
        {
            // Game -> End turn, from the menu, exactly as a player clicks it.
            Check(
                mainGame.CommandTable.TryInvoke("game.end_turn"),
                "every city: the Game -> End turn menu item is bound");

            var fall = mainGame.Session.LastSeatFalls.SingleOrDefault();
            Check(fall is not null, "every city: the seat's fall is on the session's LastSeatFalls");
            Check(
                mainGame.ActiveOverlay is GameEndScreen,
                $"every city: the window opens (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");

            if (mainGame.ActiveOverlay is GameEndScreen screen && fall is not null)
            {
                Check(
                    screen.TitleLabel.Text == "End of Game",
                    $"every city: the title is 'End of Game' (got '{screen.TitleLabel.Text}')");
                Check(
                    screen.BodyLabel.Text == "You have conquerred the Mediterranean, a unique achievement.",
                    $"every city: the body is the engine's conquest text (got '{screen.BodyLabel.Text}')");

                var nation = mainGame.Session.State.NationById("north")!;
                var expected = new[]
                {
                    nation.Name, "Start", "End",
                    "Population", nation.PopulationAtStart.ToString(), fall.EndPopulation.ToString(),
                    "Cities", nation.CityCountAtStart.ToString(), fall.EndCityCount.ToString(),
                    "Money", nation.TreasuryAtStart.ToString(), fall.EndTreasury.ToString(),
                };
                Check(
                    screen.TableCells().SequenceEqual(expected),
                    $"every city: the laid-out table is {string.Join(" | ", screen.TableCells())}");

                var requested = false;
                screen.MainMenuRequested += () => requested = true;
                screen.MainMenu();
                Check(requested, "every city: Main menu raises the screen's return-to-main-menu request");
            }
        }
        finally
        {
            RemoveChild(mainGame);
            mainGame.QueueFree();
        }
    }

    /// <summary>
    /// Done-when 6(b): a human seat whose last city falls in the AI phase sees the window naming the
    /// captor, after the battle window of that same <c>end</c>.
    /// </summary>
    private void CheckConqueredInTheAiPhase()
    {
        var mainGame = new MainGameScreen
        {
            Session = HotseatConquestSession(withEast: false),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        AddChild(mainGame);

        try
        {
            mainGame.SubmitForCheck("end"); // south ends; north's AI turn besieges Meridia and takes it.

            Check(
                mainGame.ActiveOverlay is BattleResultScreen,
                $"conquered: the siege's battle window opens first (got "
                + $"{mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
            if (mainGame.ActiveOverlay is BattleResultScreen battle)
            {
                battle.Close();
            }

            Check(
                mainGame.ActiveOverlay is GameEndScreen,
                $"conquered: the game-end window follows the battle (got "
                + $"{mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
            if (mainGame.ActiveOverlay is GameEndScreen screen)
            {
                Check(
                    screen.BodyLabel.Text == "Your nation has been conquerred by Northern League (north).",
                    $"conquered: the body names the captor (got '{screen.BodyLabel.Text}')");
                Check(
                    screen.GameOverLabel is not null && screen.GameOverLabel.Text == "The game is over.",
                    "conquered: the last human seat's fall reads 'The game is over.'");
                Check(
                    screen.Model.Buttons.SequenceEqual(new[] { GameEndButton.MainMenu, GameEndButton.ViewMap }),
                    "conquered: the last seat gets Main menu and View map");
            }
        }
        finally
        {
            RemoveChild(mainGame);
            mainGame.QueueFree();
        }
    }

    /// <summary>
    /// Done-when 6(c): with two human seats, the outgoing seat's fall shows its window with Continue, then
    /// the hand-off screen for the other seat, in that order.
    /// </summary>
    private void CheckHotseatFallThenHandoff()
    {
        var mainGame = new MainGameScreen
        {
            Session = HotseatConquestSession(withEast: true),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        AddChild(mainGame);

        try
        {
            mainGame.SubmitForCheck("end"); // south ends; north's AI turn takes Meridia and south falls.

            var fall = mainGame.Session.LastSeatFalls.SingleOrDefault();
            Check(
                fall is not null && fall.Reason == SeatFallReason.Conquered,
                "hotseat: the outgoing human seat is conquered in the AI phase");

            // T138 scope item (4): the call's battle windows come before the fall window.
            while (mainGame.ActiveOverlay is BattleResultScreen battleWindow)
            {
                battleWindow.Close();
            }

            Check(
                mainGame.ActiveOverlay is GameEndScreen,
                $"hotseat: the fall window opens before the hand-off (got "
                + $"{mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
            if (mainGame.ActiveOverlay is GameEndScreen screen)
            {
                Check(
                    screen.Model.Buttons.SequenceEqual(new[] { GameEndButton.Continue }),
                    "hotseat: the fall window offers Continue alone while another human seat remains");
                screen.Continue();
            }

            Check(
                mainGame.ActiveOverlay is HotseatHandoffScreen { Info.NextNationId: "east" },
                $"hotseat: the hand-off screen for the other seat follows (got "
                + $"{mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        }
        finally
        {
            RemoveChild(mainGame);
            mainGame.QueueFree();
        }
    }

    // ---- fixtures ----

    /// <summary>
    /// <c>toy-3city</c> with Meridia moved to <c>north</c>, so the human seat owns every city. At
    /// construction <c>AdvanceToHumanSeat</c> runs the turn-start check and the seat falls; the first
    /// command flushes it.
    /// </summary>
    private static GameSession AllCitiesSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var world = toy.World with
        {
            Cities = ValueList.From(toy.World.Cities.Select(c => c with { Owner = "north" })),
        };
        return new GameSession(world, toy.Ruleset, toy.Scenario);
    }

    /// <summary>
    /// <c>SeatCliTests.A_hotseat_conquest_of_the_last_human_seat_reaches_game_over</c>'s own fixture:
    /// south is the only human seat, north's overwhelming army takes Meridia. With
    /// <paramref name="withEast"/> a third, human <c>east</c> nation follows north in turn order, so the
    /// fall hands off to it instead of ending the game.
    /// </summary>
    private static GameSession HotseatConquestSession(bool withEast)
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");

        var nations = new List<NationDefinition>
        {
            toy.World.NationById("north")! with { Treasury = 0, Wealth = 40_000 },
            toy.World.NationById("south")! with { Treasury = 0 },
        };
        var turnOrder = new List<string> { "south", "north" };
        var seats = new List<Seat>
        {
            new("south", SeatControl.Human),
            new(
                "north", SeatControl.Ai,
                new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5)),
        };

        if (withEast)
        {
            nations.Add(toy.World.NationById("south")! with
            {
                Id = "east",
                Name = "Eastern League",
                ColorHex = "#3366cc",
                CapitalCityId = null,
            });
            turnOrder.Add("east");
            seats.Add(new Seat("east", SeatControl.Human));
        }

        var world = toy.World with
        {
            Nations = ValueList.From(nations),
            // Without a city of its own a nation is born eliminated and skipped by the seat rotation, so
            // east takes Portus; north keeps only Arx and south only Meridia.
            Cities = withEast
                ? ValueList.From(toy.World.Cities.Select(c =>
                    string.Equals(c.Id, "portus", StringComparison.Ordinal) ? c with { Owner = "east" } : c))
                : toy.World.Cities,
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 4, Y: 4, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 8, Units: ValueList.Of(new UnitSlot(0, "archers", Troops: 400_000, Quality: 6, Name: "Check Archers")))),
            TurnOrder = ValueList.From(turnOrder),
            StartingRelations = WarRelations(toy, nations),
        };
        var scenario = toy.Scenario with { Seats = ValueList.From(seats) };
        return new GameSession(world, toy.Ruleset, scenario, seedOverride: Seed);
    }

    /// <summary>
    /// The three-nation fixture's relation matrix: peace everywhere except north and south, so the AI's
    /// siege needs no declaration-of-war draw and the check stays deterministic.
    /// </summary>
    private static DiplomaticRelations WarRelations(
        ResolvedScenario toy, IReadOnlyList<NationDefinition> nations)
    {
        var ids = ValueList.From(nations.Select(n => n.Id));
        return DiplomaticRelations.Uniform(ids, toy.Ruleset.Diplomacy.StateCodes.Peace)
            .WithRelation("north", "south", toy.Ruleset.Diplomacy.StateCodes.War);
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
