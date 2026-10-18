using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.Audio;

/// <summary>Producer identity is retained through queued playback and lifecycle cancellation.</summary>
public sealed record TacticalAudioCue(string BattleId, long FrameId, string Key);

/// <summary>Adapter for the production displayed-entry seam; traversal owns exactly-once identity.</summary>
public sealed class TacticalPlaybackAudio(Ruleset rules, Func<bool> soundEnabled, Action<TacticalAudioCue> play)
{
    public void ExchangeEntered(TacticalBattleFrame frame)
    {
        if (soundEnabled() && TacticalSoundCues.ForExchange(frame, rules) is { } key)
            play(new TacticalAudioCue(frame.BattleId, frame.FrameId, key));
    }
}
