using IC2.Engine.Model;

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
/// Godot app never submits: the AI-internal commands (no human verb at all; bug #914 removed the seven
/// the Taxation, fleet and mercenary dialogs now submit). Nothing in the app ever renders their acceptance, so they
/// keep the engine's line as dead fallback rather than shipping an unreachable sentence. The unit test
/// enumerates every <see cref="IC2.Engine.Core.ICommand"/> type, removes that set, and fails if any
/// remaining kind has no wording.
/// </para>
/// </remarks>
public static class CommandOutcomeWording
{
    /// <summary>The kind of the Build fleet order.</summary>
    public const string OrderFleetKind = "naval.order-fleet";

    /// <summary>The suffix the engine's generic acceptance line ends with (<c>GameSession.Commands.cs</c>).</summary>
    public const string AcceptanceSuffix = " accepted.";

    /// <summary>Command kinds the Godot app never issues, so they need no app wording.</summary>
    public static IReadOnlySet<string> NotIssuedByTheApp { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // The three AI-internal diplomacy commands: no human-facing verb exists for them.
        "diplomacy.ai-form-alliance",
        "diplomacy.ai-form-trade",
        "diplomacy.ai-swap-trade-partner",

        // T159: the AI's own army and fleet merges under classical-faithful; the app never issues them.
        "armies.ai-absorb-army",
        "naval.ai-absorb-fleet",

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

            // Bug #914: the Taxation, fleet and mercenary dialogs submit these. The original closes them
            // silently (2026-10-08-strategy-menu-s01-s12.md); these short sentences are the user's choice
            // of 2026-10-11 (#914).
            ["economy.set-tax"] = "Tax set.",
            ["naval.split-fleet"] = "Fleets split.",
            ["naval.join-fleets"] = "Fleets joined.",
            ["naval.buy-fleet-supply"] = "Fleet supplied.",
            ["naval.fleet-to-fleet-transfer"] = "Ships transferred.",
            ["recruitment.hire-mercenary"] = "Mercenaries hired.",

            // Build fleet: the original answers "The fleet will be built at <city>."
            // (2026-10-02-fleet-orders-live.md). The city is not in the engine's line, so Apply(block, state)
            // names it; this is the fallback when no state is given.
            [OrderFleetKind] = "The fleet will be built.",

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
    /// Replaces one exact <c>"{kind} accepted."</c> line with its wording, and an engine refusal
    /// (<c>"{kind} rejected ({code}): {message}"</c>, <c>GameSession.IssueCommand</c>'s shape) with its own
    /// plain message, capitalised (#1280 F3: "diplomacy.propose-trade rejected
    /// (diplomacy.target-trade-cap-reached): 'Carthage' already trades with 3 nations." reads
    /// "'Carthage' already trades with 3 nations."). Every other line is returned unchanged (the composed
    /// declaration, or a kind with no wording).
    /// </summary>
    public static string ReadableLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (RefusalMessage(line) is { } refusal)
        {
            return refusal;
        }

        if (!line.EndsWith(AcceptanceSuffix, StringComparison.Ordinal))
        {
            return line;
        }

        var kind = line[..^AcceptanceSuffix.Length];
        return For(kind) ?? line;
    }

    private const string RejectedMarker = " rejected (";

    /// <summary>
    /// #1280 F3: the plain message of a <c>"{kind} rejected ({code}): {message}"</c> line, its first letter
    /// capitalised, or <see langword="null"/> for any other line (or one whose message is empty).
    /// </summary>
    public static string? RefusalMessage(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var marker = line.IndexOf(RejectedMarker, StringComparison.Ordinal);
        // The refusing command: a namespaced kind ("diplomacy.propose-trade") or the session's own Move/Purchase.
        var kind = marker > 0 ? line[..marker] : string.Empty;
        if (kind.Contains(' ', StringComparison.Ordinal) || !(kind.Contains('.', StringComparison.Ordinal) || kind is "Move" or "Purchase"))
        {
            return null;
        }

        var close = line.IndexOf("): ", marker, StringComparison.Ordinal);
        if (close < 0)
        {
            return null;
        }

        var message = line[(close + 3)..].Trim();
        if (message.Length == 0)
        {
            return null;
        }

        var first = message.IndexOf(message.FirstOrDefault(char.IsLetter));
        return first < 0 ? message : message[..first] + char.ToUpperInvariant(message[first]) + message[(first + 1)..];
    }

    /// <summary>
    /// Applies <see cref="ReadableLine"/> to each newline-separated line of an outcome block. The
    /// engine's composed <c>accepted (…)</c> lines are untouched.
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

    /// <summary>
    /// Bug #914: <see cref="Apply(string)"/>, but a Build fleet acceptance is worded with the build city's
    /// display name, read from the order just placed (the last fleet <paramref name="state"/> holds).
    /// </summary>
    public static string Apply(string block, GameState? state)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (state is null || block.Length == 0)
        {
            return Apply(block);
        }

        var orderLine = OrderFleetKind + AcceptanceSuffix;
        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i] == orderLine ? BuildFleetLine(state) : ReadableLine(lines[i]);
        }

        return string.Join('\n', lines);
    }

    /// <summary>"The fleet will be built at &lt;city&gt;." for the newest fleet's build city.</summary>
    public static string BuildFleetLine(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var buildCity = state.Fleets.Count > 0 ? state.Fleets[state.Fleets.Count - 1].BuildCityId : null;
        var city = buildCity is null ? null : state.CityById(buildCity);
        return city is null ? ByKind[OrderFleetKind] : $"The fleet will be built at {city.Name}.";
    }
}
