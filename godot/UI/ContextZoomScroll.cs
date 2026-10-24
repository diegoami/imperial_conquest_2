using Godot;

namespace IC2.Slice.UI;

/// <summary>Keeps ordinary wheel scrolling while Ctrl+wheel changes the context panel's text size.</summary>
public partial class ContextZoomScroll : ScrollContainer
{
    public event Action<bool>? TextZoomRequested;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { CtrlPressed: true, Pressed: true } wheel
            && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            TextZoomRequested?.Invoke(wheel.ButtonIndex == MouseButton.WheelUp);
            AcceptEvent();
            return;
        }

        base._GuiInput(@event);
    }
}
