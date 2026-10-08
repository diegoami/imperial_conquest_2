namespace IC2.Slice.UI;

/// <summary>
/// T147 (bug #781 point 4): the readable line the main game screen's output area shows for an accepted
/// order, in place of the engine's raw <c>"{kind} accepted."</c>. The engine's own text is pinned (its CLI
/// goldens and the peace-treaty tests read it), so every wording here is app-side only and is keyed by the
/// command's <see cref="IC2.Engine.Core.ICommand.Kind"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[designed].</strong> No report or design document specifies app wording for the engine's
/// commands (the original's own message table is not a source for a from-scratch Godot Control tree), so
/// each line is this task's own short sentence, chosen to read as the player's action rather than a key.
/// A kind with no entry falls back to the engine's own line, never to <see langword="null"/> — see
/// <see cref="For"/>.
/// </para>
/// <para>
/// <strong>Why some kinds are absent.</strong> <see cref="NotIssuedByTheApp"/> lists the command kinds the
/// Godot app never submits: the three AI-internal diplomacy commands (no human verb at all) and the seven
/// the UI leaves disabled or has no control for. Nothing in the app ever renders their acceptance, so they
/// keep the engine's line as dead fallback rather than shipping an unreachable sentence. The unit test
/// enumerates every <see cref="IC2.Engine.Core.ICommand"/> type, removes that set, and fails if any
/// remaining kind has no wording.
/// </para>
/// </remarks>
public static class CommandOutcomeWording
{
    /// <summary>The suffix the engine's generic acceptance line ends with (<c>GameSession.Commands.cs</c>).</summary>
    public const string AcceptanceSuffix = " accepted.";

    /// <summary>Command kinds the Godot app never issues, so they need no app wording.</summary>
    public static IReadOnlySet<string> NotIssuedByTheApp { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // The three AI-internal diplomacy commands: no human-facing verb exists for them.
        "diplomacy.ai-form-alliance",
        "diplomacy.ai-form-trade",
        "diplomacy.ai-swap-trade-partner",

        // No Godot control submits these (Strategy's Taxation and the Fleet rows are shown disabled, and
        // the mercenary pool is empty by design — bug #229/#457).
        "economy.set-tax",
        "naval.split-fleet",
        "naval.join-fleets",
        "naval.order-fleet",
        "naval.buy-fleet-supply",
        "naval.fleet-to-fleet-transfer",
        "recruitment.hire-mercenary",
    };

    /// <summary>The readable line for each command kind the app can issue, keyed by <c>ICommand.Kind</c>.</summary>
    public static IReadOnlyDictionary<string, string> ByKind { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Movement and naval movement (the map's own click orders).
            ["movement.move-army"] = "Army moved.",
            ["naval.move-fleet"] = "Fleet moved.",
            ["naval.embark-army"] = "Army embarked.",
            ["naval.disembark-army"] = "Army disembarked.",

            // Battles (the map's attack/siege orders and the battle result they open).
            ["battle.attack-army"] = "Battle fought.",
            ["battle.besiege-city"] = "Siege resolved.",
            ["battle.attack-fleet"] = "Naval battle fought.",

            // Recruitment and city orders (the context panel).
            ["recruitment.recruit-standing-unit"] = "Unit recruited.",
            ["recruitment.mobilize-recruit-slot"] = "Recruitment slot mobilized.",
            ["recruitment.disband-recruitment-slot"] = "Recruitment slot disbanded.",
            ["city.order"] = "City order given.",
            ["naval.repair-fleet"] = "Fleet repaired.",
            ["naval.scuttle-fleet"] = "Fleet scuttled.",

            // The Unit map's Army dialogs.
            ["armies.army-transfer"] = "Unit transferred.",
            ["armies.split-army"] = "Army split.",
            ["armies.join-armies"] = "Armies joined.",
            ["armies.disband-army"] = "Army disbanded.",
            ["armies.disband-unit"] = "Unit disbanded.",
            ["armies.rename-unit"] = "Unit renamed.",
            ["armies.split-unit"] = "Unit split.",
            ["armies.join-units"] = "Units joined.",

            // Supply and money (the Supply army dialog and the money prompts).
            ["economy.buy-supply"] = "Supply bought.",
            ["economy.transfer-money"] = "Money transferred.",

            // Diplomacy (the International Relations grid).
            ["diplomacy.declare-war"] = "You declared war.",
            ["diplomacy.make-peace"] = "You made peace.",
            ["diplomacy.propose-alliance"] = "Alliance proposed.",
            ["diplomacy.propose-trade"] = "Trade proposed.",
            ["diplomacy.accept-pending-offer"] = "You accepted the offer.",

            // The post-battle Offer of peace window's Yes.
            ["diplomacy.accept-peace-treaty"] = "You accepted the peace treaty.",
        };

    /// <summary>
    /// The readable line for <paramref name="kind"/>, or <see langword="null"/> when the app has none —
    /// the caller then keeps the engine's own <c>"{kind} accepted."</c> (Scope's fallback).
    /// </summary>
    public static string? For(string kind) =>
        ByKind.TryGetValue(kind, out var wording) ? wording : null;

    /// <summary>
    /// Replaces one exact <c>"{kind} accepted."</c> line with its wording, and returns the line unchanged
    /// for every other line (a rejection, the composed declaration, or a kind with no wording).
    /// </summary>
    public static string ReadableLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.EndsWith(AcceptanceSuffix, StringComparison.Ordinal))
        {
            return line;
        }

        var kind = line[..^AcceptanceSuffix.Length];
        return For(kind) ?? line;
    }

    /// <summary>
    /// Applies <see cref="ReadableLine"/> to each newline-separated line of an outcome block. The
    /// engine's rejected lines and its composed <c>accepted (…)</c> lines are untouched.
    /// </summary>
    public static string Apply(string block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (block.Length == 0)
        {
            return block;
        }

        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = ReadableLine(lines[i]);
        }

        return string.Join('\n', lines);
    }
}
