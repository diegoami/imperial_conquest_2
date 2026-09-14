using System.Collections.Frozen;

namespace IC2.Engine.News;

/// <summary>
/// Maps domain event kinds to news message templates. Message templates use curly braces to mark
/// operands, which the writer substitutes from event properties.
/// </summary>
/// <remarks>
/// Every news-worthy domain event kind declared in the engine must have an entry in this catalog,
/// asserted by T10's coverage test. Each system that declares a news-worthy event is responsible
/// for providing the template in its own task's DoD.
/// </remarks>
public static class NewsMessageCatalog
{
    /// <summary>
    /// Maps event kind (the kind property of a DomainEvent) to a message template.
    /// </summary>
    private static readonly FrozenDictionary<string, string> Templates = new Dictionary<string, string>
    {
        // Confirmed message templates from the fixtures corpus (decompiled-city-capture-resolution.md)
        { "city.falls-to", "{CityName}   ({OldOwner})  falls to {NewOwner}." },
        { "city.fails-to-capture", "{AttackerNation} fails to capture {CityName}   ({DefenderNation})." },

        // Defection message (galatia-elimination-and-city-resupply-confirmed.md)
        { "city.defects-to", "{CityName} defects from {OldOwner} to {NewOwner}." },

        // Nation elimination (galatia-elimination-and-city-resupply-confirmed.md)
        { "nation.conquered", "{ConqueringNation} conquers {ConqueredNation}." },

        // Battle outcomes (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "battle.army-destroyed", "{WinnerNation} destroys army of {LoserNation}." },
        { "battle.fleet-sunk", "{WinnerNation} sinks fleet of {LoserNation}." },

        // Peace treaties (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "peace.honourable", "{WinnerNation} and {LoserNation} have agreed to end their war." },
        { "peace.sues-for", "{LoserNation} sues {WinnerNation} for peace and;" },
        { "peace.ends-trading-agreements", "    {LoserNation} ends all current trading agreements." },
        { "peace.ends-alliances", "    {LoserNation}  ends all current alliances." },
        { "peace.pays-reparations", "    {LoserNation} pays reparations of {ReparationAmount} talents." },
        { "peace.ally-agreement", "{NationA} and {NationB} have agreed to end their war." },

        // Fleet construction (decompiled-unit-map-orders-and-record-fields.md)
        { "fleet.finished", "{Nation} finishes a new fleet at {CityName}" },

        // Fleet lost at sea (fleet-owner-field-confirmed.md, weather-event system)
        { "fleet.lost-at-sea", "A fleet belonging to {Nation} is lost at sea" },

        // Victory messages (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "victory.all-cities", "You have conquerred the Mediterranean, a unique achievement." },
        { "victory.conquered-by-nation", "Your nation has been conquerred by {ConqueringNation}." },

        // Diplomatic offers (decompiled-turn-and-calendar-sequencing.md, paraphrased)
        { "diplomacy.pending-offer", "{OfferingNation} wants to trade/form an alliance with {ReceivingNation}" },
    }.ToFrozenDictionary();

    /// <summary>
    /// Gets the message template for an event kind, or throws if the kind is not registered.
    /// </summary>
    /// <param name="eventKind">The event's stable kind string.</param>
    /// <returns>The message template with placeholder operands like {CityName}.</returns>
    /// <exception cref="KeyNotFoundException">The event kind has no catalog entry.</exception>
    public static string GetTemplate(string eventKind)
    {
        ArgumentNullException.ThrowIfNull(eventKind);

        if (!Templates.TryGetValue(eventKind, out var template))
        {
            throw new KeyNotFoundException(
                $"Event kind '{eventKind}' has no news message template registered. "
                + "T10's coverage test should have caught this when the event was declared.");
        }

        return template;
    }

    /// <summary>
    /// Whether an event kind has a registered message template.
    /// </summary>
    public static bool HasTemplate(string eventKind) => Templates.ContainsKey(eventKind);

    /// <summary>
    /// All registered event kinds, sorted for deterministic iteration.
    /// </summary>
    public static IEnumerable<string> RegisteredKinds => Templates.Keys.OrderBy(k => k, StringComparer.Ordinal);
}
