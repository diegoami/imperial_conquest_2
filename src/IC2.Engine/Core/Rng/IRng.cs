namespace IC2.Engine.Core;

/// <summary>
/// The one seeded source of randomness in gameplay code
/// (<c>docs/game-design.md</c> design principle 4, "Determinism first").
/// </summary>
/// <remarks>
/// <para>
/// Every random draw in every later system goes through this interface. <c>System.Random</c>, the wall
/// clock, <c>Guid.NewGuid</c> and <c>Environment.TickCount</c> are forbidden outright in
/// <c>src/IC2.Engine</c>, and <c>tests/IC2.Engine.Tests/Core/Determinism</c> contains the source scanner
/// that proves it.
/// </para>
/// <para>
/// <strong>Streams, and why they matter for a 29-task parallel build.</strong> A single shared draw
/// sequence would couple every system to every other one: the moment T08's economy adds one extra draw,
/// T16's battle fixtures would produce different numbers, even though nothing about battle resolution
/// changed. <see cref="ForStream"/> avoids that by deriving an independent generator purely from
/// (<see cref="Seed"/>, stream name) — never from how many draws the parent has already made. Two
/// systems therefore cannot perturb each other's rolls no matter how their draw counts change, and a
/// golden fixture written by one task survives another task's edits.
/// </para>
/// <para>
/// Implementations are stateful within a stream: successive calls advance that stream. The persisted
/// root state is <see cref="Model.GameState.RandomSeed"/>, which
/// <see cref="TurnCoordinator"/> and <see cref="CommandDispatcher"/> advance at exactly two documented
/// points; see <see cref="RngStreams"/>.
/// </para>
/// </remarks>
public interface IRng
{
    /// <summary>
    /// The seed this stream was created from. Stable for the life of the instance, and the only input
    /// <see cref="ForStream"/> derives from.
    /// </summary>
    ulong Seed { get; }

    /// <summary>
    /// The generator's current position. Changes with every draw. Exposed so a caller that needs to
    /// persist a stream's position can, though the engine's own persistence is
    /// <see cref="Model.GameState.RandomSeed"/> plus the stream names, not this value.
    /// </summary>
    ulong State { get; }

    /// <summary>Draws the next raw 64-bit value.</summary>
    ulong NextUInt64();

    /// <summary>
    /// Draws an integer in <c>[0, exclusiveUpperBound)</c>, without modulo bias.
    /// </summary>
    /// <param name="exclusiveUpperBound">The exclusive upper bound; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exclusiveUpperBound"/> is not positive.</exception>
    int NextInt(int exclusiveUpperBound);

    /// <summary>Draws an integer in <c>[inclusiveLowerBound, exclusiveUpperBound)</c>, without modulo bias.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty or inverted.</exception>
    int NextInt(int inclusiveLowerBound, int exclusiveUpperBound);

    /// <summary>
    /// A <c>numerator</c>-in-<c>denominator</c> roll — the shape the reports state the original's own
    /// odds in ("a 1-in-4 further promotion", "a 2-in-5 peace roll", "a 1-in-3 chance of a loyalty
    /// penalty"), so a later system transcribes the odds rather than rearranging them into a float.
    /// </summary>
    /// <param name="numerator">Favourable outcomes; may be zero (never) but not negative.</param>
    /// <param name="denominator">Total outcomes; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The odds are not a valid fraction of a whole.</exception>
    bool NextChance(int numerator, int denominator);

    /// <summary>
    /// An independent generator for a named sub-stream, derived purely from <see cref="Seed"/> and
    /// <paramref name="streamName"/>. Calling it does not advance this generator, and calling it twice
    /// with the same name returns two generators that produce the same sequence.
    /// </summary>
    /// <param name="streamName">
    /// A stable, meaningful name — a system id, a command kind, a phase. Two different names give
    /// independent sequences; the same name always gives the same one.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is null or whitespace.</exception>
    IRng ForStream(string streamName);
}
