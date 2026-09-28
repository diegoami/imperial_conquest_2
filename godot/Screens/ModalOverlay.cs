using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// Shared scaffolding for the two full-screen/modal interruptions <c>docs/game-design.md</c> §"User
/// interface" keeps (item 3's battle result, item 5's hotseat handoff) and the diplomacy grid (item 4) —
/// a full-rect backdrop that stops mouse input reaching whatever is underneath, and a centred content
/// panel every one of the three screens fills in. Not itself one of the three screens; each of
/// <see cref="BattleResultScreen"/>, <see cref="DiplomacyScreen"/> and <see cref="HotseatHandoffScreen"/>
/// calls <see cref="Build"/> once, in its own <c>_Ready</c>, rather than duplicating this layout three
/// times.
/// </summary>
/// <remarks>
/// A static builder, not a <see cref="Control"/> subclass of its own: every field it returns is a plain
/// already-constructed node, so the caller adds them as its own children in the same <c>_Ready</c> call
/// that builds the rest of its content — no dependency on a second node's own <c>_Ready</c> having already
/// run. Lives under <c>godot/Screens/</c> (this task's own Owns list) rather than extending
/// <c>godot/UI/UiKit.cs</c>, which is outside it — <see cref="UiKit"/>'s own public helpers
/// (<see cref="UiKit.MakePanel"/>, <see cref="UiKit.MakeLabel"/>, <see cref="UiKit.MakeButton"/>) are used
/// as-is, never modified.
/// </remarks>
public static class ModalOverlay
{
    /// <summary>
    /// Builds one full-rect backdrop plus a centred content panel, ready to add as children.
    /// </summary>
    /// <param name="root">
    /// The full-rect <see cref="Control"/> the caller should add as its own child (index 0 is fine — it
    /// carries the backdrop and the centred panel together, so ordering against the caller's own other
    /// children never matters).
    /// </param>
    /// <param name="content">The panel to add this screen's own controls to.</param>
    /// <param name="backdropColor">
    /// The backdrop's own colour. The battle result and diplomacy grid use a translucent dark backdrop
    /// (the map stays dimly visible behind them); the hotseat handoff uses an opaque one — item 5's "needs
    /// to be unmissable between human turns" reads as hiding the previous seat's own screen state
    /// entirely, not merely dimming it, which also doubles as the blind-mode floor (see
    /// <see cref="HotseatHandoffScreen"/>'s own remarks).
    /// </param>
    /// <param name="minimumWidth">The content panel's own minimum width.</param>
    public static void Build(out Control root, out VBoxContainer content, Color backdropColor, float minimumWidth = 420f)
    {
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = backdropColor, MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColorRaised);
        panel.CustomMinimumSize = new Vector2(minimumWidth, 0);
        center.AddChild(panel);

        content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        panel.AddChild(content);
    }
}
