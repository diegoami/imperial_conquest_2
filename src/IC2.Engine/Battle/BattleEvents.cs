using IC2.Engine.Core;

namespace IC2.Engine.Battle;

/// <summary>
/// A field battle annihilated the loser's army — <c>FUN_0044AEE4</c>'s
/// <c>news("&lt;winner&gt; destroys army of &lt;loser&gt;.")</c>. Matches the news catalog's
/// already-registered <c>battle.army-destroyed</c> template, whose two placeholders
/// <c>&lt;winner&gt;</c>/<c>&lt;loser&gt;</c> resolve to this record's own two properties.
/// </summary>
/// <remarks>
/// Published only when the loser is actually destroyed. Under the <c>improved</c> ruleset's
/// <see cref="Model.DefeatOutcome.Scatter"/> the army survives, so this message would be false and
/// <see cref="ArmyScattered"/> is published instead — except in the two documented fallbacks (nothing
/// survived the mirrored casualties, or the survivor was fully boxed in), where the outcome genuinely
/// is destruction and this event is correct again.
/// </remarks>
/// <param name="Winner">The winning nation's name, as the news line prints it.</param>
/// <param name="Loser">The losing nation's name.</param>
[DomainEvent("battle.army-destroyed", NewsWorthy = true)]
public sealed record BattleArmyDestroyed(string Winner, string Loser) : DomainEvent;

/// <summary>
/// A naval battle sank the loser's fleet — <c>FUN_0044B5D0</c>'s
/// <c>news("&lt;winner&gt; sinks fleet of &lt;loser&gt;.")</c>, DoD 9. Matches the news catalog's
/// already-registered <c>battle.fleet-sunk</c> template.
/// </summary>
/// <param name="Winner">The winning nation's name.</param>
/// <param name="Loser">The losing nation's name.</param>
[DomainEvent("battle.fleet-sunk", NewsWorthy = true)]
public sealed record BattleFleetSunk(string Winner, string Loser) : DomainEvent;

/// <summary>
/// Every battle publishes this, whatever the variant and whatever the loser's fate: the machine-readable
/// companion to the news line, carrying the whole <see cref="BattleResult"/> for the UI and for any
/// system that wants to react to a battle without re-deriving it.
/// </summary>
/// <remarks>
/// Not news-worthy: the confirmed news lines are <see cref="BattleArmyDestroyed"/> and
/// <see cref="BattleFleetSunk"/>, and a second rendered line per battle would not match the original's
/// log. <c>docs/build-process.md</c> §2.5 keeps the message literal with the task that owns the
/// mechanic, and the original has no literal for "a battle happened" as such.
/// </remarks>
/// <param name="Result">The full result.</param>
[DomainEvent("battle.resolved")]
public sealed record BattleResolved(BattleResult Result) : DomainEvent;

/// <summary>
/// The <c>improved</c> ruleset's alternative to annihilation fired: the losing army survived at reduced
/// strength and was relocated (DoD 10).
/// </summary>
/// <remarks>
/// Not news-worthy. The original has no partial-defeat outcome and therefore no news literal for one
/// (<c>design-audit.md</c> Q1 follow-up, Q10), and this task may not invent one — inventing a template
/// here would put a <c>[designed]</c> sentence into a catalog whose entries are all transcriptions.
/// T24's battle-result screen reads <see cref="BattleResult.LoserFate"/> and
/// <see cref="BattleResult.Scatter"/> instead, which is what <c>docs/task-catalogue.md</c> T24 asks it
/// to do ("present whichever <c>BattleResult</c> actually reports, not a hardcoded 'destroyed' string").
/// </remarks>
/// <param name="ArmyId">The scattered army.</param>
/// <param name="NationId">Its nation.</param>
/// <param name="ToX">The tile it was relocated to.</param>
/// <param name="ToY">See <paramref name="ToX"/>.</param>
[DomainEvent("battle.army-scattered")]
public sealed record ArmyScattered(string ArmyId, string NationId, int ToX, int ToY) : DomainEvent;

/// <summary>The naval twin of <see cref="ArmyScattered"/> (DoD 10).</summary>
/// <param name="FleetId">The scattered fleet.</param>
/// <param name="NationId">Its nation.</param>
/// <param name="ToX">The sea tile it was relocated to.</param>
/// <param name="ToY">See <paramref name="ToX"/>.</param>
[DomainEvent("battle.fleet-scattered")]
public sealed record FleetScattered(string FleetId, string NationId, int ToX, int ToY) : DomainEvent;

/// <summary>
/// The automatic post-battle peace treaty fired — <c>FUN_0044AEE4</c>'s
/// <c>if (random(5) &lt; 2 &amp;&amp; unity[loser] &gt; 500 &amp;&amp; cities[loser] &gt; 7)
/// FUN_00450C68(winnerNation, loserNation)</c>, DoD 8.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An event, not a call.</strong> The original calls the treaty routine directly. This task
/// publishes instead, so T16 and T19 do not depend on each other's internals
/// (<c>docs/task-catalogue.md</c> T16 Scope, T19 Scope). T19 subscribes and runs the confirmed terms —
/// ending trade agreements and alliances, the honourable-peace branch, the reparations formula and their
/// news lines, none of which live here. The DoD requires this to be observable "with no diplomacy system
/// registered", which publication trivially gives and a direct call could not.
/// </para>
/// <para>
/// Not news-worthy: the peace lines the original prints (<c>peace.sues-for</c>,
/// <c>peace.ends-trading-agreements</c>, <c>peace.ends-alliances</c>, <c>peace.pays-reparations</c>,
/// <c>peace.honourable</c>) belong to the treaty routine — which branch fires, and the reparation
/// amount, are decided there. Rendering one from here would be guessing at T19's outcome.
/// </para>
/// </remarks>
/// <param name="WinnerNationId">The victorious nation, the treaty's first argument.</param>
/// <param name="LoserNationId">The defeated nation.</param>
/// <param name="LoserUnity">The loser's unity at the moment the gate was tested, after the battle's own swing.</param>
/// <param name="LoserCityCount">The loser's city count at the same moment.</param>
[DomainEvent("battle.peace-treaty-triggered")]
public sealed record PeaceTreatyTriggered(
    string WinnerNationId,
    string LoserNationId,
    int LoserUnity,
    int LoserCityCount) : DomainEvent;

/// <summary>
/// A post-battle peace treaty was <em>offered</em>, pending the human's own Yes or No — T88's fix for
/// bug #384. Published instead of <see cref="PeaceTreatyTriggered"/> whenever exactly one side of the
/// battle is human: the original's own gate for this case is stricter, and its outcome is never applied
/// without the human answering the <c>TBattlePols</c> dialog
/// <strong>[confirmed: decompiled-war-cascade-and-peace-paths.md §2.3]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The four gates, human-involved order.</strong> Unlike the AI-vs-AI gate behind
/// <see cref="PeaceTreatyTriggered"/> (draw first, then unity and cities), a human-involved battle tests,
/// in order, <c>armies(winner) &lt; armies(loser)</c>, <c>unity(loser) &gt; 500</c>,
/// <c>cities(loser) &gt; 7</c>, and only then draws <c>Random(5) &lt; 2</c> — the draw is never taken if
/// an earlier gate fails (report §2.3, <c>0x004594DC</c>–<c>9524</c>).
/// </para>
/// <para>
/// <strong>An event, not a call — and never auto-applied.</strong> <c>PeaceTreatySystem</c> reacts only
/// to <see cref="PeaceTreatyTriggered"/>, never to this event, so publishing this alone writes no
/// relation. <see cref="IC2.Engine.Presentation.GameSession"/> is where the pending decision actually
/// lives and where Yes/No resolve it — see that type's own remarks for the design chosen (Battle must not
/// reference Diplomacy or Presentation, so this event is deliberately data-only: the two nation ids, and
/// nothing GameSession would otherwise have to re-derive from a snapshot of the battle).
/// </para>
/// <para>
/// Not news-worthy: no news is written until the human answers, exactly like
/// <see cref="PeaceTreatyTriggered"/> itself.
/// </para>
/// </remarks>
/// <param name="WinnerNationId">The battle's winner — the treaty's first argument if accepted.</param>
/// <param name="LoserNationId">The battle's loser.</param>
[DomainEvent("battle.peace-treaty-offered")]
public sealed record PeaceTreatyOffered(
    string WinnerNationId,
    string LoserNationId) : DomainEvent;
