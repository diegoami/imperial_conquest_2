namespace IC2.Slice.UI;

/// <summary>User arguments after Godot's -- separator. Ordinary startup remains menu-driven.</summary>
public sealed record ObserverLaunchOptions(bool Observe = false, bool Loop = false)
{
    public static ObserverLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var observe = false;
        var loop = false;
        foreach (var argument in arguments)
        {
            if (argument == "--observe") observe = true;
            else if (argument == "--observe-loop") { observe = true; loop = true; }
            else if (argument.StartsWith("--observe", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown observer option '{argument}'. Use --observe or --observe-loop.");
        }
        return new(observe, loop);
    }
}
