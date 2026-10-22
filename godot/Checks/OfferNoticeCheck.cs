using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Fix #917: the original's OK-only box at the human's turn start when an AI nation offers trade or an
/// alliance. Drives a seeded game (seat rome, seed 4) with the real <see cref="MainGameScreen"/> until the
/// engine holds a pending offer at the human's turn start, then checks the box shows the engine's exact
/// text, OK closes it, Esc closes a second one, it is not shown again for the same offer, and a turn start
/// with no offer shows none. Seed 4 since #1012: the new strategic step changed the AI's marches, and on
/// seed 7 the second offer came only at turn 29; on seed 4 the two offers come at turns 3 and 4, after
/// three quiet turn starts. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/OfferNoticeCheck.tscn
/// </code>
/// </summary>
public partial class OfferNoticeCheck : Node
{
    private const ulong Seed = 4UL;
    private const int MaxTurns = 150;

    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"OfferNoticeCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"OfferNoticeCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
    }

    private void Run()
    {
        var resolved = GameDataContext.Repository.Resolve("example-classical-improved");
        var scenario = resolved.Scenario with
        {
            Seats = ValueList.From(resolved.Scenario.Seats.Select(seat => seat with
            {
                Control = seat.Nation == "rome" ? SeatControl.Human : SeatControl.Ai,
            })),
        };
        var session = new GameSession(resolved.World, resolved.Ruleset, scenario, seedOverride: Seed);
        var mainGame = new MainGameScreen
        {
            Session = session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
            CheckSaveSuffix = "offer-notice",
        };
        AddChild(mainGame);

        var shownOffers = 0;
        var quietTurns = 0;
        for (var turn = 0; turn < MaxTurns && shownOffers < 2; turn++)
        {
            var offer = session.State.PendingOffer;
            var box = FindNotice(mainGame);
            if (offer is null)
            {
                if (box is null)
                {
                    quietTurns++;
                }
                else
                {
                    Check(false, $"turn {turn}: no pending offer, yet a box is shown ('{box.Text}')");
                }
            }
            else
            {
                var proposer = session.State.NationById(offer.ProposingNationId)!.Name;
                var human = session.State.NationById(session.State.ActiveNationId)!.Name;
                var verb = offer.ProposedRelationCode == session.Ruleset.Diplomacy.StateCodes.Trade
                    ? "trade with"
                    : "form an alliance with";
                var expected = $"{proposer} wants to {verb} {human}.";
                Check(box is not null, $"turn {turn}: a pending offer shows the OK box");
                if (box is not null)
                {
                    Check(box.Text == expected, $"turn {turn}: box text is '{expected}' (got '{box.Text}')");
                    shownOffers++;
                    if (shownOffers == 1)
                    {
                        PressOk(box!.Button);
                    }
                    else
                    {
                        mainGame.ActiveOverlay!._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
                    }

                    Check(
                        mainGame.ActiveOverlay is null && FindNotice(mainGame) is null,
                        $"turn {turn}: the box closed ({(shownOffers == 1 ? "OK" : "Esc")})");
                    Check(
                        session.State.PendingOffer == offer,
                        $"turn {turn}: the box changed nothing; the offer is still pending for the button");
                }
            }

            mainGame.SubmitForCheck("end");
            DismissOverlays(mainGame);
        }

        Check(shownOffers == 2, $"two offers were announced and shown within {MaxTurns} turns (got {shownOffers})");
        Check(quietTurns > 0, $"turn starts without an offer showed no box ({quietTurns})");
        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    // The notice is found by what it shows (an OK button under a label), not by its type, so the same
    // check compiles and fails on a build that has no notice.
    private static Notice? FindNotice(MainGameScreen screen)
    {
        if (screen.ActiveOverlay is not { } overlay)
        {
            return null;
        }

        var ok = Descendants(overlay).OfType<Button>().FirstOrDefault(b => b.Text == "OK");
        var label = Descendants(overlay).OfType<Label>().FirstOrDefault();
        return ok is null || label is null ? null : new Notice(label.Text, ok);
    }

    private sealed record Notice(string Text, Button Button);

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static void PressOk(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);

    // Battle windows and a peace offer window can precede the box; the offers they raise are answered No.
    private void DismissOverlays(MainGameScreen mainGame)
    {
        for (var guard = 0; guard < 50 && mainGame.ActiveOverlay is { } overlay; guard++)
        {
            switch (overlay)
            {
                case BattleResultScreen battle:
                    battle.Close();
                    break;
                case PeaceOfferScreen peace:
                    peace.Buttons.First(b => b.Text == "No").EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                default:
                    if (FindNotice(mainGame) is not null)
                    {
                        return;
                    }

                    Check(false, $"unexpected overlay {overlay.GetType().Name}");
                    return;
            }
        }
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
