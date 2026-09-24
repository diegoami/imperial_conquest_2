using System.Globalization;

namespace IC2.Engine.Ai;

/// <summary>
/// Formats a rationale line culture-invariantly. The per-seed log is a test artifact a failing seed
/// is reproduced from (<c>docs/task-catalogue.md</c> T22 Done-when 4), so two machines with
/// different locales have to write the same bytes.
/// </summary>
internal static class AiFormat
{
    internal static string Inv(string format, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);
}
