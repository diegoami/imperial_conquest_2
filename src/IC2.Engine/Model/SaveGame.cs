using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// A live, versioned snapshot of a <see cref="Scenario"/> in progress, including which world and
/// ruleset it started from — the fourth file kind in
/// <c>docs/game-design.md</c> §"The core data model".
/// </summary>
/// <remarks>
/// Deliberately carries no timestamp: the engine rules forbid wall-clock reads on any path that ends
/// up in state, so a save is named by its <see cref="Label"/> and ordered by its state's
/// <see cref="CalendarState.TurnIndex"/>, never by <c>DateTime.Now</c>.
/// </remarks>
/// <param name="SchemaVersion">The document contract version.</param>
/// <param name="Id">The save's id.</param>
/// <param name="Label">The save's label.</param>
/// <param name="ScenarioId">The scenario the game started from.</param>
/// <param name="WorldId">The world the game runs on.</param>
/// <param name="RulesetId">The ruleset the game runs under.</param>
/// <param name="State">The game state.</param>
/// <param name="Provenance">Optional provenance notes.</param>
/// <param name="Battle">
/// T125 (save format version 7): the tactical battle that was open when the game was saved, or
/// <see langword="null"/> when none was (the member is then absent from the JSON, so a save made outside a
/// battle is byte-identical to a version-6 save apart from its format version). The clone's own JSON
/// format, never an original <c>.sav</c> (design principle 6).
/// </param>
/// <param name="FirstEndNewsOwed">
/// T137 (no format bump: optional, and its absence means what it meant before): the number of news entries
/// the saving session's log had gained since its construction-time snapshot, written only while that session
/// had not yet had its first <c>end</c>; a resumed session rebuilds the snapshot from the loaded log without its
/// newest this-many entries, so its first <c>end</c> lists them too. <see langword="null"/> (absent from the
/// JSON) for a save made after the first <c>end</c> and for every older save.
/// </param>
public sealed record SaveGame(
    int SchemaVersion,
    string Id,
    string Label,
    string ScenarioId,
    string WorldId,
    string RulesetId,
    GameState State,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SavedTacticalBattle? Battle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FirstEndNewsOwed = null) : IVersionedDocument;

/// <summary>
/// An open tactical battle as a save holds it: the original's battle block (<c>2026-10-04-decompiled-tactical-battle-rules.md</c>
/// §1: the header, the 40 unit slots of 11 fields and the 14 × 12 icon grid), who plays each side, the computer
/// general's claims, the selection, and the position of the battle's own draw stream.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[designed: the JSON shape; confirmed: the block's fields]</strong>. Searched and found empty for the shape:
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §1 gives the original's byte layout (the table of addresses, the 22-word unit slot, the
/// 14 × 12 grid) and <c>2026-10-04-tactical-battle-sweep.md</c> (B2) decodes the same block from crafted saves; both describe the original <c>.sav</c>
/// bytes, and neither says anything about a JSON form. This clone saves its own JSON format, never the original <c>.sav</c> (the original's
/// byte layout belongs to T129), so the member names and nesting are this engine's default, named after the report's fields. The trace (the battle's event log)
/// is not part of the block and is not saved: a reloaded battle's exchange log starts at its reload.
/// </para>
/// <para>
/// <see cref="DrawState"/> is the raw state of the battle's <c>SplitMix64</c> stream (the stream is derived from the
/// game state's seed and the key <c>battle.tactical:&lt;attacker&gt;:&lt;defender&gt;:&lt;turnIndex&gt;</c>), so a reload
/// continues the same draws. <see cref="AtHalfRoundStart"/> is true when no order of the current half-round has been
/// given since its setup; the exact-restore path (T130's switch) reads it, the classical reload never does.
/// </para>
/// </remarks>
/// <param name="AttackerArmyId">The attacking army (side 0).</param>
/// <param name="DefenderArmyId">The defending army (side 1).</param>
/// <param name="SideToMove">0 attacker, 1 defender.</param>
/// <param name="Placed">Whether the placement half-rounds are over.</param>
/// <param name="Counter">The half-round counter.</param>
/// <param name="Slots">The 40 unit slots, attacker's first.</param>
/// <param name="Grid">The 14 × 12 icon codes, indexed <c>x × boardHeight + y</c>.</param>
/// <param name="ComputerControlled">Per side, whether its nation is computer-controlled.</param>
/// <param name="ComputerGeneral">Per side, whether <em>Computer general</em> is on.</param>
/// <param name="GeneralClaims">The computer general's 40 target claims.</param>
/// <param name="Selected">The selected slot, or <c>−1</c>.</param>
/// <param name="DrawState">The raw state of the battle's draw stream.</param>
/// <param name="AtHalfRoundStart">Whether no order of the current half-round had been given.</param>
public sealed record SavedTacticalBattle(
    string AttackerArmyId,
    string DefenderArmyId,
    int SideToMove,
    bool Placed,
    int Counter,
    ValueList<SavedTacticalSlot> Slots,
    ValueList<int> Grid,
    ValueList<bool> ComputerControlled,
    ValueList<bool> ComputerGeneral,
    ValueList<int> GeneralClaims,
    int Selected,
    ulong DrawState,
    bool AtHalfRoundStart);

/// <summary>One of a saved battle's unit slots: the report's 11 fields (§1, "Unit slot").</summary>
/// <param name="X">Column.</param>
/// <param name="Y">Row.</param>
/// <param name="Origin">The origin label (0 national, non-zero mercenary).</param>
/// <param name="Type">The unit type, as the index into the ruleset's unit types.</param>
/// <param name="Troops">Troops; 0 means the slot is out of the battle.</param>
/// <param name="Quality">Quality.</param>
/// <param name="Morale">The battle morale.</param>
/// <param name="Moves">Moves left this half-round.</param>
/// <param name="Shots">Shots left for the battle.</param>
/// <param name="Target">The melee target slot, or −1.</param>
/// <param name="Name">The unit's name.</param>
public sealed record SavedTacticalSlot(
    int X,
    int Y,
    int Origin,
    int Type,
    int Troops,
    int Quality,
    int Morale,
    int Moves,
    int Shots,
    int Target,
    string Name);
