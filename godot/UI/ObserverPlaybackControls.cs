using Godot;

namespace IC2.Slice.UI;

/// <summary>Shared speed preference for observer map and battle presentation; never simulation timing.</summary>
public static class ObserverPlaybackControls
{
    public static void Synchronize(OptionButton speed)
    {
        var percent = SettingsScreen.Current.ObserverSpeedPercent;
        for (var i = 0; i < speed.ItemCount; i++)
            if (speed.GetItemId(i) == percent) { speed.Select(i); return; }
        speed.AddItem($"{percent / 100.0:0.##}x", percent); speed.Select(speed.ItemCount - 1);
    }
    public static OptionButton SpeedChoice()
    {
        var speed = new OptionButton { Name = "ObserverSpeed" };
        var choices = new[] { 25, 50, 100, 200, 400, 800 }.Append(SettingsScreen.Current.ObserverSpeedPercent).Distinct().Order().ToArray();
        foreach (var percent in choices) speed.AddItem($"{percent / 100.0:0.##}x", percent);
        speed.Select(Array.IndexOf(choices, SettingsScreen.Current.ObserverSpeedPercent));
        speed.TooltipText = "Observer playback speed · map movement and battles";
        speed.ItemSelected += index => SettingsScreen.Commit(SettingsScreen.Current with { ObserverSpeedPercent = speed.GetItemId((int)index) });
        return speed;
    }
}
