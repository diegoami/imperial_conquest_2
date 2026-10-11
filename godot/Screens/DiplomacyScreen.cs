using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/game-design.md</c> §"User interface" item 4: "the original's peace/trade/ally/war grid per
/// nation (the International Relations screen) — already a clear, working UI pattern, reused as-is."
/// </summary>
/// <remarks>
/// Issues orders only through <see cref="GameSession.Submit"/>, with the existing CLI verbs
/// (<c>declare-war</c>, <c>make-peace</c>, <c>propose-alliance</c>, <c>propose-trade</c>,
/// <c>accept-offer</c>) — <c>docs/tasks/T25.md</c>'s own Owns note: "never mutate GameState from the UI."
/// The grid's own rows and which actions each one currently allows come from
/// <see cref="DiplomacyGridViewModel.BuildRows"/>, never re-derived here.
/// </remarks>
public partial class DiplomacyScreen : Control
{
    public required GameSession Session { get; init; }

    /// <summary>Raised after this screen issues a command, so <see cref="MainGameScreen"/> can refresh the
    /// map/news the same way it does for <see cref="ContextPanel"/> and <see cref="GameMapView"/>.</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    /// <summary>Raised when the player dismisses this screen.</summary>
    public event Action? Closed;

    /// <summary>Exposed for <c>godot/Screens/Checks/**</c> to assert on the rendered grid without re-deriving it.</summary>
    public GridContainer Grid { get; private set; } = null!;

    private VBoxContainer _content = null!;

    public override void _Ready() => Rebuild();

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        ModalOverlay.Build(out var root, out var content, new Color(0f, 0f, 0f, 0.65f), minimumWidth: 560f);
        AddChild(root);
        _content = content;

        content.AddChild(UiKit.MakeLabel("International Relations", 22, UiKit.AccentColor));

        BuildPendingOfferRow();

        Grid = new GridContainer { Columns = 6 };
        Grid.AddThemeConstantOverride("h_separation", 12);
        Grid.AddThemeConstantOverride("v_separation", 6);

        // The grid has one row per nation (16 in the shipped scenario), taller than the window, so it sits
        // in a scroll container whose height is capped to what the viewport leaves after the title, the
        // pending-offer row and the Close button: the Close button always stays on screen (bug #968).
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(Grid);
        content.AddChild(scroll);

        AddHeaderRow();
        foreach (var row in DiplomacyGridViewModel.BuildRows(Session.State, Session.Ruleset, Session.State.ActiveNationId))
        {
            AddNationRow(row);
        }

        content.AddChild(new HSeparator());
        content.AddChild(UiKit.MakeButton("Close", Close));

        var cap = Math.Max(MinimumGridHeight, GetViewportRect().Size.Y - ReservedHeight);
        scroll.CustomMinimumSize = new Vector2(0, Math.Min(cap, Grid.GetCombinedMinimumSize().Y));
    }

    /// <summary>Height the window keeps for everything but the grid: title, pending offer, separator, the
    /// Close button, the panel's margins and the gaps between them.</summary>
    private const float ReservedHeight = 300f;

    private const float MinimumGridHeight = 120f;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            var viewport = GetViewport();
            Close();
            viewport.SetInputAsHandled();
        }
    }

    /// <summary>Dismisses this screen — what the "Close" button does, exposed under its own name for
    /// <c>godot/Screens/Checks/**</c>, the same convention <see cref="BattleResultScreen.Close"/> uses.</summary>
    public void Close() => Closed?.Invoke();

    private void AddHeaderRow()
    {
        Grid.AddChild(UiKit.MakeLabel("Nation", 13, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Relation", 13, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("War", 13, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Peace", 13, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Alliance", 13, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Trade", 13, UiKit.MutedTextColor));
    }

    private void AddNationRow(DiplomacyRelationRow row)
    {
        Grid.AddChild(UiKit.MakeLabel(row.NationName, 14, UiKit.TextColor));
        Grid.AddChild(UiKit.MakeLabel(row.RelationLabel, 14, UiKit.TextColor));
        Grid.AddChild(MakeActionButton("Declare War", row.CanDeclareWar, () => Issue($"declare-war {row.NationId}")));
        Grid.AddChild(MakeActionButton("Make Peace", row.CanMakePeace, () => Issue($"make-peace {row.NationId}")));
        Grid.AddChild(MakeActionButton("Ally", row.CanProposeAlliance, () => Issue($"propose-alliance {row.NationId}")));
        Grid.AddChild(MakeActionButton("Trade", row.CanProposeTrade, () => Issue($"propose-trade {row.NationId}")));
    }

    private void BuildPendingOfferRow()
    {
        var offer = Session.State.PendingOffer;
        if (offer is null)
        {
            return;
        }

        var proposer = Session.State.NationById(offer.ProposingNationId)?.Name ?? offer.ProposingNationId;
        _content.AddChild(UiKit.MakeLabel($"Pending offer from {proposer}.", 14, UiKit.TextColor));
        _content.AddChild(UiKit.MakeButton("Accept Offer", () => Issue("accept-offer")));
    }

    private Control MakeActionButton(string label, bool enabled, Action onPressed)
    {
        var button = UiKit.MakeButton(label, onPressed, 12);
        button.Disabled = !enabled;
        return button;
    }

    private void Issue(string commandLine)
    {
        var output = Session.Submit(commandLine);
        CommandIssued?.Invoke(output.Lines);

        // Rebuilds so the grid reflects the relation the command just changed immediately, the same
        // "act, then Refresh()" shape ContextPanel.Issue already uses.
        Rebuild();
    }
}
