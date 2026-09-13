using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Engine.Core;

/// <summary>
/// A stable fingerprint of a <see cref="GameState"/>: the hash two runs of the same scripted turns must
/// agree on, and the one they must differ on when the seed changes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why hash the canonical JSON rather than the object graph.</strong> The obvious alternative,
/// <see cref="object.GetHashCode"/> over T02's value-equal record tree, is wrong here for two independent
/// reasons. .NET randomizes string hashing per process, so the same state would fingerprint differently
/// in two runs of the same test; and a 32-bit hash collides often enough that "different seeds produce
/// different hashes" would eventually fail for a reason that has nothing to do with the engine. Hashing
/// the serialized bytes borrows T02's already-canonical, already-tested contract — declaration-ordered
/// properties, no dictionaries, nothing dropped — and makes the fingerprint exactly as stable as the
/// save format, which is the property actually wanted.
/// </para>
/// <para>
/// <strong>Not indented.</strong> <see cref="GameJson.Options"/> writes indented, because its files are
/// hand-reviewed; indentation would put the platform's newline into the hash and make a fixture written
/// on Windows disagree with the same state on Linux. The options used here are a copy of the canonical
/// ones — same converters, same naming, same enum handling — with indentation turned off, so the hash
/// covers the same information in a platform-independent form.
/// </para>
/// </remarks>
public static class GameStateHash
{
    private static readonly JsonSerializerOptions HashOptions =
        new(GameJson.Options) { WriteIndented = false };

    /// <summary>The canonical bytes a state is fingerprinted from — its compact JSON, UTF-8 encoded.</summary>
    public static byte[] CanonicalBytes(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.SerializeToUtf8Bytes(state, HashOptions);
    }

    /// <summary>The SHA-256 fingerprint of a state, as lowercase hex.</summary>
    public static string Compute(GameState state) =>
        Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(state)));

    /// <summary>
    /// The fingerprint of a whole sequence of states — every state the pipeline passed through, folded
    /// into one value.
    /// </summary>
    /// <remarks>
    /// Stronger than hashing only the final state: two runs can converge on the same end state after
    /// diverging in the middle, and a determinism check that could not see that would be missing the
    /// interesting failure.
    /// </remarks>
    public static string ComputeSequence(IEnumerable<GameState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        using var sha = SHA256.Create();
        var count = 0;
        foreach (var state in states)
        {
            var bytes = CanonicalBytes(state);
            // The index and the length go in alongside the payload so that concatenation is
            // unambiguous: two different sequences cannot serialize to the same byte stream.
            var header = Encoding.UTF8.GetBytes($"[{count}:{bytes.Length}]");
            sha.TransformBlock(header, 0, header.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            count++;
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }
}
