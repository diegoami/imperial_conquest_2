using IC2.Engine.Core;

namespace IC2.Engine.Diplomacy;

// The confirmed diplomacy news literals (docs/task-catalogue.md T19, Hazards) — every one a vehicle for
// News.NewsLogWriter.Append, never a second path into Model.GameState.NewsLog. WarDeclared is
// deliberately never published through an IEventSink: see its own remarks for why it is rendered by
// hand instead of through the standard per-event pipeline.
//
// Every template these events resolve against already exists in News.NewsMessageCatalog, added by T42
// in anticipation of this task (news-log-format-and-messages.md Q4). None of the kind strings below are
// new to the catalog; declaring the events here (rather than in src/IC2.Engine/News/**, which this task
// does not own) is what docs/build-process.md §2.5 calls "emission, and the literal, stay with the task
// that owns the mechanic".

/// <summary>
/// <c>newsMessage.formsAlliance</c> — <c>"&lt;A&gt; forms an alliance with &lt;B&gt;."</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md, news-log-format-and-messages.md Q4 #1]</strong>.
/// Never uppercased (alliances are never shouted).
/// </summary>
/// <param name="A">The nation forming the alliance (the proposer).</param>
/// <param name="B">The nation it allies with.</param>
[DomainEvent("alliance.formed", NewsWorthy = true)]
public sealed record AllianceFormed(string A, string B) : DomainEvent;

/// <summary>
/// <c>newsMessage.declaresWar</c> — <c>"&lt;A&gt; declares war on &lt;B&gt;."</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md, news-log-format-and-messages.md Q4 #2]</strong>.
/// </summary>
/// <remarks>
/// <strong>Never published through an <see cref="IEventSink"/>.</strong> A war declaration involving a
/// human is uppercased in full (ASCII <c>a-z</c> only) — the whole rendered line, template text
/// included, not just the two names — and <see cref="News.NewsLogWriter.Append"/>'s per-event
/// pipeline has no per-kind post-processing hook to apply that (confirmed by reading the whole file:
/// its only special case is <see cref="News.NewsMessageCatalog.IsWrappedInDashLines"/>'s dash-wrap).
/// <see cref="News.NewsLogWriter.ApplyWarDeclarationShouting"/>'s own doc names this task as the caller
/// that applies it to "an already-rendered war.declared message" — which is only possible for a caller
/// that renders the message itself, before it reaches the log. So every war declaration this task
/// raises — the direct command and every cascade step — is written by
/// <see cref="RelationTransitions.DeclareWar"/> calling <see cref="News.NewsLogWriter.Append"/> directly,
/// with a per-call <c>templateFor</c> override that supplies the already-shouted text as a
/// placeholder-free "template". This type exists only to carry that text through
/// <see cref="News.NewsLogWriter.Append"/>'s <see cref="DomainEvent.IsNewsWorthy"/> gate; nothing ever
/// publishes an instance of it to <see cref="Core.SystemContext.Events"/> or
/// <see cref="CommandContext.Events"/>, so it can never be rendered a second time by
/// <c>NewsLogWriterSeatEnd</c>/<c>NewsLogWriterRoundEnd</c>'s automatic per-event pass.
/// </remarks>
/// <param name="A">The nation declaring war.</param>
/// <param name="B">The nation war is declared on.</param>
[DomainEvent("war.declared", NewsWorthy = true)]
public sealed record WarDeclared(string A, string B) : DomainEvent;

/// <summary>
/// <c>newsMessage.honourablePeace</c> — <c>"&lt;winner&gt; and &lt;loser&gt; have agreed to end their war."</c>
/// Fires when DoD 8's honourable-peace gate passes: no reparations paid.
/// </summary>
[DomainEvent("peace.honourable", NewsWorthy = true)]
public sealed record PeaceHonourableAgreed(string Winner, string Loser) : DomainEvent;

/// <summary><c>newsMessage.suesForPeace</c> — <c>"&lt;loser&gt; sues &lt;winner&gt; for peace and;"</c>.</summary>
[DomainEvent("peace.sues-for", NewsWorthy = true)]
public sealed record PeaceSuedFor(string Loser, string Winner) : DomainEvent;

/// <summary>
/// <c>newsMessage.endsTradingAgreements</c> — <c>"    &lt;loser&gt; ends all current trading agreements."</c>
/// (leading whitespace is the catalog's own, transcribed exactly).
/// </summary>
[DomainEvent("peace.ends-trading-agreements", NewsWorthy = true)]
public sealed record PeaceEndsTradingAgreements(string Loser) : DomainEvent;

/// <summary>
/// <c>newsMessage.endsAlliances</c> — <c>"    &lt;loser&gt;  ends all current alliances."</c> (double
/// space before "ends" is the catalog's own).
/// </summary>
[DomainEvent("peace.ends-alliances", NewsWorthy = true)]
public sealed record PeaceEndsAlliances(string Loser) : DomainEvent;

/// <summary>
/// <c>newsMessage.paysReparations</c> — <c>"    &lt;loser&gt; pays reparations of &lt;n&gt; talents."</c>.
/// </summary>
/// <param name="Loser">The paying nation's name.</param>
/// <param name="N">
/// The reparations amount, already comma-grouped by <see cref="News.NewsLogWriter.FormatGroupedAmount"/>
/// before this event is built — <see cref="News.NewsLogWriter.RenderMessage"/> substitutes a property's
/// value with a plain <c>ToString()</c> call and applies no formatting of its own to any placeholder, so
/// the grouping has to already be in the string this property holds (confirmed by reading
/// <c>NewsLogWriter.cs</c> in full; its own doc comment on <c>FormatGroupedAmount</c> names this task as
/// the caller for exactly this reason). This corrects the task entry's own hazard note ("pass it as a
/// number, not a preformatted string"), which the shipped renderer's actual behaviour contradicts —
/// <c>docs/build-process.md</c> §4.3's "the evidence wins" principle, applied to a hazard note rather
/// than a Done-when line.
/// </param>
[DomainEvent("peace.pays-reparations", NewsWorthy = true)]
public sealed record PeacePaysReparations(string Loser, string N) : DomainEvent;

/// <summary>
/// <c>newsMessage.allyPeaceAgreement</c> — <c>"&lt;A&gt; and &lt;B&gt; have agreed to end their war."</c>
/// — an ally of either treaty side, still at war with the other, also makes peace (cooldown
/// <see cref="Model.DiplomacyRules.CooldownAfterAllyPeace"/>).
/// </summary>
[DomainEvent("peace.ally-agreement", NewsWorthy = true)]
public sealed record PeaceAllyAgreement(string A, string B) : DomainEvent;

/// <summary>
/// A pending trade or alliance offer was rolled and is being announced to the human seat whose turn just
/// started (DoD 10) — <c>"X wants to trade with Y."</c> / <c>"X wants to form an alliance with Y."</c>,
/// the exact period-terminated literal <c>news-log-format-and-messages.md</c> Q5 confirms
/// (<c>FUN_00452034</c> → <c>TPremierForm_StartTurn</c>'s <c>MessageDlg</c>).
/// </summary>
/// <remarks>
/// <strong>Not news-worthy, on purpose.</strong> Q5 is explicit: <c>StartTurn</c> never calls the news
/// writer, and no news slot in any of 54 saves contains "wants". This is a presentation event only —
/// <see cref="PendingOfferSystem"/> raises it for T23/T25 to show as a modal dialog; this task writes no
/// news line for it.
/// </remarks>
/// <param name="ProposingNationId">The nation the pending offer proposes from.</param>
/// <param name="ProposingNationName">Its name, for the dialog text.</param>
/// <param name="TargetNationId">The human nation the offer is addressed to (the seat whose turn started).</param>
/// <param name="TargetNationName">Its name, for the dialog text.</param>
/// <param name="ProposedRelationCode">
/// The proposed relation, in <see cref="Model.RelationStateCodes"/>' own encoding (trade or alliance).
/// </param>
/// <param name="DialogText">
/// The exact literal the original shows, built by <see cref="PendingOfferSystem"/> from the confirmed
/// Q5 format — period-terminated, word-for-word ("X wants to trade with Y." / "X wants to form an
/// alliance with Y."), distinct from the corpus's own <c>dialog.pendingDiplomaticOfferParaphrase</c>
/// entry, which the same report tags as a paraphrase rather than the dialog's exact text.
/// </param>
[DomainEvent("diplomacy.pending-offer-announced")]
public sealed record PendingDiplomaticOfferAnnounced(
    string ProposingNationId,
    string ProposingNationName,
    string TargetNationId,
    string TargetNationName,
    int ProposedRelationCode,
    string DialogText) : DomainEvent;
