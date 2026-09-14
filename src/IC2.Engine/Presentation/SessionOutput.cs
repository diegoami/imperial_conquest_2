namespace IC2.Engine.Presentation;

/// <summary>What one submitted line produced: the text to print, and whether the session should stop.</summary>
/// <param name="Lines">
/// The rendered transcript for this one command, in order — the echoed input line, the command's own
/// output, and a trailing blank separator line.
/// </param>
/// <param name="ShouldExit">Whether the caller (a script loop or a stdin loop) should stop reading.</param>
public sealed record SessionOutput(IReadOnlyList<string> Lines, bool ShouldExit);
