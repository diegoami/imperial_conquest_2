namespace IC2.Slice.UI;

/// <summary>User arguments after Godot's -- separator. Ordinary startup remains menu-driven.</summary>
public sealed record ObserverLaunchOptions(bool Observe = false, bool Loop = false,
    string? ScenarioId = null, ulong? Seed = null, string? RecordingDirectory = null)
{
    public static ObserverLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var observe = false;
        var loop = false;
        string? scenario = null, recording = null;
        ulong? seed = null;
        foreach (var argument in arguments)
        {
            if (argument == "--observe") observe = true;
            else if (argument == "--observe-loop") { observe = true; loop = true; }
            else if (argument.StartsWith("--observe-scenario=", StringComparison.Ordinal))
            {
                scenario = argument["--observe-scenario=".Length..];
                if (string.IsNullOrWhiteSpace(scenario)) throw new ArgumentException("--observe-scenario needs a scenario ID.");
            }
            else if (argument.StartsWith("--observe-seed=", StringComparison.Ordinal))
            {
                if (!ulong.TryParse(argument["--observe-seed=".Length..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    throw new ArgumentException("--observe-seed needs an unsigned 64-bit integer.");
                seed = parsed;
            }
            else if (argument.StartsWith("--observe-recording=", StringComparison.Ordinal))
            {
                recording = argument["--observe-recording=".Length..];
                if (!Path.IsPathFullyQualified(recording)) throw new ArgumentException("--observe-recording needs an absolute output directory.");
            }
            else if (argument.StartsWith("--observe", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown observer option '{argument}'. Use --observe or --observe-loop.");
        }
        if (!observe && (scenario is not null || seed is not null || recording is not null))
            throw new ArgumentException("Observer scenario/seed/recording options require --observe or --observe-loop.");
        if (recording is not null && !loop) throw new ArgumentException("Recording requires --observe-loop.");
        return new(observe, loop, scenario, seed, recording);
    }
}
