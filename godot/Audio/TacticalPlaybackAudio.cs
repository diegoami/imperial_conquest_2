using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.Audio;

/// <summary>Adapter for the production displayed-entry seam; traversal owns exactly-once identity.</summary>
public sealed class TacticalPlaybackAudio(Ruleset rules, Func<bool> soundEnabled, Action<string> play)
{
    public void ExchangeEntered(TacticalBattleFrame frame)
    {
        if (soundEnabled() && TacticalSoundCues.ForExchange(frame, rules) is { } key) play(key);
    }
}
