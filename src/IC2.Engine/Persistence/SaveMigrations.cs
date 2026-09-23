using System.Text.Json.Nodes;

namespace IC2.Engine.Persistence;

/// <summary>
/// Migrates an older save envelope forward to <see cref="SaveFormat.CurrentVersion"/>, one version step
/// at a time.
/// </summary>
/// <remarks>
/// Empty at <see cref="SaveFormat.CurrentVersion"/> 1: this task creates the save format, so no older
/// version exists yet to migrate from (<c>docs/tasks/T20.md</c> Done-when 2's own hazard note). The first
/// real step lands in the same commit that first bumps <see cref="SaveFormat.CurrentVersion"/> past 1.
/// </remarks>
internal static class SaveMigrations
{
    /// <summary>
    /// Steps <paramref name="envelope"/>, declaring <paramref name="foundVersion"/>, forward to
    /// <see cref="SaveFormat.CurrentVersion"/>.
    /// </summary>
    /// <exception cref="UnsupportedSaveFormatException">
    /// <paramref name="foundVersion"/> is older than any version this build knows how to migrate from.
    /// </exception>
    public static JsonObject MigrateToCurrent(string documentPath, JsonObject envelope, int foundVersion) =>
        throw new UnsupportedSaveFormatException(documentPath, foundVersion, SaveFormat.CurrentVersion);
}
