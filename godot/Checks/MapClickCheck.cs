using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T99's own headless check (Done-when 2–6): the real <see cref="MainGameScreen"/> on a new
/// classical-mediterranean game, driven through the real input path —
/// <see cref="GameMapView._GuiInput"/> with real <see cref="InputEventMouseButton"/> events at real
/// tile positions, and the screen's own <see cref="MainGameScreen._UnhandledInput"/> with real
/// <see cref="InputEventKey"/> events for the Esc and Shift+X keys — never a helper that skips it.
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/MapClickCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>The fixture (a scenario variant in code, the seam
/// <c>godot/Screens/Checks/ScreensCheck.cs</c> already establishes).</strong> Rome plays (the CLI's
/// <c>--seat rome</c> shape). <c>army-0</c> stays at its shipped (100,37) — the move-test army: the
/// column north of it is plain except one forest, so an 8-move click on (100,36) walks one tile and
/// leaves 7 moves, and a click on (100,29) spends every remaining move and ends at 0.
/// <c>army-1</c> moves to (98,37) beside Carthage's <c>army-2</c> at (99,37) — Rome and Carthage are
/// at peace, so that click must prompt. A third Rome army (<c>rome-check-army</c>, army-1's own shape)
/// stands at (99,39) beside Gaul's <c>army-9</c> at (99,38) — Rome and Gaul are at war, so that click
/// must not prompt. Bithynia's <c>army-11</c> stands at (100,40), three tiles from army-0 — beyond
/// attack reach, so a left click on it drops the selection and shows its (fogged) details, and a
/// right click opens its unit list without touching the selection. Rome's own city Arretium at
/// (99,36) is the Esc and Shift+X selection. Every tile was verified against the shipped terrain
/// grid before the fixture was written.
/// </para>
/// <para>
/// <strong>The carrying-fleet fixture (review round 1, B1).</strong> Two cargo armies are embarked on
/// their fleets by resuming a save that carries the link the engine's own (still unreachable, bug
/// #453) embark would build: Rome's <c>rome-carry-fleet</c> at (100,45) carries <c>rome-cargo-army</c>,
/// and Ptolemaic <c>ptolemy-carry-fleet</c> at (100,47) carries <c>ptolemy-cargo-army</c>. Rome's
/// <c>rome-attack-fleet</c> sits between them at (100,46). An embarked army has its fleet's own X/Y
/// but is not drawn there, so a click on a carrying fleet must resolve to the visible fleet, never the
/// hidden army: a left click selects the fleet, a right click lists the fleet's ships and the army
/// aboard, and a fleet-click on the Ptolemaic carrying fleet (Rome is at peace with Ptolemaic) asks
/// the original's fleet question. <see cref="GameMapView.FindArmyAt"/> skips embarked armies exactly
/// as <c>DrawArmy</c> does — the defect this fixture pins.
/// </para>
/// <para>
/// <strong>The "command count" (Done-when 3).</strong> <see cref="GameSession"/> keeps no running
/// command count (its public surface is <see cref="GameSession.World"/>, <see cref="GameSession.Ruleset"/>,
/// <see cref="GameSession.Scenario"/>, <see cref="GameSession.State"/>, <see cref="GameSession.Submit"/>
/// and <see cref="GameSession.LastBattles"/>), so this check counts at the one seam every command this
/// screen issues flows through: <see cref="GameMapView.CommandIssued"/> (and
/// <see cref="ContextPanel.CommandIssued"/>), which fires for <em>every</em> submission — accepted or
/// rejected, prompted or not. "No submits nothing" is asserted on that count and on the session's own
/// <see cref="GameState"/> staying the very same reference (no accepted command at all, so no
/// declaration of war either).
/// </para>
/// <para>
/// <strong>Every click and every assertion is its own plan step</strong> (review round 1, N3): the plan
/// alternates an interaction and its assertion, each step waited out with the same settle-frame pattern
/// <c>godot/Checks/CommandFeedbackCheck.cs</c> and <c>godot/Checks/MapClipCheck.cs</c> use. A click runs
/// in one <c>_Process</c> call and its panel assertion runs <em>after</em> the layout pass, so the
/// assertions read the laid-out labels (T96's hazard: <c>.Text</c> is not what the player sees),
/// asserting <see cref="Label.GetVisibleLineCount"/> on every label they read. The "unchanged" counts
/// are asserted as deltas against the value captured immediately before the interaction (N4), never as
/// an absolute count that a previous step already satisfied.
/// </para>
/// </remarks>
public partial class MapClickCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 3;

    private const string DeclareWarLine = "diplomacy.declare-war accepted (composed ahead of the attack).";
    private const string AttackPromptQuestion = "Are you sure you want to attack this army ?";
    private const string FleetAttackPromptQuestion = "Are you sure you want to attack this fleet ?";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private GameMapView _map = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    /// <summary>The ordered plan: each step waits its own frames, then runs once.</summary>
    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    /// <summary>The session's command count, at the one seam every submission this screen issues flows through.</summary>
    private int _commandsSubmitted;

    private int _attackConfirmationsRequested;
    private int _selectionsCleared;
    private int _citiesSelected;
    private int _armySelectedCount;
    private int _fleetSelectedCount;
    private IReadOnlyList<string> _lastCommandLines = Array.Empty<string>();

    /// <summary>The prompt the last "opens" assertion saw, for the following "press" step.</summary>
    private ConfirmPrompt? _activePrompt;

    // The before-values the delta assertions read (review round 1, N4): captured in the interaction
    // step, asserted in the following step.
    private int _commandsBefore;
    private int _clearedBefore;
    private int _citiesBefore;
    private int _armiesBefore;
    private int _fleetsBefore;
    private int _confirmationsBefore;
    private GameState? _stateBeforeNo;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen
        {
            Session = _session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _map = _mainGame.MapView;
        _map.CommandIssued += lines =>
        {
            _commandsSubmitted++;
            _lastCommandLines = lines;
        };
        _mainGame.ContextPanel.CommandIssued += lines =>
        {
            _commandsSubmitted++;
            _lastCommandLines = lines;
        };
        _map.AttackConfirmationRequested += _ => _attackConfirmationsRequested++;
        _map.SelectionCleared += () => _selectionsCleared++;
        _map.CitySelected += _ => _citiesSelected++;
        _map.ArmySelected += _ => _armySelectedCount++;
        _map.FleetSelected += _ => _fleetSelectedCount++;

        BuildPlan();
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            if (_planIndex >= _plan.Count)
            {
                return;
            }

            var (waitFrames, run) = _plan[_planIndex];
            if (_frame < waitFrames)
            {
                return;
            }

            run();
            _planIndex++;
            _frame = 0;

            if (_planIndex >= _plan.Count)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MapClickCheck: unhandled exception: {ex}");
            GD.Print("MapClickCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// The plan, in Done-when order: each interaction is followed by its own assertion step, so every
    /// panel read happens after the layout pass that follows the click (N3).
    /// </summary>
    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            // Done-when 2 (selection) and Done-when 5 (the own half of the fog).
            (InitialSettleFrames, ClickOwnArmy),
            (BetweenStepsFrames, AssertOwnArmyPanelShowsAllFourNumbers),

            // Done-when 2 (the right-click rule).
            (BetweenStepsFrames, ClickRightOnForeignArmyForUnitList),
            (BetweenStepsFrames, AssertUnitListShownAndNothingChanged),

            // Done-when 5's foreign half.
            (BetweenStepsFrames, ClickForeignArmyBeyondReach),
            (BetweenStepsFrames, AssertForeignArmyFoggedPanel),

            // Done-when 2's move half: a move that leaves moves >= 1 keeps the selection.
            (BetweenStepsFrames, ClickSelectArmyForMove),
            (BetweenStepsFrames, AssertArmySelectedForMove),
            (BetweenStepsFrames, ClickNearLandTile),
            (BetweenStepsFrames, AssertMoveInReachKeepsSelection),

            // Done-when 2's second half: a move that spends every move ends the selection.
            (BetweenStepsFrames, ClickFarLandTile),
            (BetweenStepsFrames, AssertMoveSpendsEveryMove),

            // Done-when 3's first half: the prompt opens, and No submits nothing.
            (BetweenStepsFrames, ClickSelectArmy1),
            (BetweenStepsFrames, AssertArmy1Selected),
            (BetweenStepsFrames, ClickAdjacentForeignArmyForPrompt),
            (BetweenStepsFrames, AssertPromptOpened),
            (BetweenStepsFrames, ClickNoOnPrompt),
            (BetweenStepsFrames, AssertNoSubmitsNothing),

            // Done-when 3's second half: Yes submits, with one composed declaration of war.
            (BetweenStepsFrames, ClickSelectArmy1Again),
            (BetweenStepsFrames, AssertArmy1Reselected),
            (BetweenStepsFrames, ClickAdjacentForeignArmyAgain),
            (BetweenStepsFrames, AssertSameClickPromptsAgain),
            (BetweenStepsFrames, ClickYesOnPrompt),
            (BetweenStepsFrames, AssertYesSubmitsAttackAndCloses),

            // Done-when 6, first key: Esc.
            (BetweenStepsFrames, AssertNoOverlayOpen),
            (BetweenStepsFrames, ClickCityForEsc),
            (BetweenStepsFrames, AssertCitySelectedForEsc),
            (BetweenStepsFrames, ClickEsc),
            (BetweenStepsFrames, AssertEscClearedSelection),

            // Done-when 6, second key: Shift+X.
            (BetweenStepsFrames, ClickCityForShiftX),
            (BetweenStepsFrames, AssertCitySelectedForShiftX),
            (BetweenStepsFrames, ClickShiftX),
            (BetweenStepsFrames, AssertShiftXClearedSelection),

            // Done-when 4: at war, the same click submits the attack with no prompt at all.
            (BetweenStepsFrames, ClickThirdArmy),
            (BetweenStepsFrames, AssertThirdArmySelected),
            (BetweenStepsFrames, ClickGaulArmyAtWar),
            (BetweenStepsFrames, AssertAtWarAttackNoPrompt),

            // B1: a carrying fleet's click resolves to the fleet, not the hidden army aboard.
            (BetweenStepsFrames, ClickOwnCarryingFleet),
            (BetweenStepsFrames, AssertOwnCarryingFleetSelected),
            (BetweenStepsFrames, ClickRightOnOwnCarryingFleet),
            (BetweenStepsFrames, AssertCarryingFleetUnitListShowsTheArmyAboard),

            // B1: a peace-time click on a foreign carrying fleet prompts for the fleet.
            (BetweenStepsFrames, ClickOwnAttackFleet),
            (BetweenStepsFrames, AssertAttackFleetSelected),
            (BetweenStepsFrames, ClickForeignCarryingFleet),
            (BetweenStepsFrames, AssertFleetAttackPrompt),
            (BetweenStepsFrames, ClickNoOnFleetPrompt),
            (BetweenStepsFrames, AssertFleetPromptNoSubmitsNothing),
        });
    }

    // ---- Done-when 2 (selection) and Done-when 5 (the own half of the fog) ----

    private void ClickOwnArmy() => LeftClick(100, 37);

    private void AssertOwnArmyPanelShowsAllFourNumbers()
    {
        var army = _session.State.ArmyById("army-0");
        Check(army is not null, "army-0 stands at (100,37) in the fixture");
        Check(ArmyIsSelected("army-0"), "a left click on an own army selects it");

        CheckPanelShowsHeading("Army — army-0");
        var stats = PanelStatsLine();
        Check(stats is not null && StatsLineIsLaidOut(stats), "the own army's stats line is laid out after layout frames");
        Check(
            stats is not null
            && FieldShowsANumber(stats.Text, "Moves")
            && FieldShowsANumber(stats.Text, "Morale")
            && FieldShowsANumber(stats.Text, "Money")
            && FieldShowsANumber(stats.Text, "Supply"),
            "the own army's panel shows a number for moves, morale, money and supply "
            + $"(got '{stats?.Text}')");
    }

    // ---- Done-when 2 (the right-click rule) ----

    private void ClickRightOnForeignArmyForUnitList()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;
        _armiesBefore = _armySelectedCount;
        _fleetsBefore = _fleetSelectedCount;

        RightClick(100, 40);
    }

    private void AssertUnitListShownAndNothingChanged()
    {
        CheckPanelShowsHeading("Units — army-11");
        Check(
            _commandsSubmitted == _commandsBefore,
            $"a right click submits nothing (command count {_commandsBefore} -> {_commandsSubmitted})");
        Check(
            _selectionsCleared == _clearedBefore && _armySelectedCount == _armiesBefore
            && _fleetSelectedCount == _fleetsBefore,
            "a right click on a foreign army fires no selection event at all, so the selection "
            + "is unchanged (still army-0)");
    }

    // ---- Done-when 5's foreign half ----

    private void ClickForeignArmyBeyondReach()
    {
        _clearedBefore = _selectionsCleared;
        LeftClick(100, 40);
    }

    private void AssertForeignArmyFoggedPanel()
    {
        Check(
            _selectionsCleared == _clearedBefore + 1,
            "a left click on a foreign army beyond attack reach drops the selection");
        CheckPanelShowsHeading("Army — army-11");
        var stats = PanelStatsLine();
        Check(stats is not null && StatsLineIsLaidOut(stats), "the foreign army's stats line is laid out after layout frames");
        Check(
            stats is not null && stats.Text.Contains("withheld", StringComparison.Ordinal)
            && !stats.Text.Any(char.IsDigit),
            "the foreign army's panel shows no number for moves, morale, money or supply "
            + $"(got '{stats?.Text}')");
    }

    // ---- Done-when 2's move half ----

    private void ClickSelectArmyForMove() => LeftClick(100, 37);

    private void AssertArmySelectedForMove() => Check(ArmyIsSelected("army-0"), "army-0 is selected for the move");

    private void ClickNearLandTile()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;

        LeftClick(100, 36);
    }

    private void AssertMoveInReachKeepsSelection()
    {
        var army = _session.State.ArmyById("army-0");
        Check(
            _commandsSubmitted == _commandsBefore + 1,
            $"the land-tile click submits one command ({_commandsBefore} -> {_commandsSubmitted})");
        Check(
            _lastCommandLines.Contains("> move army-0 100 36", StringComparer.Ordinal),
            $"the submitted command is the composed move (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            army is { X: 100, Y: 36 },
            $"the army's position changed to the clicked tile (got ({army?.X}, {army?.Y}))");
        Check(army is { Moves: 7 }, $"the walk left the army 7 moves (got {army?.Moves})");
        Check(
            _selectionsCleared == _clearedBefore,
            "the army is still selected after a move that leaves moves >= 1 (no SelectionCleared)");
        CheckPanelShowsHeading("Army — army-0");
    }

    private void ClickFarLandTile()
    {
        _clearedBefore = _selectionsCleared;
        LeftClick(100, 29);
    }

    private void AssertMoveSpendsEveryMove()
    {
        var army = _session.State.ArmyById("army-0");
        Check(
            _lastCommandLines.Contains("> move army-0 100 29", StringComparer.Ordinal),
            $"the far tile click submits the composed move (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            army is { Moves: 0 },
            $"the walk spent every remaining move (got {army?.Moves})");
        Check(
            _selectionsCleared == _clearedBefore + 1,
            "the army is not selected after a move that leaves 0 (SelectionCleared fired)");
        CheckPanelShowsHeading("Nation Overview");
    }

    // ---- Done-when 3's first half: the prompt opens, and No submits nothing ----

    private void ClickSelectArmy1() => LeftClick(98, 37);

    private void AssertArmy1Selected() => Check(ArmyIsSelected("army-1"), "army-1 is selected for the attack");

    private void ClickAdjacentForeignArmyForPrompt()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;
        _stateBeforeNo = _session.State;

        LeftClick(99, 37);
    }

    private void AssertPromptOpened()
    {
        _activePrompt = _mainGame.ActiveOverlay as ConfirmPrompt;
        Check(
            _activePrompt is not null,
            $"an attack on an adjacent foreign army of a nation at peace opens the prompt "
            + $"(got {_mainGame.ActiveOverlay?.GetType().Name ?? "no overlay"})");
        Check(
            _activePrompt?.Question == AttackPromptQuestion,
            $"the prompt asks the original's own question with its own spacing (got '{_activePrompt?.Question}')");
    }

    private void ClickNoOnPrompt() => PressButton(FindButton(_activePrompt!, "No")!);

    private void AssertNoSubmitsNothing()
    {
        Check(
            _commandsSubmitted == _commandsBefore,
            $"No submits nothing (command count {_commandsBefore} -> {_commandsSubmitted})");
        Check(
            ReferenceEquals(_session.State, _stateBeforeNo),
            "No leaves the session's state the very same reference, so no command ran at all");
        Check(
            _mainGame.ActiveOverlay is null,
            "No closes the prompt");
        Check(
            _selectionsCleared > _clearedBefore && ArmyIsNotSelected(),
            "No drops the selection");
    }

    // ---- Done-when 3's second half: Yes submits, one declaration of war ----

    private void ClickSelectArmy1Again() => LeftClick(98, 37);

    private void AssertArmy1Reselected() => Check(ArmyIsSelected("army-1"), "army-1 is re-selected for the attack");

    private void ClickAdjacentForeignArmyAgain()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;

        LeftClick(99, 37);
    }

    private void AssertSameClickPromptsAgain()
    {
        _activePrompt = _mainGame.ActiveOverlay as ConfirmPrompt;
        Check(_activePrompt is not null, "the same click prompts again");
    }

    private void ClickYesOnPrompt() => PressButton(FindButton(_activePrompt!, "Yes")!);

    private void AssertYesSubmitsAttackAndCloses()
    {
        Check(
            _commandsSubmitted == _commandsBefore + 1,
            $"Yes submits one command ({_commandsBefore} -> {_commandsSubmitted})");
        Check(
            _lastCommandLines.Contains("> attack-army army-1 army-2", StringComparer.Ordinal),
            $"the submitted command is the composed attack (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            _lastCommandLines.Count(line => string.Equals(line, DeclareWarLine, StringComparison.Ordinal)) == 1,
            $"the session's output shows the composed declaration of war exactly once "
            + $"(got {_lastCommandLines.Count(line => string.Equals(line, DeclareWarLine, StringComparison.Ordinal))})");
        Check(
            _mainGame.ActiveOverlay is not ConfirmPrompt,
            "Yes closes the prompt");
        Check(
            _selectionsCleared > _clearedBefore,
            "the selection ends after the attack");

        // The attack resolves a battle, whose result screen is modal over the map; close it so the
        // later clicks are a real player's clicks on an interactive map.
        CloseAnyOverlay();
    }

    // ---- Done-when 6, first key: Esc ----

    private void AssertNoOverlayOpen() =>
        Check(
            _mainGame.ActiveOverlay is null,
            "no overlay is open when the key is pressed, so the key belongs to the map, not a modal");

    private void ClickCityForEsc()
    {
        _citiesBefore = _citiesSelected;
        LeftClick(99, 36);
    }

    private void AssertCitySelectedForEsc() =>
        Check(
            _citiesSelected > _citiesBefore && ArmyIsNotSelected(),
            "a left click on Rome's own city selects it");

    private void ClickEsc()
    {
        _clearedBefore = _selectionsCleared;
        PressKey(Key.Escape);
    }

    private void AssertEscClearedSelection()
    {
        Check(
            _selectionsCleared == _clearedBefore + 1,
            "Esc clears the selection (GameMapView.SelectionCleared fired)");
        Check(ArmyIsNotSelected() && CityIsNotSelected(), "no selection remains after Esc");
    }

    // ---- Done-when 6, second key: Shift+X ----

    private void ClickCityForShiftX()
    {
        _citiesBefore = _citiesSelected;
        LeftClick(99, 36);
    }

    private void AssertCitySelectedForShiftX() =>
        Check(
            _citiesSelected > _citiesBefore && CityIsSelected("Arretium"),
            "the city is selected again for Shift+X");

    private void ClickShiftX()
    {
        _clearedBefore = _selectionsCleared;
        PressKey(Key.X, shift: true);
    }

    private void AssertShiftXClearedSelection() =>
        Check(
            _selectionsCleared == _clearedBefore + 1,
            "Shift+X clears the selection (GameMapView.SelectionCleared fired)");

    // ---- Done-when 4: at war, the same click submits with no prompt ----

    private void ClickThirdArmy() => LeftClick(99, 39);

    private void AssertThirdArmySelected() =>
        Check(ArmyIsSelected("rome-check-army"), "the third Rome army is selected for the war attack");

    private void ClickGaulArmyAtWar()
    {
        _commandsBefore = _commandsSubmitted;
        _confirmationsBefore = _attackConfirmationsRequested;

        LeftClick(99, 38);
    }

    private void AssertAtWarAttackNoPrompt()
    {
        Check(
            _attackConfirmationsRequested == _confirmationsBefore,
            "an attack on a nation already at war raises no prompt "
            + $"({_confirmationsBefore} -> {_attackConfirmationsRequested})");
        Check(
            _mainGame.ActiveOverlay is not ConfirmPrompt,
            "no confirmation prompt opened");
        Check(
            _commandsSubmitted == _commandsBefore + 1,
            $"the click submitted the attack directly ({_commandsBefore} -> {_commandsSubmitted})");
        Check(
            _lastCommandLines.Contains("> attack-army rome-check-army army-9", StringComparer.Ordinal),
            $"the submitted command is the composed attack (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            !_lastCommandLines.Contains(DeclareWarLine, StringComparer.Ordinal),
            "the output composes no declaration of war against a nation already at war");

        // This attack resolves its own battle result screen; close it before the carrying-fleet clicks.
        CloseAnyOverlay();
    }

    // ---- B1: a carrying fleet's click resolves to the fleet, not the hidden army aboard ----

    private void ClickOwnCarryingFleet()
    {
        _armiesBefore = _armySelectedCount;
        _fleetsBefore = _fleetSelectedCount;

        LeftClick(100, 45);
    }

    private void AssertOwnCarryingFleetSelected()
    {
        Check(
            _fleetSelectedCount > _fleetsBefore,
            "a left click on the carrying fleet fires FleetSelected");
        Check(
            _armySelectedCount == _armiesBefore,
            "the embarked army is not selected in the fleet's place");
        CheckPanelShowsHeading("Fleet — rome-carry-fleet");
    }

    private void ClickRightOnOwnCarryingFleet()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;
        _armiesBefore = _armySelectedCount;
        _fleetsBefore = _fleetSelectedCount;

        RightClick(100, 45);
    }

    private void AssertCarryingFleetUnitListShowsTheArmyAboard()
    {
        CheckPanelShowsHeading("Units — rome-carry-fleet");
        var aboard = FindPanelLabel(label => label.Text.StartsWith("The army aboard", StringComparison.Ordinal));
        Check(
            aboard is { Visible: true } && aboard.GetVisibleLineCount() >= 1 && aboard.Size.Y > 1f,
            "the unit list shows the army aboard, laid out after layout frames");
        Check(
            _commandsSubmitted == _commandsBefore,
            "the right click on the carrying fleet submits nothing");
        Check(
            _selectionsCleared == _clearedBefore && _armySelectedCount == _armiesBefore
            && _fleetSelectedCount == _fleetsBefore,
            "the right click on the carrying fleet changes no selection");
    }

    private void ClickOwnAttackFleet()
    {
        _fleetsBefore = _fleetSelectedCount;
        LeftClick(100, 46);
    }

    private void AssertAttackFleetSelected()
    {
        Check(_fleetSelectedCount > _fleetsBefore, "a left click selects the own attack fleet");
        CheckPanelShowsHeading("Fleet — rome-attack-fleet");
    }

    private void ClickForeignCarryingFleet()
    {
        _commandsBefore = _commandsSubmitted;
        _clearedBefore = _selectionsCleared;
        _confirmationsBefore = _attackConfirmationsRequested;

        LeftClick(100, 47);
    }

    private void AssertFleetAttackPrompt()
    {
        _activePrompt = _mainGame.ActiveOverlay as ConfirmPrompt;
        Check(
            _activePrompt is not null,
            "a peace-time click on a foreign carrying fleet opens the prompt");
        Check(
            _activePrompt?.Question == FleetAttackPromptQuestion,
            $"the prompt names the fleet with the original's own spacing (got '{_activePrompt?.Question}')");
        Check(
            _attackConfirmationsRequested == _confirmationsBefore + 1,
            "the fleet attack raises one confirmation request");
    }

    private void ClickNoOnFleetPrompt() => PressButton(FindButton(_activePrompt!, "No")!);

    private void AssertFleetPromptNoSubmitsNothing()
    {
        Check(
            _commandsSubmitted == _commandsBefore,
            "No on the fleet prompt submits nothing (no declaration of war)");
        Check(_mainGame.ActiveOverlay is null, "No closes the fleet prompt");
        Check(
            _selectionsCleared > _clearedBefore && ArmyIsNotSelected(),
            "No drops the selection");
        CheckPanelShowsHeading("Nation Overview");
    }

    // ---- the drivers: the real input path, never a shortcut around it ----

    /// <summary>
    /// Drives the map's real input path with real mouse events at real positions: the press arms the
    /// left button's drag state and the release resolves the click, exactly as Godot delivers it; the
    /// right button has no press state to arm (its release alone is the click).
    /// </summary>
    private void LeftClick(int x, int y) => Click(x, y, MouseButton.Left);

    private void RightClick(int x, int y) => Click(x, y, MouseButton.Right);

    private void Click(int x, int y, MouseButton button)
    {
        var position = _map.TileCenterForCheck(x, y);
        if (button == MouseButton.Left)
        {
            _map._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = button,
                Pressed = true,
                Position = position,
            });
        }

        _map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = false,
            Position = position,
        });
    }

    /// <summary>
    /// Drives the screen's real key handler with a real key event. The fully real path —
    /// <see cref="Input.ParseInputEvent"/> into the engine's own pipeline — is a no-op under
    /// <c>--headless</c> (the headless display server drops the event before the tree ever sees it,
    /// which is exactly what this check's first run showed), so the check calls the handler the
    /// viewport itself would call, <see cref="MainGameScreen._UnhandledInput"/>, with the same
    /// <see cref="InputEventKey"/> the pipeline would deliver — the same real-handler-with-real-event
    /// convention the mouse clicks above use for <see cref="GameMapView._GuiInput"/>.
    /// </summary>
    private void PressKey(Key keycode, bool shift = false)
    {
        _mainGame._UnhandledInput(new InputEventKey
        {
            Keycode = keycode,
            Pressed = true,
            ShiftPressed = shift,
        });
    }

    /// <summary>Presses a real button the way a click does — the same signal Godot's own press emits.</summary>
    private static void PressButton(Button button) =>
        button.EmitSignal(Button.SignalName.Pressed);

    /// <summary>Closes the open overlay's own Close button, if it has one (a battle result screen does).</summary>
    private void CloseAnyOverlay()
    {
        if (_mainGame.ActiveOverlay is { } overlay && FindButton(overlay, "Close") is { } close)
        {
            PressButton(close);
        }
    }

    // ---- the panel and selection observables ----

    /// <summary>Selection is observable only through the panel the map's events drive — T96's own lesson.</summary>
    private bool ArmyIsSelected(string armyId) => PanelShowsHeading($"Army — {armyId}");

    private bool ArmyIsNotSelected() => !PanelHeadingStartsWith("Army —");

    private bool CityIsSelected(string cityName) => PanelShowsHeading($"City — {cityName}");

    private bool CityIsNotSelected() => !PanelHeadingStartsWith("City —");

    private bool PanelShowsHeading(string text) =>
        FindPanelLabel(label => label.Text == text) is { Visible: true };

    private bool PanelHeadingStartsWith(string prefix) =>
        FindPanelLabel(label => label.Text.StartsWith(prefix, StringComparison.Ordinal)) is { Visible: true };

    private void CheckPanelShowsHeading(string text) =>
        Check(PanelShowsHeading(text), $"the panel shows its '{text}' view after layout frames");

    /// <summary>The army panel's stats line — the one label the fog rule is about.</summary>
    private Label? PanelStatsLine() =>
        FindPanelLabel(label => label.Text.StartsWith("Moves:", StringComparison.Ordinal));

    /// <summary>T96's hazard — <c>.Text</c> is not what the player sees: the line must be laid out too.</summary>
    private static bool StatsLineIsLaidOut(Label label) =>
        label.GetVisibleLineCount() >= 1 && label.Size.Y > 1f;

    /// <summary>Whether the stats line shows a number right after the named field (the fog's own test).</summary>
    private static bool FieldShowsANumber(string text, string field)
    {
        var at = text.IndexOf($"{field}: ", StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var numberAt = at + field.Length + 2;
        return numberAt < text.Length && char.IsDigit(text[numberAt]);
    }

    private Label? FindPanelLabel(Func<Label, bool> predicate) =>
        FindLabel(_mainGame.ContextPanel, predicate);

    private static Label? FindLabel(Node root, Func<Label, bool> predicate)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Label label && predicate(label))
            {
                return label;
            }

            if (FindLabel(child, predicate) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static Button? FindButton(Node root, string text)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button && string.Equals(button.Text, text, StringComparison.Ordinal))
            {
                return button;
            }

            if (FindButton(child, text) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    // ---- the finish ----

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"MapClickCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private bool Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"PASS: {what}");
            return true;
        }

        GD.PrintErr($"FAIL: {what}");
        _ok = false;
        return false;
    }

    /// <summary>
    /// The fixture (see this class's own remarks): the shipped classical pair with rome as the human
    /// seat, five armies repositioned or added, three fleets added, and two armies embarked on their
    /// fleets by resuming a save that carries the link — the same <c>scenario variant in code</c> seam
    /// <c>godot/Screens/Checks/ScreensCheck.cs</c> establishes.
    /// </summary>
    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        var repositioned = resolved.World.StartingArmies.Select(a => a.Id switch
        {
            // Rome's second army beside Carthage's army — the at-peace prompt pair.
            "army-1" => a with { X = 98, Y = 37 },
            // Carthage's army beside army-1.
            "army-2" => a with { X = 99, Y = 37 },
            // Gaul's army beside the third Rome army — the at-war pair.
            "army-9" => a with { X = 99, Y = 38 },
            // Bithynia's army beyond army-0's attack reach — the unit-list and fog army.
            "army-11" => a with { X = 100, Y = 40 },
            _ => a,
        }).ToList();

        // The third Rome army for the at-war attack (army-1 is spent on the at-peace pair by then):
        // army-1's own shape under a new id, beside Gaul's army.
        repositioned.Add(resolved.World.StartingArmies.Single(a => a.Id == "army-1") with
        {
            Id = "rome-check-army",
            X = 99,
            Y = 39,
        });

        // B1's cargo armies: army-1's shape for Rome's own carrying fleet, army-2's for Ptolemaic's.
        repositioned.Add(resolved.World.StartingArmies.Single(a => a.Id == "army-1") with
        {
            Id = "rome-cargo-army",
            X = 100,
            Y = 45,
        });
        repositioned.Add(resolved.World.StartingArmies.Single(a => a.Id == "army-2") with
        {
            Id = "ptolemy-cargo-army",
            Nation = "ptolemaic",
            X = 100,
            Y = 47,
        });

        var fleets = resolved.World.StartingFleets.ToList();
        fleets.Add(new StartingFleet("rome-carry-fleet", "rome", 100, 45, Ships: 3, ConditionPercent: 100, Money: 0, SupplyTons: 0, Moves: 25));
        fleets.Add(new StartingFleet("rome-attack-fleet", "rome", 100, 46, Ships: 3, ConditionPercent: 100, Money: 0, SupplyTons: 0, Moves: 25));
        fleets.Add(new StartingFleet("ptolemy-carry-fleet", "ptolemaic", 100, 47, Ships: 3, ConditionPercent: 100, Money: 0, SupplyTons: 0, Moves: 25));

        var world = resolved.World with
        {
            StartingArmies = ValueList.From(repositioned),
            StartingFleets = ValueList.From(fleets),
        };

        var initial = new GameSession(
            world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");

        // Embark the two cargo armies. The engine's own embark is unreachable in play (bug #453), so
        // the fixture sets the link directly and resumes the session from it — the seam a loaded save
        // gives a real player. CoveredTileCode is null exactly while AboardFleetId is set, as
        // GameDataValidation requires, and the fleet's position is pinned here so an AI turn before
        // Rome's can never move the marker out from under the click.
        var state = initial.State;
        var armies = state.Armies.Select(a => a.Id switch
        {
            "rome-cargo-army" => a with { AboardFleetId = "rome-carry-fleet", CoveredTileCode = null, X = 100, Y = 45 },
            "ptolemy-cargo-army" => a with { AboardFleetId = "ptolemy-carry-fleet", CoveredTileCode = null, X = 100, Y = 47 },
            _ => a,
        }).ToList();
        var stateFleets = state.Fleets.Select(f => f.Id switch
        {
            "rome-carry-fleet" => f with { CarriedArmyId = "rome-cargo-army", X = 100, Y = 45 },
            "rome-attack-fleet" => f with { X = 100, Y = 46 },
            "ptolemy-carry-fleet" => f with { CarriedArmyId = "ptolemy-cargo-army", X = 100, Y = 47 },
            _ => f,
        }).ToList();
        var embarked = state with
        {
            Armies = ValueList.From(armies),
            Fleets = ValueList.From(stateFleets),
        };

        var save = new SaveGame(
            state.SchemaVersion,
            "map-click-check",
            "map-click-check",
            state.ScenarioId,
            state.WorldId,
            state.RulesetId,
            embarked);

        return new GameSession(world, resolved.Ruleset, resolved.Scenario, save);
    }
}
