# Manual restrictions and GUI evidence

This intake evaluates original-behavior research against remake commit
`4d70275aac4e155c0370a3ee23525cf34450a908`. It defines evidence and correction
boundaries; it does not claim the corresponding gameplay changes have shipped.
Issue labels carry implementation status.

Research revisions: [nine-report promotion, PR86](https://github.com/diegoami/imperial-conquest-2-research/pull/86)
at `24e93f330a634542e68343a069f1585db6e5b117`,
[selection correction, PR87](https://github.com/diegoami/imperial-conquest-2-research/pull/87)
at `4813a50991fc06f551f7bdc9a2c4d5ef99640c69`, and
[carried-disband promotion, PR88](https://github.com/diegoami/imperial-conquest-2-research/pull/88)
at `1f40a33fe9639172d9c28bdba7a8d96db64b240e`. The last revision also qualifies
the earlier assumption sweep's carried-disband reachability conclusion.

## What the evidence establishes

The links below pin the promoted reports at research main
`77d89876c89570962c7dc75f718a8f88774177f9`. Observations are **stock-original
Wine controls on synthetic private copies**, independently reparsed by the
research session. Code readings and unplayed branches remain separate.

| Original caller | Established behavior | Remake correction boundary |
| --- | --- | --- |
| [Paid Supply](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-signed-paid-supply85.md) | Floor the changed staged amount at zero **before** clamping by stock, signed room and purse. Negative room can produce a negative final amount. Buy applies it immediately, refunds with truncation toward zero, then resets staging. Close before Buy discards staging; Close after Buy retains the purchase. | [#1486](https://github.com/diegoami/imperial_conquest_2/issues/1486): final UI flooring suppresses this return. Resolve model, command and helper together; a negative public `buy` request is not currently accepted. |
| [Army money arrows and unchanged OK](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-army-money-and-unchanged-ok85.md) | `min(request, 1000 - receiver, giver)` is signed on working copies. Donor100/receiver1100 becomes200/1000; repeated press changes nothing. Split uses the same handler. No-edit OK unconditionally pushes supply excess A to B, then B to A; Cancel discards copies. | [#1487](https://github.com/diegoami/imperial_conquest_2/issues/1487) and [#1491](https://github.com/diegoami/imperial_conquest_2/issues/1491): neither a nonnegative arrow limit nor an unstaged/no-op guard represents these rules. |
| [Army unit transfer](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-partial-army-transfer85.md) | Source slots19 down to0 are tested individually. Units that fit move; failures do not stop later units. Append at destination occupied count, compact donor, reset only the recipient's working moves. Refusal summaries can contain **two** boxes: unit-count or troop-limit, then independently fleet-capacity. OK/Cancel commit/discard working copies. | [#1488](https://github.com/diegoami/imperial_conquest_2/issues/1488): aggregate rejection is different. Do not reduce a partially accepted selection to a total rejection or promise only one refusal. |
| [Transfer capacity](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-partial-army-transfer85.md) and [human embark/hire](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-human-transport-rounding83.md) | These separately tested human callers compare ships against integer-divided troop totals. Thirty ships accept15499/refuse15500 on transfer; ten accept5499/refuse5500 on embark/hire. Hire's opening gate and individual-offer gate are separate. | [#1489](https://github.com/diegoami/imperial_conquest_2/issues/1489), [#1490](https://github.com/diegoami/imperial_conquest_2/issues/1490). Nominal `ships * 500` is not their exact admission predicate. Do not generalize to T216 AI overflow allocation. |
| [Partial fleet money](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-manual-family-assumption-sweep85.md) | Money arrows cap by signed receiver room on copies. Donor200/receiver950 transfers50; receiver1100 returns100. No treasury diversion. A ship-only partial OK leaves receiver1100 untouched. | [#1326](https://github.com/diegoami/imperial_conquest_2/issues/1326): the cap is an arrow operation, not an unconditional partial-OK normalization. Preserve the separate empty-fleet pooling rule. |
| [Supply through selected carrier](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-carried-army-supply-gui79.md) | Supply army resolves the cargo and searches its coordinates; the carrier may supply its own cargo. Supply fleet instead excludes itself. Army-target supply/funding applies at arrows and persists after Close. | Cargo target resolution is supported, not a new defect. Keep selected provider/account routing distinct from the extra remake money-source picker and the already separate fleet-target treasury rule. |
| [Tactical enemy inspection](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-tactical-enemy-click-gui80.md) | Left enemy click can inspect without selecting the enemy for commands. Ineligible distant/zero-move attacks inspect and clear friendly selection; an eligible adjacent click sets a target. Enemy information remains nation/name/type/troops. Right-own clears its target; Cancel selection keeps an assigned target. | [#1492](https://github.com/diegoami/imperial_conquest_2/issues/1492). Separate inspection from command selection; scene-level correction still needs scene-level checks. No hidden-enemy-field leak established. |

For no-edit OK, two10000-troop armies with supplies206/0 become106/100 when
the first is selected, or100/106 when the second is selected. Both-over-cap
206/150 becomes256/100 or100/256 respectively. These are deliberate
discriminating controls, not rounding examples from ordinary turn consumption.

## Original quirks and explicit remake policy

[Provider research78](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-supply-provider-gui78.md)
establishes a physical x-then-y scan, at most two retained cities and three
fleets, and stock ordering of the cities. It also finds skipped/duplicate/invalid
fleet row bindings. Switching a paid provider retains the old staged amount;
Buy does not reclamp it, and can leave the newly selected city's stock negative.
The remake's uncapped state-order enumeration and resetting staging on a provider
change must not be called original-confirmed. Its extra army providers and money
picker are designed extensions. This intake does not authorize invalid bindings,
negative-stock exploitation or removal of those extensions. Any exact-parity
change to that UI needs an explicit design decision.

[Recruitment research81](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-recruitment-default-gui81.md)
corrects the earlier broad default claim: choose capital when queue slot0 is
empty; otherwise maximize occupied queued-unit count plus one for capital,
retaining the first city-list entry on ties. Adding All cities does not replace
the chosen city. The no-queue case and ordinary packed-queue cases are observed;
the gapped slot0-empty/later-occupied case is code-derived. T162's owner-requested
All cities opening remains an explicit remake UX policy, not an original rule.

The independent arbitrary-command `transfer-money` limit of1000 is a CLI/model
policy. Original10/100 arrows and a receiver cap do not establish that separate
command ceiling. No player-reachable failure was established for that ceiling.
Original desertion deletion clears a valid carrier link by its source call
chain; that particular quarter-boundary runtime case remains unplayed.

## Carried-army disband decision

[Research218](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-26-carried-army-disband218.md)
shows why the earlier direct-menu no-op was insufficient to prove unreachability.
With no mercenary offer, the recruitment action temporarily binds the carried
army until repaint. Cargo Disband before repaint opens a real confirmation.
Yes deletes the army, clears the carrier link, refunds the purse to treasury
and supplies to the nearby own city, with mobilisation unchanged. No and an
intervening map pan are negative controls. Configured input delays do not measure
a maximum timing window.

**Owner decision, 2026-10-26:** Faithful should support whole-army disband of
cargo near an owned town through a clear command, with refunds and safe carrier
unlinking, while preserving the separate last-unit safeguard. The implementation
contract is [#1493](https://github.com/diegoami/imperial_conquest_2/issues/1493).
This decision does not require emulating the original timing/selection bug.

Change units' final-unit removal can instead leave an empty live cargo record
in the original, surviving reload; this does not revoke the owner's last-unit
refusal. Disband inside Army transfer has another contract: final OK deletes
empty cargo and pools stores into its partner, rather than treasury/city refunds.
Neither result implies a later End turn or full lifecycle equivalence.

## AI overflow receiver context and raw lineage

[Research89](https://github.com/diegoami/imperial-conquest-2-research/pull/89),
report revision `da01d29eb7d8c01eb507d173174a13c76d0760f3`, promotes
[conditional original-instruction and native-command evidence](https://github.com/diegoami/imperial-conquest-2-research/blob/dc851f5267f7d8c4a887f066204c1355480844aa/docs/reports/2026-10-26-fleet-overflow-receiver174.md).
Unlike the stock-Wine GUI controls above, this evidence uses retained original
instruction traces and diagnostic RAM-to-SAV projections, not original Save As
outputs or a newly played original session. Research independently audited the
retained artifacts and freshly replayed the unchanged native command probe.

Original B79C vacates the source land cell, snaps the army to the fleet, zeros
moves, then calls F8FC. Receiver search uses that post-board context. Transfer
also zeros the receiver's initial AI move. The remake instead queries placement
on the pre-board land army and leaves the receiver with one move. With source
(39,34), fleet (40,33), ten ships and units4000/2000, the observed original
receiver is (40,34)/moves0; accepted native receiver is (40,35)/moves1.
Diagnostic post-board native queries match every successful original receiver
coordinate. This supports the narrow follow-up [#1495](https://github.com/diegoami/imperial_conquest_2/issues/1495),
not a change to unrelated split placement or a tested production fix.

The16 labels represent13 distinct inputs. Seven position/moves-only discrepancy
rows cover four inputs; an all-overflow row makes eight discrepancies over five
inputs and has an additional resource-policy difference. Occupied own/enemy
armies remain untouched; the original creates a new receiver. A wrong accepted
receiver is demonstrated, but the separate false-no-receiver-refusal control
was not run and must not be inferred.

The original raw198 branch deletes overflow and writes resource words through
receiver index-1 into a city name. Keep T216's owner-approved atomic refusal.
In all-overflow, AB90 tombstones source0 and unlinks its carrier; ABE0 is not
called and raw count remains2. Receiver1 holds33money/30supply, while dead
source0 retains67/60. The remake's100/90 on its live replacement preserves
resources by policy. Neither original data corruption nor resource loss is an
automatic fidelity correction. Raw tombstones, imported live models and actual
original compaction are different; [#1439](https://github.com/diegoami/imperial_conquest_2/issues/1439)
retains the ordering/lineage boundary.

Research checked255 retained closure files,32 endpoints,32 traces,30 placement
queries and15 fresh native full-state outcomes. Its fresh private closure has
257 files because of added audit receipts; scientific fields and rows agree.
This is not fresh original instruction execution, an actual AI caller history,
whole-turn/RNG parity, human UI, Godot, Windows or export/reimport acceptance.
Closed T216's whole-unit conservation fix and its explicit safety policies are
not reopened by this narrower placement/moves finding.

## Selection-rendering correction

[Research87's selection report](https://github.com/diegoami/imperial-conquest-2-research/blob/77d89876c89570962c7dc75f718a8f88774177f9/docs/reports/2026-10-12-unit-map-selection-rendering.md)
supports a bright yellow army/fleet rhombus in retained patched-Wine screenshots.
Its98 changed pixels lie in the horizontal scrollbar, not the selected tiles;
the tile comparisons have zero changed pixels. This does not establish a300ms
timer or600ms blink. The town stimulus did not demonstrate the claimed frame.
Do not derive exact vectors, independent marker recolouring or current remake
appearance from these captures. [#1010](https://github.com/diegoami/imperial_conquest_2/issues/1010)
retains the remaining original-selection questions separately from T196 feedback.

## Verification limits

Research reaudited102 current pairs, six historical patched-Wine pairs and three
supporting pilots for the nine-report packet, and separately29 pairs/52 unique
SAVs for carried disband. Original private artifacts were not reparsed in this
build-session intake. Its evaluation reads the promoted reports, safe scalar
receipts, historical decisions and production source. Research's unchanged
native probes establish model/helper/dispatcher differences, not installed
Godot or native Windows acceptance.

Earlier paid tests had mismatched city markers; two coherent owner/marker
repeats establish proper foreign entry. Tactical load resets staged zero moves;
genuine-zero controls use actual shots. Failed multirow selections, invalid
provider rows and stale cargo stimuli are not successful natural controls.

No original/private bytes, screenshots or private hashes are copied here.
No release gate, owner-approved no-wrap policy, fleet-transfer safety exception,
automatic funding reserve, Improved affordability or T216 safe refusal changes
through this intake. The released v0.8.2 tag remains immutable.
