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
/// The two settle-frame waits and the per-step waits are the same pattern
/// <c>godot/Checks/CommandFeedbackCheck.cs</c> and <c>godot/Checks/MapClipCheck.cs</c> use; the
/// panel assertions run after layout frames and read the laid-out labels (T96's hazard:
/// <c>.Text</c> is not what the player sees), asserting <see cref="Label.GetVisibleLineCount"/> on
/// every label they read.
/// </para>
/// </remarks>
public partial class MapClickCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 3;

    private const string DeclareWarLine = "diplomacy.declare-war accepted (composed ahead of the attack).";
    private const string AttackPromptQuestion = "Are you sure you want to attack this army ?";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private GameMapView _map = null!;

    private bool _ok = true;
    private int _frame;
    private int _step;

    /// <summary>The session's command count, at the one seam every submission this screen issues flows through.</summary>
    private int _commandsSubmitted;

    private int _attackConfirmationsRequested;
    private int _selectionsCleared;
    private int _citiesSelected;
    private IReadOnlyList<string> _lastCommandLines = Array.Empty<string>();

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
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= InitialSettleFrames:
                    CheckLeftClickSelectsAnOwnArmyAndItsPanelShowsAllFourNumbers();
                    NextStep();
                    break;

                case 1 when _frame >= BetweenStepsFrames:
                    CheckRightClickShowsTheUnitListAndChangesNothing();
                    NextStep();
                    break;

                case 2 when _frame >= BetweenStepsFrames:
                    CheckAForeignArmyBeyondReachDropsTheSelectionAndShowsTheFoggedPanel();
                    NextStep();
                    break;

                case 3 when _frame >= BetweenStepsFrames:
                    CheckAMoveInReachSubmitsMoveAndKeepsTheSelectionWhileMovesRemain();
                    NextStep();
                    break;

                case 4 when _frame >= BetweenStepsFrames:
                    CheckAMoveThatSpendsEveryMoveEndsTheSelection();
                    NextStep();
                    break;

                case 5 when _frame >= BetweenStepsFrames:
                    CheckAnAttackAtAvoidedNationPromptsAndNoSubmitsNothing();
                    NextStep();
                    break;

                case 6 when _frame >= BetweenStepsFrames:
                    CheckYesSubmitsTheAttackWithOneComposedDeclarationOfWar();
                    NextStep();
                    break;

                case 7 when _frame >= BetweenStepsFrames:
                    CheckEscClearsTheSelection();
                    NextStep();
                    break;

                case 8 when _frame >= BetweenStepsFrames:
                    CheckShiftXClearsTheSelection();
                    NextStep();
                    break;

                case 9 when _frame >= BetweenStepsFrames:
                    CheckAnAttackOnANationAlreadyAtWarSubmitsWithNoPrompt();
                    _step++;
                    _frame = 0;
                    Finish();
                    break;

                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MapClickCheck: unhandled exception: {ex}");
            GD.Print("MapClickCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    // ---- the checks, one per Done-when clause ----

    /// <summary>Done-when 2 (selection) and Done-when 5 (the own half of the fog).</summary>
    private void CheckLeftClickSelectsAnOwnArmyAndItsPanelShowsAllFourNumbers()
    {
        LeftClick(100, 37);

        var army = _session.State.ArmyById("army-0");
        Check(army is not null, "army-0 stands at (100,37) in the fixture");
        Check(ArmyIsSelected("army-0"), "a left click on an own army selects it");

        CheckPanelShowsHeading("Army — army-0");
        var stats = PanelStatsLine();
        Check(stats is not null && StatsLineIsLaidOut(stats), "the own army's stats line is laid out");
        Check(
            stats is not null
            && FieldShowsANumber(stats.Text, "Moves")
            && FieldShowsANumber(stats.Text, "Morale")
            && FieldShowsANumber(stats.Text, "Money")
            && FieldShowsANumber(stats.Text, "Supply"),
            "the own army's panel shows a number for moves, morale, money and supply "
            + $"(got '{stats?.Text}')");
    }

    /// <summary>Done-when 2 (the right-click rule): the unit list shows, and nothing else changes.</summary>
    private void CheckRightClickShowsTheUnitListAndChangesNothing()
    {
        var commandsBefore = _commandsSubmitted;
        var clearedBefore = _selectionsCleared;
        var armiesBefore = ArmiesSelectedCount();
        var fleetsBefore = FleetsSelectedCount();

        RightClick(100, 40);

        CheckPanelShowsHeading("Units — army-11");
        Check(
            _commandsSubmitted == commandsBefore,
            $"a right click submits nothing (command count {commandsBefore} -> {_commandsSubmitted})");
        Check(
            _selectionsCleared == clearedBefore && ArmiesSelectedCount() == armiesBefore
            && FleetsSelectedCount() == fleetsBefore,
            "a right click on a foreign army fires no selection event at all, so the selection "
            + "is unchanged (still army-0)");
    }

    /// <summary>
    /// Done-when 5's foreign half: a foreign army beyond attack reach drops the selection and shows
    /// its details with the four withheld — no number for any of them.
    /// </summary>
    private void CheckAForeignArmyBeyondReachDropsTheSelectionAndShowsTheFoggedPanel()
    {
        var clearedBefore = _selectionsCleared;

        LeftClick(100, 40);

        Check(
            _selectionsCleared == clearedBefore + 1,
            "a left click on a foreign army beyond attack reach drops the selection");
        CheckPanelShowsHeading("Army — army-11");
        var stats = PanelStatsLine();
        Check(stats is not null && StatsLineIsLaidOut(stats), "the foreign army's stats line is laid out");
        Check(
            stats is not null && stats.Text.Contains("withheld", StringComparison.Ordinal)
            && !stats.Text.Any(char.IsDigit),
            "the foreign army's panel shows no number for moves, morale, money or supply "
            + $"(got '{stats?.Text}')");
    }

    /// <summary>Done-when 2's move half: the click submits <c>move</c>, the army walks, and it stays selected.</summary>
    private void CheckAMoveInReachSubmitsMoveAndKeepsTheSelectionWhileMovesRemain()
    {
        // The fog step above ended by dropping the selection (the original's own row): the move
        // test starts the way the player would, selecting the army first.
        LeftClick(100, 37);
        Check(ArmyIsSelected("army-0"), "army-0 is selected for the move");

        var commandsBefore = _commandsSubmitted;
        var clearedBefore = _selectionsCleared;

        LeftClick(100, 36);

        var army = _session.State.ArmyById("army-0");
        Check(
            _commandsSubmitted == commandsBefore + 1,
            $"the land-tile click submits one command ({commandsBefore} -> {_commandsSubmitted})");
        Check(
            _lastCommandLines.Contains("> move army-0 100 36", StringComparer.Ordinal),
            $"the submitted command is the composed move (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            army is { X: 100, Y: 36 },
            $"the army's position changed to the clicked tile (got ({army?.X}, {army?.Y}))");
        Check(army is { Moves: 7 }, $"the walk left the army 7 moves (got {army?.Moves})");
        Check(
            _selectionsCleared == clearedBefore,
            "the army is still selected after a move that leaves moves >= 1 (no SelectionCleared)");
        CheckPanelShowsHeading("Army — army-0");
    }

    /// <summary>Done-when 2's second half: a move that spends every move ends the selection.</summary>
    private void CheckAMoveThatSpendsEveryMoveEndsTheSelection()
    {
        var clearedBefore = _selectionsCleared;

        LeftClick(100, 29);

        var army = _session.State.ArmyById("army-0");
        Check(
            _lastCommandLines.Contains("> move army-0 100 29", StringComparer.Ordinal),
            $"the far tile click submits the composed move (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            army is { Moves: 0 },
            $"the walk spent every remaining move (got {army?.Moves})");
        Check(
            _selectionsCleared == clearedBefore + 1,
            "the army is not selected after a move that leaves 0 (SelectionCleared fired)");
        CheckPanelShowsHeading("Nation Overview");
    }

    /// <summary>Done-when 3's first half: the prompt opens, and No submits nothing.</summary>
    private void CheckAnAttackAtAvoidedNationPromptsAndNoSubmitsNothing()
    {
        LeftClick(98, 37);
        Check(ArmyIsSelected("army-1"), "army-1 is selected for the attack");

        var commandsBefore = _commandsSubmitted;
        var stateBeforeNo = _session.State;

        LeftClick(99, 37);

        var prompt = _mainGame.ActiveOverlay as ConfirmPrompt;
        Check(
            prompt is not null,
            $"an attack on an adjacent foreign army of a nation at peace opens the prompt "
            + $"(got {_mainGame.ActiveOverlay?.GetType().Name ?? "no overlay"})");
        Check(
            prompt?.Question == AttackPromptQuestion,
            $"the prompt asks the original's own question with its own spacing (got '{prompt?.Question}')");

        PressButton(FindButton(prompt!, "No")!);

        Check(
            _commandsSubmitted == commandsBefore,
            $"No submits nothing (command count {commandsBefore} -> {_commandsSubmitted})");
        Check(
            ReferenceEquals(_session.State, stateBeforeNo),
            "No leaves the session's state the very same reference, so no command ran at all");
        Check(
            _mainGame.ActiveOverlay is null,
            "No closes the prompt");
        Check(
            _selectionsCleared > 0 && ArmyIsNotSelected(),
            "No drops the selection");
    }

    /// <summary>Done-when 3's second half: Yes submits the attack, with the declaration composed exactly once.</summary>
    private void CheckYesSubmitsTheAttackWithOneComposedDeclarationOfWar()
    {
        LeftClick(98, 37);
        Check(ArmyIsSelected("army-1"), "army-1 is re-selected for the attack");

        var commandsBefore = _commandsSubmitted;
        var clearedBefore = _selectionsCleared;

        LeftClick(99, 37);

        var prompt = _mainGame.ActiveOverlay as ConfirmPrompt;
        Check(prompt is not null, "the same click prompts again");
        PressButton(FindButton(prompt!, "Yes")!);

        Check(
            _commandsSubmitted == commandsBefore + 1,
            $"Yes submits one command ({commandsBefore} -> {_commandsSubmitted})");
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
            _selectionsCleared > clearedBefore,
            "the selection ends after the attack");

        // The attack resolves a battle, whose result screen is modal over the map; close it so the
        // later clicks are a real player's clicks on an interactive map.
        if (_mainGame.ActiveOverlay is { } overlay && FindButton(overlay, "Close") is { } close)
        {
            PressButton(close);
        }
    }

    /// <summary>Done-when 6, first key: Esc clears the selection, through the real key handler.</summary>
    private void CheckEscClearsTheSelection()
    {
        Check(
            _mainGame.ActiveOverlay is null,
            "no overlay is open when the key is pressed, so the key belongs to the map, not a modal");

        LeftClick(99, 36);
        Check(
            _citiesSelected > 0 && ArmyIsNotSelected(),
            "a left click on Rome's own city selects it");

        var clearedBefore = _selectionsCleared;
        PressKey(Key.Escape);

        Check(
            _selectionsCleared == clearedBefore + 1,
            "Esc clears the selection (GameMapView.SelectionCleared fired)");
        Check(ArmyIsNotSelected() && CityIsNotSelected(), "no selection remains after Esc");
    }

    /// <summary>Done-when 6, second key: Shift+X clears the selection, through the real key handler.</summary>
    private void CheckShiftXClearsTheSelection()
    {
        LeftClick(99, 36);
        Check(
            _citiesSelected > 0 && CityIsSelected("Arretium"),
            "the city is selected again for Shift+X");

        var clearedBefore = _selectionsCleared;
        PressKey(Key.X, shift: true);

        Check(
            _selectionsCleared == clearedBefore + 1,
            "Shift+X clears the selection (GameMapView.SelectionCleared fired)");
    }

    /// <summary>Done-when 4: at war, the same click submits the attack with no prompt at all.</summary>
    private void CheckAnAttackOnANationAlreadyAtWarSubmitsWithNoPrompt()
    {
        LeftClick(99, 39);
        Check(ArmyIsSelected("rome-check-army"), "the third Rome army is selected for the war attack");

        var commandsBefore = _commandsSubmitted;
        var confirmationsBefore = _attackConfirmationsRequested;

        LeftClick(99, 38);

        Check(
            _attackConfirmationsRequested == confirmationsBefore,
            "an attack on a nation already at war raises no prompt "
            + $"({confirmationsBefore} -> {_attackConfirmationsRequested})");
        Check(
            _mainGame.ActiveOverlay is not ConfirmPrompt,
            "no confirmation prompt opened");
        Check(
            _commandsSubmitted == commandsBefore + 1,
            $"the click submitted the attack directly ({commandsBefore} -> {_commandsSubmitted})");
        Check(
            _lastCommandLines.Contains("> attack-army rome-check-army army-9", StringComparer.Ordinal),
            $"the submitted command is the composed attack (got '{string.Join("|", _lastCommandLines)}')");
        Check(
            !_lastCommandLines.Contains(DeclareWarLine, StringComparer.Ordinal),
            "the output composes no declaration of war against a nation already at war");
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

    // ---- the event tallies the "unchanged" assertions read ----

    private int ArmiesSelectedCount() => _armySelectedCount;

    private int FleetsSelectedCount() => _fleetSelectedCount;

    private int _armySelectedCount;
    private int _fleetSelectedCount;

    // ---- the fixture and the finish ----

    private void NextStep()
    {
        _frame = 0;
        _step++;
    }

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
    /// seat, four armies repositioned and one added, the same <c>scenario variant in code</c> seam
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

        var world = resolved.World with { StartingArmies = ValueList.From(repositioned) };
        return new GameSession(world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");
    }
}
