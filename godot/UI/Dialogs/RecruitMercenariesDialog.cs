using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Army menu's <strong>Recruit mercenaries</strong> dialog — the original's <c>TRecruitMercs</c>
/// window [confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.6; decompiled
/// <c>TUnitMap_RecruitMercenaries</c> <c>0x00446FF4</c>]. It lists the chosen city's live offers (type,
/// troops, quality, quarterly cost) and a "Recruit unit" button that submits one <c>hire-mercenary</c>
/// command — the same path every other dialog on this screen uses, so <see cref="MainGameScreen.CommandIssued"/>
/// counts it once.
/// </summary>
/// <remarks>
/// <para>
/// All rules live in the Godot-free <see cref="MercenaryDialogModel"/>; this control owns only widgets,
/// the submit callback and <see cref="Closed"/>. The model reads the engine's adjacency rule from the
/// same <see cref="IC2.Engine.Recruitment.Commands.HireMercenaryCommandHandler.FindChosenCity"/> shape
/// it implements, so the dialog's listing and the engine's own live offer scan cannot disagree.
/// </para>
/// <para>
/// <strong>Refusals.</strong> T76 owns the engine rules — 20 units, 100,000 troops, enemy city, supplies,
/// fleet space — and rejects with its own engine codes (T143/bug #755 turns the original's purge into
/// the gate). The dialog shows whatever the engine's outcome says, never its own refusal; a refused
/// hire leaves the dialog open, the player tries again.
/// </para>
/// <para>
/// <strong>Title</strong> is "Recruit mercenary unit", the audit's §1.6 row's transcription of the
/// original's form [confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.6, 'it is
/// titled "Recruit mercenary unit"']. The subtitle carries the chosen city's name (or "no offers" with
/// an open dialog of zero rows, never a hidden failure).
/// </para>
/// </remarks>
public partial class RecruitMercenariesDialog : Control
{
    /// <summary>The session the dialog submits to and reads its figures from.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The army the dialog hires into. The dialog's own adjacency rule resolves from this id.</summary>
    public required string ArmyId { get; init; }

    /// <summary>
    /// Submits one composed command line through the screen's own path and returns the session's output
    /// lines, so the dialog can show the reply and refresh its figures.
    /// </summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by Close (or Esc); <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private MercenaryDialogModel _model = null!;
    private ItemList _offerList = null!;
    private Label _summaryLabel = null!;
    private Label _replyLabel = null!;
    private Button _hireButton = null!;
    private Button _closeButton = null!;

    /// <summary>The live model, exposed for headless check assertions.</summary>
    public MercenaryDialogModel ModelForCheck => _model;

    /// <summary>The session's reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var army = Session.State.ArmyById(ArmyId)
            ?? throw new InvalidOperationException($"Army '{ArmyId}' is not a known army.");
        _model = MercenaryDialogModel.ForArmy(Session.State, ArmyId, Session.Ruleset);

        if (!_model.DialogOpens)
        {
            // T76's adjacency rule found no city in reach, or the army already has 20 units.
            // The dialog still opens (the brief's amendment is built around the opened dialog's
            // behaviour); with no offers the listing stays empty and the Recruit button is disabled.
        }

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Recruit mercenary unit", 18, UiKit.AccentColor));
        _summaryLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.TextColor);
        _summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_summaryLabel);

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel("Mercenary offers", 15, UiKit.TextColor));
        _offerList = new ItemList { CustomMinimumSize = new Vector2(0, 140) };
        _offerList.ItemSelected += _ =>
        {
            _hireButton.Disabled = _offerList.GetSelectedItems().Length == 0;
        };
        column.AddChild(_offerList);

        _hireButton = UiKit.MakeButton("Recruit unit", SubmitHire);
        _hireButton.Disabled = _model.Offers.Count == 0;
        column.AddChild(_hireButton);

        column.AddChild(new HSeparator());
        _replyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.TextColor);
        _replyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_replyLabel);

        var buttonRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.AddChild(buttonRow);
        _closeButton = UiKit.MakeButton("Close", Close);
        buttonRow.AddChild(_closeButton);

        UpdateSummary();
        UpdateOfferList();
    }

    private void UpdateSummary()
    {
        if (_model.Offers.Count == 0)
        {
            _summaryLabel.Text = $"No mercenary offers are in reach of army {_model.ArmyId} (no city at distance exactly 1).";
        }
        else
        {
            _summaryLabel.Text =
                $"Mercenary offers at {_model.CityName ?? "the chosen city"} for army {_model.ArmyId}";
        }
    }

    private void UpdateOfferList()
    {
        _offerList.Clear();
        foreach (var offer in _model.Offers)
        {
            _offerList.AddItem(FormatOffer(offer));
        }

        _hireButton.Disabled = _model.Offers.Count == 0;
    }

    private static string FormatOffer(MercenaryOfferLine offer) =>
        $"{offer.TypeName}  —  {offer.Troops.ToString(System.Globalization.CultureInfo.InvariantCulture)} troops  —  "
        + MercenaryDialogModel.QualityCaption(offer.Quality)
        + $"  —  {offer.QuarterlyCostTalents.ToString(System.Globalization.CultureInfo.InvariantCulture)}/quarter";

    /// <summary>
    /// Selects an offer row by id, exactly as a real click on the list would. Exposed for the
    /// headless check.
    /// </summary>
    public void SelectOfferForCheck(int slotIndex)
    {
        for (var i = 0; i < _model.Offers.Count; i++)
        {
            if (_model.Offers[i].SlotIndex == slotIndex)
            {
                _offerList.Select(i);
                _hireButton.Disabled = false;
                return;
            }
        }
    }

    /// <summary>
    /// Submits the selected offer through the screen's own path — equivalent to pressing the
    /// Recruit unit button when an offer row is selected. Exposed for the headless check.
    /// </summary>
    public void HireSelectedForCheck()
    {
        var indices = _offerList.GetSelectedItems();
        if (indices.Length == 0)
        {
            return;
        }

        var row = (int)indices[0];
        if (row < 0 || row >= _model.Offers.Count)
        {
            return;
        }

        var offer = _model.Offers[row];
        var line = _model.HireCommandLine(offer, ArmyId);
        var lines = Submit(line);

        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;

        // The dialog closes if the hire filled the army's 20th unit — the brief's "(derived: code,
        // :43686, iVar3 == 0x13 ... TRecruitMercs_OK)" amendment. The model decides it from the live
        // offers and the army's pre-hire unit count. Refresh and close accordingly.
        var refresh = MercenaryDialogModel.ForArmy(Session.State, ArmyId, Session.Ruleset);
        if (!refresh.DialogOpens || _model.HireFillsCap(offer))
        {
            Close();
        }
        else
        {
            _model = refresh;
            UpdateSummary();
            UpdateOfferList();
        }
    }

    private void SubmitHire() => HireSelectedForCheck();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Closes the dialog — the Close button and Esc both call this.</summary>
    public void Close() => Closed?.Invoke();
}
