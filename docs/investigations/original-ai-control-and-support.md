# Original AI control, regions and automatic support

This is the build-side reading of research commit `500e521`, especially
[Computer General: control predicate, setup timing, interruption and reload](https://github.com/diegoami/imperial-conquest-2-research/blob/500e521/docs/reports/2026-10-16-computer-general-predicate-and-reload.md)
and [Strategic AI: region boundaries, embarked-army resupply and fleet purse/repair](https://github.com/diegoami/imperial-conquest-2-research/blob/500e521/docs/reports/2026-10-16-strategic-ai-regions-resupply.md).
It also incorporates the changed ranges of the tactical rules report
(`ef19e2c..500e521`), tactical sweep (`87d1571..500e521`) and strategic turn
(`18edd81..500e521`). The reports correct original-rule claims; they do not
establish that the remake implements them.

## Evidence and limits

**[confirmed: code]** below means a read of the pinned original decompile or
instructions, not confirmation of an outcome on Windows. **[x86]** is upstream
execution of original instructions under Unicorn. **[Wine candidate]** means
execution of the original under Wine; the strategic cases use explicitly
staged save fields. Native Windows confirmation remains required before a
Wine-only outcome settles a rule ([evidence pipeline](../evidence-pipeline.md#the-stages)).

Both reports pin `all_app_functions.txt` SHA-256
`2018c205a4e4ea4a4079f2d166623fb571062832d76e19b72e11d1f0edac87d8`
and upstream commit `caf2a17d420cfca35757fad60311cae9514af257`
([upstream PR 88](https://github.com/diegoami/ic2-conquest/pull/88)). Their
research intake verified 257 artifact hashes, independently decoded 28
supply/fleet cases and 109 tactical boundaries, and verified seven region
plain/observer post-save pairs byte-identical. It did **not** independently
replay observer streams, x86 tests or UI input timelines. This evaluation
reads those promoted reports and production code; it does not repeat the
artifact checks or original executions. Save filenames resolve through the
[pinned evidence index](https://github.com/diegoami/imperial-conquest-2-research/blob/500e521/docs/evidence-index.md#original-ai-probes-2026-10-16).

## Tactical control and reload

**[confirmed: code; x86 corroboration]** `FUN_00438378` (source 38271) is true
when the active nation is computer-controlled **or the active side's general
flag is on**. The other side's general flag does not count. The upstream
instruction test covers all 16 human/side/general-bit combinations. Setup
`FUN_00439968`, Walk `FUN_00438d24`, its step helper `FUN_00438a6c` and
shooting `FUN_0043910c` consume this predicate. Copy-in `FUN_00437de4` keeps
the strategic morale +3 and sorting **nation-only**; the new resume probes
do not execute copy-in and do not independently confirm those effects in play.

**[confirmed: code]** Setup clears active targets, assigns type moves, then
uses one move for non-heavy-infantry units if the control predicate is true,
the minimum live own/enemy distance is greater than 2, and the counter is
**less than 10 before its increment** (source 39189–39285, especially
39257–39279). Placement has no separate exemption. Turning general on after
setup does not rerun that setup. Full placement moves followed by early slow
movement therefore do not imply a placement-specific control rule.

**[confirmed: code]** `TBattleMap_ComputerGeneral` at `0x437ab8` enables the
active side; it is not a symmetric toggle. The general polls Information-window
mouse-down events in `FUN_0043a31c` and `FUN_0043abb4` (39835–39838,
40254–40257). A human-side interruption clears its general flag and sets
`0x45e6a7`; an AI nation's predicate remains true. Existing move allowances
are not recomputed by interrupting or enabling the general.

**[confirmed: code]** `TBattleMap_StartBattle` runs copy-in or resumed setup
**before clearing both runtime general flags** (37447–37457). Those flags are
not serialized in the original 2,105-byte battle block. A same-process reload
can therefore use an old general flag for its setup, then return with flags
cleared; a fresh process starts without that flag. A resume's behavior depends
on this runtime context as well as the saved block.

**[Wine candidate]** The original `gate2_a_BATTLE02.SAV` / `...03.SAV` pair
(turn 0743) retains full human placement moves followed by an archer advancing
one cell under the general. The controlled fresh-process human-off, human-on
and AI-nation cases distinguish the predicate and setup timing. A real UI
interruption returns control only after its queued click is consumed, not
necessarily before the first unit moves. In the same-process reload probe,
`gen_reload_on-20261016-152304-549608-BATTLE27.SAV` has counter 3 and moves
HI/Ar/HC/LI = 2/1/1/1; the following manual End turn reaches counter 4 and
2/4/5/4 with generals off. These are Wine observations, not native Windows proof.

The raw draw stream splits at explicit lab-start reseeds to 1; a cross-start
seed discontinuity is not an unexplained target draw. Off, on, interrupted
and re-enabled runs are different input sequences. These findings do not
resolve the 318-log targeting investigation. At the October 16 evaluation
checkpoint, its comparison required a **reviewed, integrated control correction**
and the copy-in/exhausted-move corrections, as recorded by [build issue 1107](https://github.com/diegoami/imperial_conquest_2/issues/1107)
and [research issue 25](https://github.com/diegoami/imperial-conquest-2-research/issues/25).
Report promotion alone did not clear that gate. [Build issue 1104](https://github.com/diegoami/imperial_conquest_2/issues/1104)
records the control correction. The later [target/draw investigation](tactical-target-and-draw-attribution.md)
names the reviewed T175 control correction in its `257f2d2` comparison and keeps
complete causal attribution open; this historical intake does not replace that
later accounting. The `improved` cap, remnant, pursuit and exact restore design
is separate and is unchanged.

## Coordinate regions and scorer arithmetic

**[confirmed: code]** `FUN_0044eb18` returns **(region index, coast flag)**.
The index starts at -1; all eight strict-interior box tests run in order,
with the last match winning. The actual interiors do not overlap.

| Index | Excluded x bounds | Excluded y bounds |
| --- | --- | --- |
| 0 | 58, 64 | 53, 61 |
| 1 | 86, 91 | 40, 48 |
| 2 | 82, 90 | 49, 63 |
| 3 | 99, 114 | 62, 74 |
| 4 | 149, 156 | 54, 60 |
| 5 | 150, 164 | 75, 81 |
| 6 | 163, 167 | 66, 71 |
| 7 | 190, 207 | 68, 79 |

The coast flag starts at 1 and becomes 0 if any of `(44,78), (158,75),
(180,36), (171,41), (163,45), (161,47)` satisfies `x < cx && y < cy`.
Equality fails that pair's test. Tables are at `0x4794e8` and `0x479528`.
`FUN_0044eb9c` compares both components, including for index -1.
Reachability's `FUN_0044ca30` is byte-identical over 132 bytes; its caller
`FUN_0044cab4` accepts either matching pairs or ownership of **any fleet**,
without checking launch state or computing a sea path. Movement's
`FUN_0044da88` uses the same classification before mapping it to a movement
kind. A nearest-city assignment is not this coordinate classifier. The
[334-city CSV](https://github.com/diegoami/imperial-conquest-2-research/blob/500e521/docs/data/city-regions-20261016-145216.csv)
is a derived mapping of the original coordinates, not the classifier itself.
Fleet hunting `FUN_0044f4f8` has no corresponding region score penalty.

**[confirmed: code; signed subtraction corroborated by x86]** Enemy-city and
field-army scores subtract distance **before** subtracting `trunc(score/2)`
when the pairs differ: 107 becomes 54; -3 becomes -2. City weak/near doubling
then capital doubling follow; the army scorer caps at 1000 then adds 1000
for a weaker enemy within 7. Both select by strict `>` (first equal retained),
store the winning score in a word and add the winning distance on return.
The instruction slices are at `0x44edd1` / `0x44ef4d`; 20 upstream signed
cases corroborate that operation, not all whole-scorer overflow behavior.

The later [scorer controls](https://github.com/diegoami/imperial-conquest-2-research/blob/895adfb/docs/reports/2026-10-30-scorer-word-storage130.md), research `895adfb`, settle the previously open storage boundary **[confirmed: original instructions/emulation]**: compare each 32-bit candidate strictly against the sign-extended stored word, store its low word on a win, then add the distance with word wrap and return signed. No target returns -10000 and distance 0 (city index -1; army index word untouched). All 22 arithmetic controls match; the army cap makes truncation unreachable for that scorer. The public survey found no city truncation or final wrap in 251,182 calls. Strength-zero divide faults occurred only in staged army inputs; [T246](../tasks/T246.md) retains the remake's zero-divisor safety. Its scope is both enemy scorers under original policy; designed arithmetic and ID ties remain separate.

The helpers `0x44a8cc`, `0x44a930` and `0x44a98c` distinguish type-valued
field-army strength from assault strength and city defense:

```text
field strength = trunc(sum(trunc(typeValue * troops / 100)) / 80) * morale
assault strength = trunc(sum(troops * (3 for archers, otherwise 1)) / 80) * morale
```

City defense applies capital and owner/allegiance modifiers before queued
recruit troops/2 are added; the enemy-city scorer then applies
`trunc(defense*9/10)` if the attacker owns the city's original allegiance,
before the ratio and later score modifiers. The report gives the complete
integer operation order. Its synthetic Wine corridor controls observe army
scores 110 and 57 at equal distance 3, strict box edges and coast-only
separation at fallback index -1. The negative-score arithmetic and city
modifiers remain code/x86 evidence, not played Windows outcomes. Synthetic
corridors establish no natural island-crossing behavior.

## Automatic army and fleet support

**[confirmed: code]** Army support (`FUN_0044f31c` → `FUN_0044e41c`,
52925–52928) runs for every own army before the moves check, including
embarked armies, using **army record coordinates**, without following the
carrier link. Cities are scanned in table order at Chebyshev distance **< 5**;
own cities require positive stock. Foreign non-war cities additionally need
a positive purse captured at entry to the city loop. `FUN_0044f6d8` (53077)
uses capacity `troops/100` and the army's separate 500-based purse grant.

**[Wine candidate, staged]** Matched aboard/land no-move armies at 50 tons
receive 250 from Antium (330 → 80), reaching 300; carrier supply stays 720
(`aboard-20261016-145017-521550-post.SAV`, `land-...-post.SAV`, turn 0734).
Distance 4 qualifies, 5 does not; empty-stock and hostile controls do not
supply. A purse 0 with treasury 1 receives 500 and leaves -499; treasury 0
blocks the grant while free supply still runs. A previous provider can fill
the army and prevent another grant through the purse threshold. These
record-based probes do not establish transported-coordinate synchronization
in either the original or the remake.

**[confirmed: code]** Launched own fleets run `FUN_0044e5dc` before movement,
even at zero moves. Its radius is **< 4**, not the army's <= 4. Own-city
support has no stock or coastal eligibility test; foreign non-war support
requires stock and positive entry purse. Destination-selection coastal/path
tests belong to another routine. `FUN_0044f7e4` (53120) computes free supply
`min(ships*8 - supplies, city stock)`, then:

1. If purse < 100 and treasury > 100, **add 100** to purse and debit 100.
2. If condition < 95, debit `trunc((100-condition)*ships/5)` and set condition
   to 100, without a solvency gate. There is **no flat-100 repair branch**.
3. Commit the previously computed supply and city-stock writes.

**[Wine candidate, staged]** Purse 99 / treasury 101 becomes 199 / 1.
Treasury 100 blocks that grant. With 90 ships, condition 94 and treasury 1,
repair costs 108 and leaves -107; 91 ships costs 109. Condition 95 or 96
is not repaired. Grant and repair can both run at one city; distance 3 and
4 controls distinguish eligibility, and zero stock does not prevent the
own-city handler. The earlier westward distance-4 case has another provider
and is not a valid no-provider control. War controls have unrelated recruitment
spending, which is not attributed to support. The full foreign-purchase
matrix remains unplayed. These automatic operations are distinct from the
human repair dialog and its carrying-army/move restrictions.

## Capital defence before the army tree

The promoted [capital-defence report](https://github.com/diegoami/imperial-conquest-2-research/blob/04a9e45/docs/reports/2026-10-30-capital-defence-rule123.md) (research `04a9e45`, answering research #123) extends the original's homeland intercept account. This evaluation reads the report and production source; it does not repeat its original-game runs or artifact verification.

**[confirmed: code; x86 controls; Wine candidate for in-play outcomes]** `FUN_0044efc8` runs before F31C's per-army loop. It lists the first **nine** qualifying foreign armies in table order, in the capital's region/coast side, within Chebyshev 19 at war or 9 otherwise. Own armies with moves are considered in table order until `threats + 2` have been dispatched. An army with a qualifying offensive target (score above 100 and distance below its moves) is skipped when the capital is more than three times its moves away. The nearest eligible listed live threat wins, earlier entry on an equal distance. A non-war threat is eligible only when the responder is at least 20 from the capital. Interception or capital fallback spends moves before the ordinary target tree; it does not override a target already chosen by that tree.

The report's original wording named an **aboard responder** in the capital-fallback rule. [Research PR128](https://github.com/diegoami/imperial-conquest-2-research/pull/128), report correction `1582986`, identifies this as an intake error: the covered-cell comparison at `0x44F2A3` is on the **chosen threat**. An aboard threat, or a threat not at war, sends the responder toward its capital; otherwise it intercepts. The responder's own aboard state is not tested there: DBA8 forwards its move to its carrier. The older specification was already correct. This is a code-reading correction, not a new playtest or a second independent review.

The emulated controls swap records to change an equal-distance choice and the threat admitted by the nine-entry cap. Research independently reran those controls and verified 183 release files and 90/90 hooked/plain save pairs. In three 30-turn Wine starts there were 179 calls and 89 dispatches, with one or two threats per call, **zero ties and zero cap drops**. This establishes no natural-play table-order outcome difference in those turns. Crowded/later play, the aboard case and the 20-tile exemption were not isolated in play; the strict tie disassembly is conquest's reading, not a fresh research re-read. Native Windows confirmation remains open.

At remake checkpoint `7982eb9a`, `AiTurn.Run` runs economy and support/hire passes then the scored proposal loop; `AiMilitaryPhase.Propose` proceeds directly to each own army's target tree. No capital-defence assignment runs first. This source comparison is filed as [#1620](https://github.com/diegoami/imperial_conquest_2/issues/1620), separate from [#1216](https://github.com/diegoami/imperial_conquest_2/issues/1216)'s remaining scorer storage gap. T240/T244 preserve table order and T243 corrects equal enemy picks; none introduces this prepass. No production correction or new remake/original runtime comparison is part of this intake.

[T245](../tasks/T245.md) supplies the separate faithful prepass before support/hire and ordinary target proposals, using the existing command dispatcher and navigation. T247 subsequently changes an embarked responder to a same-seat army ferry request, spending two army moves and deferring sailing to the fleet phase (#1632); it does not sail the linked fleet immediately. This routing correction is separate from the land-arrival contact gap in #1639 below and does not establish native-original outcome parity. The designed AI and save schemas are unchanged.

The same research commit corrects the army-table report's old “at most 10” threat count to **nine** and links the full rule. It changes no T240/T244 sweep rule or T243 enemy-pick rule. Its selection-rhombus update is only a findings-intake ledger re-review: the selection report itself remains at `576d097`, already evaluated separately.

## Remake comparison checkpoints

The following findings describe the source read at build `5ca9c3f` during the
October 16 evaluation, not a claim that all defects remain in later code.
At that checkpoint, `AiResupplyPass` excluded embarked armies; its fleet
radius reused the army's <= 4. `AutomaticResupply.ForFleet` shared the army
500-based purse hygiene and lacked automatic repair. `AiResupplyPass` also
discarded results with zero admitted tons and zero price, even when purse and
treasury changed. The findings go through the bug lane, not an implementation
on this evidence branch. Existing [759](https://github.com/diegoami/imperial_conquest_2/issues/759),
[823](https://github.com/diegoami/imperial_conquest_2/issues/823) and
[938](https://github.com/diegoami/imperial_conquest_2/issues/938) cover embarked
support, fleet support and the missing region classifier respectively.

The separately filed source findings at that checkpoint were [1145](https://github.com/diegoami/imperial_conquest_2/issues/1145)
(field-army strength, winning-distance return and city allegiance defense)
and [1146](https://github.com/diegoami/imperial_conquest_2/issues/1146)
(purse-only support discarded as a no-op). The scorer also used id-based ties
and wide winning-score storage, unlike the original's first equal candidate
and word storage; those differences require an explicit fidelity accounting,
without extrapolating the limited signed-operation probes to all overflow cases.

The later source checkpoint is build `0056339972b95f257542525303521439fd468e60`:

- [T176](../tasks/T176.md) changed original-economy support writeback to retain
  any changed unit, city or nation record, including purse-only results. Designed
  economy keeps the zero-ton/zero-price suppression
  ([`AiResupplyPass`](https://github.com/diegoami/imperial_conquest_2/blob/0056339972b95f257542525303521439fd468e60/src/IC2.Engine/Ai/AiResupplyPass.cs)).
- [T177](../tasks/T177.md) supplies the original fleet's strict distance bound,
  additive grant and independent repair. Central-treasury mode omits the purse
  grant but retains repair; designed fleets keep legacy hygiene and no automatic
  repair ([`AutomaticResupply`](https://github.com/diegoami/imperial_conquest_2/blob/0056339972b95f257542525303521439fd468e60/src/IC2.Engine/Economy/AutomaticResupply.cs)).
- [T178](../tasks/T178.md) supplies field-army strength, winning-distance return
  and attacker-allegiance city-defense adjustment
  ([`AiArmyTargetTree`](https://github.com/diegoami/imperial_conquest_2/blob/0056339972b95f257542525303521439fd468e60/src/IC2.Engine/Ai/AiArmyTargetTree.cs)).
  That scorer still uses id-based ties and wide winning-score storage. Its
  nearest-pinned-city `RegionOf` with fallback 0 still differs from the original
  coordinate-pair classifier; embarked armies are still excluded by the support
  pass. These are source comparisons, not runtime tests or new original evidence.

These later corrections do not promote the staged Wine observations to native
Windows confirmation, prove transported-coordinate synchronization, or settle
the original's full word-overflow behavior.

## Fleet route lifetime and faithful destination execution

[Research `895adfb`](https://github.com/diegoami/imperial-conquest-2-research/blob/895adfb/docs/reports/2026-10-30-fleet-route-lifetime131.md) **[confirmed: original instructions/emulation]** corrects the old saved-route execution premise of #1405. Fleet +4/+6 are signed words; X >= 0 indicates a route and inactive Y is residue. Only the AI army phase writes them (D9A8 rendezvous or DBA8 carried-army landing), F608 reads them in the same seat, and the round tick clears X only. The fleet phase never writes them. A route found in a save is never read. Native import/export canonicalizes the words under its accepted exchange policy; this is not evidence of a live saved-route gameplay defect. [T247](../tasks/T247.md) therefore requires transient army-to-fleet context, not resuming imported destinations.

F608 runs prepass → port pick → stored route → hunt → port fallback. Hunt needs moves > 0 and signed score strictly above 100, draws once plus once per eligible target, preserves first-equal table order and treats an owner-city code in the target's 3×3 neighbourhood as protection. Port selection has two passes, with the second always running after a foreign first-pass choice; x > 247 and (x > 213, y > 98) cities are excluded. The 39 local controls contain no route writes, 42 E1FC calls and 30 draws; these are equivalent-phase controls, not full-seat RNG parity.

[Research `b490785`](https://github.com/diegoami/imperial-conquest-2-research/blob/b490785/docs/reports/2026-10-30-fleet-move-contract131.md) **[confirmed: original instructions/emulation]** completes E1FC: latch the original target map code; try the direct line; otherwise replace land/city/army target codes 2–247 with nearby sea (last expanding-square scan match, eastern exclusion), apply D174 group 0 without another direct-line test, charge one fallback move if nothing moved, recurse on the original target, and run arrival at every returning level. This explains the (74,69) endpoint, 7-move swing, repeated docking and 24-move coincident-route round trip. CB40 at distance zero reads uninitialized state; these observations do not choose a deterministic original rule there. Naval combat outcomes, hostile-city arrival, plain-sea arrival, record compaction, global RNG and native Windows parity remain unestablished. Existing legal contact integration is not new original outcome evidence.

## Campaign comparison at v0.8.5

[Recheck `f194ce6`](https://github.com/diegoami/imperial-conquest-2-research/blob/f194ce6/docs/reports/2026-10-30-recheck-v085.md) compares remake `1622407e`: 139 tests, 8 fixed, 2 changed, 129 unchanged, none regressed. Human sea-target moves and army second walks match; the fleet (88,32) case matches, while (87,47) matches immediately after the order and differs after End turn. The later foreign-inspection commit `ed6dd4ad` was not tested. [Campaign attribution at the same research commit](https://github.com/diegoami/imperial-conquest-2-research/blob/f194ce6/docs/reports/2026-10-30-campaign-differences-v085.md) attributes the first all-human turn-16 divergence to a weather draw; turns 1–15 match. This establishes no RNG-sequence parity.

Two faithful fleet moves differ in 13 of 16 bot campaigns (#1405/#1409). Carthage army 3 also selects a different target in those campaigns: remake resupply (95,77) stops at distance 1, so extra march continuation cannot explain it. The later [branch attribution](https://github.com/diegoami/imperial-conquest-2-research/blob/b633494/docs/reports/2026-10-30-carthage-army3-branch138.md) resolves that single start/turn as described below; it does not establish a new raw-resupply defect or reopen T213/#1402. Other differences are attributed to draws, with no statistical proof that city-supply differences are purely random.

## Long-run execution evidence at v0.9.0

[Research `07965cd`](https://github.com/diegoami/imperial-conquest-2-research/blob/07965cd/docs/reports/2026-11-02-longrun-v090-ai-gaps.md) compares published remake `88c1989b` with four original starts: Rome, Carthage and Gaul for 40 turns each, Seleucid for 25 before the later-investigated turn-26 overthrow (below). Anchored replays start every turn from the original's own save; free-running games accumulate earlier differences. **[Wine candidate: observation-only hooks and remake decision/walk logs]** Hook/plain autosaves agree in all three decision probes and three fleet probes; the later AAB4 gate probe agrees in four of four starts. These are research's checks, not reruns by this intake. The fleet-start saves were subsequently published in `hooks-and-probes-saves.tar.gz` in the report's linked release and verified by research; they are no longer an unpublished-artifact limitation.

**Gap A — spent-move swing versus fallback gate ([#1723](https://github.com/diegoami/imperial_conquest_2/issues/1723)).** Macedonia army 8 chooses resupply, swings out and back to (144,42), and remains there in the original with 2 of 9 moves left. The remake also returns there, then applies garrison fallback and ends at (146,44). The 410 own-capital rows and three Armenia–Van rows expose this second march. Research's AAB4 hook at `0x44F4CC` measures 38 walks over four starts: 37 return true and skip fallback, including returning swings; only the untouched Celtiberia army returns false and takes fallback. The reported original predicate is `recomputedFullMoves != remainingMoves`, so spending moves counts even when the tile is unchanged. The historical tile gate also appears at v0.8.4, `8e2d9053` and `e20a6488`; this is not established as a newly introduced v0.9.0 regression. [PR #1726](https://github.com/diegoami/imperial_conquest_2/pull/1726), commit `9588fe3e`, replaces the Faithful tile gate with a remaining-moves comparison against the full allowance from the shared end-turn movement query. The [derived fix specification at research `07965cd`](https://github.com/diegoami/imperial-conquest-2-research/blob/07965cd/docs/reports/2026-11-02-remake-fixes-gaps-ab.md) corrects the original report's per-command wording: earlier spending in the seat also counts. The full-budget and last-command tests agree on 38 measured walks, but no case distinguishing them was sampled. This is the same gate corrected by #1726, with no new rule or permission to change Improved-only reachable resupply/garrison/emergency policy.

The [army-comparison correction at `220c500`](https://github.com/diegoami/imperial-conquest-2-research/blob/220c500/docs/reports/2026-10-30-ai-decisions-vs-remake-main.md) explains why Macedonia had counted as “same”: the comparison checked first decisions, not final tiles. Its bounded counts remain valid on that axis; they cannot establish absence of a later fallback or complete execution parity.

**Gap B — adjacent mercenary-run siege ([#1724](https://github.com/diegoami/imperial_conquest_2/issues/1724)).** Carthage army 2 beside Castulo (city score 98) and Seleucid army 5 beside Astacus (97) select `e84c+city>85`, stay adjacent and cause siege loyalty/troop losses in the original. Remake v0.9.0 probes select `MercenaryRun` with no offer in reach, then step aside with unchanged loyalty/troops. The long-run table has 17 enemy-city stay/different-endpoint rows, including a random Galatia–Cius case; that count is not 17 independently attributed mercenary-run mechanisms. The plain city-target march is also present at `8e2d9053` and `e20a6488`; v0.8.4 predates the city-target destination correction and still has the older resupply continuation. Source similarity alone supplies no older-pin outcome recheck.

[The cause report at research `07965cd`](https://github.com/diegoami/imperial-conquest-2-research/blob/07965cd/docs/reports/2026-11-02-gapb-siege-call168.md) answers [research #168](https://github.com/diegoami/imperial-conquest-2-research/issues/168): **[confirmed: Wine hook]** all five sieges in four one-round starts call `FUN_0044b27c` from D734's end check at `0x44D949`, through `DBA8`/`D734`. Two come from F31C's `0x44F4C7` continuation (Castulo and Astacus), three from `0x44F43F` (`city>100`). In the two continuation cases E84C makes no walk of its own; the already-adjacent army takes no step, besieges and reaches AAB4 with zero moves, before any garrison fallback. The guard code requires exact distance 1, a city marker, war (relation 3) with the city's **owner**, and positive moves; the siege sets moves to zero. City score and mercenary offers are not end-check guards. **[code]** Negative guard sides (not adjacent, not at war, zero moves) were not measured.

The [derived specification](https://github.com/diegoami/imperial-conquest-2-research/blob/07965cd/docs/reports/2026-11-02-remake-fixes-gaps-ab.md) also records one measured D734 army-target attack: Carthage 2 in `hooks-gapb-v1/rome-t01end`, through F31C `0x44F409`, ending at (39,64), with AEE4 returning to `0x44D9A0`. The city-end outcome was not observed through E84C's own `0x44E913` walk or F31C `0x44F409`/`0x44F47C`; shared code alone does not measure those callers. The earlier correction contract covered the Faithful walk's guarded target end check before no-progress fallback. The later recheck below measures it on the garrison fallback walk too. Neither observation establishes a general automatic siege for all movement or wider zero-move transport permission. The separate remake `AttackCity` branch besieged in one score-102 sample; that is not a measured threshold. These findings differ from the earlier destination/ferry work in [#1653](https://github.com/diegoami/imperial_conquest_2/pull/1653)/[#1661](https://github.com/diegoami/imperial_conquest_2/pull/1661).

The zero-versus-nine-move observations are reconciled: Carthage 2 has zero immediately after siege and nine in the next autosave after a mid-round refill. Both watched armies refill between Celtiberia's and Illyria's seats in all four starts; the refill function was not hooked. In `rome-t06end`, reused slot 2 is Media's capital army, not Carthage 2. Compare positions, city loyalty and troops, or seat-end hook values, when checking the correction. Castulo loses three loyalty points per turn, Astacus two; sampled troop losses are 1,146 and 594 (roughly 600–1,150), correcting the older 500–700 wording. Hook/plain autosaves agree in four of four cause probes; this intake did not rerun them. These Wine measurements and the derived implementation specification do not establish native Windows confirmation.

**Gap C — hunt samples and unresolved port tile.** The [fleet-report addition at `220c500`](https://github.com/diegoami/imperial-conquest-2-research/blob/220c500/docs/reports/2026-10-31-ai-fleets-vs-remake-main.md) supersedes “hunt not exercised” for the later sample only. Utica and Leptis Magna exercise scores on both sides of `> 100`; hunt scores vary randomly in both games, with remake samples 82–127. No distribution or draw-sequence parity is established. Alexandria selects the port branch in both, but the original stays at (189,93) while the remake ends at (188,92), accounting for three rows. The cause at port picker `FUN_0044e9a8` remains unassigned; old route-lifetime/ferry issues do not attribute it. No confirmed rule defect is inferred from these fleet endpoints.

The registered-difftest labels include 11 “REGRESSED” rows from one storm draw (condition 96 versus 97); both values occur across seeds on v0.8.4 and v0.9.0, so they do not establish regressions. Free-running army-count/capture differences accumulate through earlier wars; anchored army counts agree turn by turn. The later diplomacy follow-up below attributes four war rows and separates quarterly relation writes; it does not attribute every capture or battle. The bounded turn-26 overthrow replay below supersedes the earlier unresolved human-end condition. Seleucid campaign turns 26–40, broader seeds/nations, hunt distributions, global RNG and native Windows AI-contact confirmation remain outside the evidence. This intake changes no release acceptance, historical task DoD or allocation, and repeats neither original execution nor artifact verification.

**Recheck after #1726 and #1728.** The [research `4bacb8e`](https://github.com/diegoami/imperial-conquest-2-research/blob/4bacb8e/docs/reports/2026-11-02-remake-11f713e2-recheck.md) recheck of remake `11f713e2` replays 145 anchored turns without error: capital-army rows fall from 410 to 4 (the later-attributed war rows below), enemy-city rows from 17 to 1 (Sidon), and other-city rows to zero. Sidon measures a third D734 siege caller, EBE8 garrison fallback → DBA8 at `0x44ECD6`, with score 84, nine moves and war with the city owner; [PR #1731](https://github.com/diegoami/imperial_conquest_2/pull/1731), commit `e1772c52`, adds that guarded city contact to both remake garrison construction paths. The [later v0.9.1 recheck at research `570aff8`](https://github.com/diegoami/imperial-conquest-2-research/blob/570aff8/docs/reports/2026-11-03-remake-v091-recheck.md) independently confirms Sidon: enemy-city rows fall from one to zero over 145 anchored turns, with 144/145 turns retaining the same reported difference counts. A new Ptolemaic run finds the separate no-command full-budget gate omission ([#1739](https://github.com/diegoami/imperial_conquest_2/issues/1739)): one 19,300-troop army has eight moves against allowance ten. The [later Ptolemaic position attribution](https://github.com/diegoami/imperial-conquest-2-research/blob/bc8d720/docs/reports/2026-11-03-ptolemaic-longrun-positions.md) separates four causes across ten rows: the no-command budget gate, pending-human-battle capital defence (#1741), resupply-city table ties (#1742), and new-fleet launch placement (#628). Five further rows depend on draws, without identifying the particular random calls. These Wine/code findings do not establish native Windows or full state/RNG parity. The earlier four port/hunt fleet rows remain separate from the newly measured launch defect. Native Windows AI-contact and global-RNG parity remain unproved. The [diplomacy follow-up at research `264098b`](https://github.com/diegoami/imperial-conquest-2-research/blob/264098b/docs/reports/2026-11-03-diplomacy-longrun.md) attributes the four war rows to war/alliance rolls and battle/treaty changes to protection, rather than a measured deterministic diplomacy defect. Across seven starts and three rounds (330 seat-turns), it reports 326 matching, three roll-only and one after-divergence seat, with zero deterministic differences; hooked/plain saves agree in 21/21 rounds. This Wine sample does not measure distributions or native Windows parity. Its code-only D3 trade-order finding motivates T274/[#1732](https://github.com/diegoami/imperial_conquest_2/issues/1732): Original AI prioritizes first-sweep non-leading nations, then still-eligible skipped war leaders in table order if live capacity remains; Improved keeps its single scan. The initial scan shares the existing strict running-war predicate and retains its leader set across higher-priority war/alliance writes, while trade eligibility/caps remain live; this is a bounded correction within the scored driver, not full monolithic diplomacy parity. The report's intake did not independently recheck the individual battle/treaty/protection chains.

Research independently recounted the nine residual rows and reread the fallback hook and remake log. In Seleucid turn 10, Ptolemaic army 6 stays at (221,70) and besieges Sidon (221,71), city 263, in the original; the remake logs garrison fallback and steps to (222,71). AAB4 returns false with all nine moves; EBE8 is called from `0x44F4D9`, DBA8 from `0x44ECD6`, then D734 calls B27C at `0x44D949`. The hook/plain autosave is identical. This extends the [derived specification at `46f1b20`](https://github.com/diegoami/imperial-conquest-2-research/blob/46f1b20/docs/reports/2026-11-02-remake-fixes-gaps-ab.md). The #1724 correction in PR #1728 covered the earlier measured `0x44F4C7`/`0x44F43F` city callers and army-target contact, excluding this garrison caller. The measured remake path is `AiMilitaryPhase.AddMarchCandidate`'s no-tree-command fallback; `AiTurn.RunGarrisonFallback` also left `EndCheck=None` on that measured revision. [PR #1731](https://github.com/diegoami/imperial_conquest_2/pull/1731) adds `OriginalWalkEndCheck.City` in both paths at `e1772c52`. That source correction is distinct from an independent original/remake replay, which this report does not provide. E84C's `0x44E913` and F31C `0x44F409`/`0x44F47C` remain unobserved for city-end contacts; negative guard sides remain code-only.

**Diplomacy attribution (research `264098b`).** The four capital rows are Carthage turns 34/36, Gaul turn 36 and Seleucid turn 18. The [follow-up](https://github.com/diegoami/imperial-conquest-2-research/blob/264098b/docs/reports/2026-11-03-diplomacy-longrun.md) attributes Carthage 34 and Gaul 36 to battles ending Illyria's alliances/protection before Macedonia's war roll; Carthage 36 and Seleucid 18 arise from alliance rolls and the setter's inherited wars (Ptolemaic with Celtiberia, and Media with Ptolemaic). These are reported causal chains, not independently rechecked row by row at research intake or here. They supersede the earlier unassigned-war wording without establishing identical battle occurrence or global random streams. Port behavior and hunt distributions remain unresolved.

The diplomacy hook separates the Illyria-seat quarterly bookkeeping writes from AI actions: four initial deterministic classifications become zero after thaw/deposition writes are separated. Thaw chooses +1 **or** +3 (not +1 plus an additional +3), giving ordinary two-point differences between draws; the report's cooldown count includes one +4 exception, so it should not be read as 157 exclusively two-point pairs. Research checked the 330-seat classification totals and 21/21 hooked/plain equality, but did not independently recheck those cooldown pairs, neighbour-mask stability or the causal chains. This intake reads the promoted reports and current C#; it downloads no original saves or screenshots and performs no original execution.

**Static limits.** [D3/#1732](https://github.com/diegoami/imperial_conquest_2/issues/1732) is an existing rule departure: `EligibleTradePartners` and `ProposeOwnTrade` collapse two passes into one rank-ordered list. The original loop-1 `else if` excludes every candidate that becomes the running best war target in that iteration; loop 3 can revisit it later. A binding three-trade cap can change the chosen partners. No D3 play divergence was observed in the new sample. Historical driver-scope comments are not an owner-approved fidelity exception. No runtime code is changed by this intake.

The [primary report §1a at `264098b`](https://github.com/diegoami/imperial-conquest-2-research/blob/264098b/docs/reports/decompiled-ai-offers-to-human-seats.md#1a-fun_0044fb7c-an-ai-seats-own-turn-confirmed-decompile-key-compares-checked-in-the-listing) identifies power's input as wealth `+0x430`, matching `AiOwnDiplomacyRule.Power` and `PowerWealthDivisor`; the new overview table's treasury caption does not establish a field defect. Its added disassembly note locates the busy store inside loop 1 (`0x44FC55`, after relation 0–2 gates). With no qualifying nation the alliance check reads an uninitialised value. This remains an original-code undefined edge, without a measured remake defect or an invented stack-emulation requirement. The [rules-specification consolidation at `d5e9f05`](https://github.com/diegoami/imperial-conquest-2-research/commit/d5e9f05) restates these reports and earlier movement/overthrow findings; it is derived documentation, not new measurement or proof that all diplomacy rules match.

**Turn-26 human overthrow.** The bounded Seleucid turn-26 replay in [research `e449768`](https://github.com/diegoami/imperial-conquest-2-research/blob/e449768/docs/reports/2026-11-02-seleucid-turn26-overthrow.md) confirms human overthrow under Wine, native Windows and remake `11f713e2` from the same start with the logged seed. Research independently verified identical executable bytes and both message screenshots; the public v2 asset uses a Windows game-window crop. This intake did not rerun or download them. The full campaign’s unpainted Wine battle window was not exactly reproduced and remains unexplained. Selected-seat watch continuation is an approved T83 design (owner decision on [PR #375](https://github.com/diegoami/imperial_conquest_2/pull/375), review N2/N3; `GameSession.cs:105–112`, distinguished from final-human hotseat exit at `1296–1312`). Native confirmation covers only this bounded overthrow, not other AI contacts, unmodified retail, global RNG or whole campaigns.

## Carthage army 3: original branch and EE60 halving

[Research `b633494`](https://github.com/diegoami/imperial-conquest-2-research/blob/b633494/docs/reports/2026-10-30-carthage-army3-branch138.md) **[confirmed: original hooks and decompile; remake decision records]** pins the original turn-1 army-3 choice on one start and seed. The original picks enemy army 10 with stored score 34 and returned distance 59, hence score 93. Its first move is the `e913` call to `FUN_0044e84c`, from (93,79) toward (111,69) with 8 moves, ending at (88,77) with none. The later `f4a3` over-70 call toward army 10 at (34,63) has zero moves and makes no progress. The original's city score was not hooked: the reported city score 91 is from the patched remake, not an observed-original score.

The v0.8.5 no-target resupply choice came from extra `IsReachable` gates, already removed by T247. At main `d8e7a646`, army 3 reaches the original endpoint but selects `AttackArmy` with score 126. `AiArmyTargetTree.ScoreArmyTarget` at that baseline halves by nearest-city `RegionOf`; original EE60's EB9C/EB18 predicate compares region-and-side classification (`ClassicalArmyNavigation.Classify`). Applying that comparison in the patched remake yields 67 − 33 + 59 = 93, `MercenaryRun`, aim (111,69) and endpoint (88,77). The city scorer and capital defence already use `Classify` for faithful classical navigation. [#1628](https://github.com/diegoami/imperial_conquest_2/issues/1628) owns that army-score defect; [T248](../tasks/T248.md) applies the bounded predicate correction.

T248 uses the existing `ClassicalArmyNavigation.Applies` boundary (original-economy classical world, 320 × 140), preserving designed/custom-world `RegionOf`. File-free regressions distinguish coordinate classification from configured nearest-city regions in both directions, including a differing-side case, and exercise the decision threshold and mercenary destination as well as the numeric score; endpoint equality alone misses this defect. Keep signed-word storage, winning-distance return, target eligibility, resupply and movement contracts unchanged. The optional published-BASE regression compares branch, score, aim and the movement event/endpoint, explicitly skipping when that fixture is unavailable. End-of-round move refill is separate from the original walk's zero remaining budget. These checks do not alter historical task DoDs.

This evidence does not rerun the 13 campaigns on main, settle later turns (main stores the army-10 route), establish the `FUN_0044e84c` table contract, or establish global RNG or native Windows parity. The [campaign report correction](https://github.com/diegoami/imperial-conquest-2-research/blob/b633494/docs/reports/2026-10-30-campaign-differences-v085.md) links this attribution without expanding those limits.

## Army-decision comparison after T248

The promoted [comparison at research `c159c93`](https://github.com/diegoami/imperial-conquest-2-research/blob/c159c93/docs/reports/2026-10-30-ai-decisions-vs-remake-main.md) uses remake main `10d3bb81` (including T248), replaying 16 original New Game starts for five rounds from each original autosave. Research independently checked 86/86 hooked/plain save pairs for byte equality. Of 749 army decisions, 378 match plus 12 matching but unlogged decisions (390 total). The reported categories overlap: 260 resupply-city differences, 44 diplomacy, 16 state (14 battle, 2 death), 46 missing remake decisions, 28 city-target, 41 branch, 20 city-score, 16 army-target and 11 army-score differences. Research did **not** independently rederive the draft's `252 + 8 designed / 44 diplomacy / 17 state` grouping row by row. These counts are neither a general correctness percentage nor whole-turn parity.

Two source-backed execution gaps are separated from that attribution:

- **Mercenary continuation ([#1638](https://github.com/diegoami/imperial_conquest_2/issues/1638)).** F31C's `0x44F4C7`, after E84C when army score < 71 and city score > 85, marches toward the ECE4 enemy-city target, not F670's resupply city. This corrects the earlier strategic-AI report §3.3. At the comparison baseline, `AiArmyTargetTree.DecideUnlogged` resolves `DefendResupplyCity` through `ResolveResupplyCity`, and `AiMilitaryPhase` uses that resupply destination. Its offer-city march also breaks before continuation and the driver permits one march per army: a target correction alone does not establish complete run-then-continuation ordering.
- **Capital-defence contact ([#1639](https://github.com/diegoami/imperial_conquest_2/issues/1639)).** The promoted comparison reports the first responder fighting a threat before later responders in S01 Rome and S05 Seleucid. The existing [distance-1 tail controls](https://github.com/diegoami/imperial-conquest-2-research/blob/c159c93/docs/reports/2026-10-30-walker-distance1-tail-controls98.md) specify attack at exact distance 1, at war, with positive moves. The walker **never enters the army marker cell**. Their synthetic emulator controls intercept the contact routines; the comparison separately supplies the multi-responder outcome. At the comparison baseline, `AiCapitalDefence` dispatches movement without that army-contact attack, so the live threat remains for later responders. This is a scoped capital-defence defect, not a claim that every DBA8 caller's contact contract is settled. [#1329](https://github.com/diegoami/imperial_conquest_2/issues/1329) is the closed research-request history; [#1626](https://github.com/diegoami/imperial_conquest_2/issues/1626) concerns observability rather than this execution gap.

The [T249 recheck at research `85ef715`](https://github.com/diegoami/imperial-conquest-2-research/blob/85ef715/docs/reports/2026-10-30-ai-decisions-vs-remake-main.md#recheck-on-remake-main-05b02936-t249-1643-added-2026-10-30) compares the same 749 original decisions with main `05b02936`: 422 match (410 logged + 12 unlogged), capital-defence attack differences drop to zero, and 14 city-continuation differences remain, all already adjacent to the city target before the #925 garrison/emergency tier. Its independently read attribution is disjoint here: 422 same + 250 resupply-tier + 8 fallback-tier + 14 continuation-tier + 41 diplomacy + 14 battle/death-state = 749, with zero unattributed. That attribution applies to these rows only; it is not campaign, whole-turn or global RNG parity. The remake replays were not rerun by research's recheck review or this intake.

**Owner policy, 2026-10-31:** the [decision at research `8c4cfaf`](https://github.com/diegoami/imperial-conquest-2-research/blob/8c4cfaf/docs/reports/2026-10-31-reachable-tiers-faithful-decision.md) supersedes the previous approval to retain [#925](https://github.com/diegoami/imperial_conquest_2/issues/925)'s reachable tiers in Faithful. Reachable resupply, reachable garrison fallback and emergency moves apply **in Improved only, unchanged**. Classical Faithful must use F670's raw resupply pick, or EBE8's nearest city of any owner when an own army is strictly `< 10` from the capital (equal city distances to the first city-table record), otherwise the capital. It must use DBA8's original walk, including its no-progress swing and stay-put case, and skip emergency. An original swing is not the designed emergency tier. Preserve native absent-capital/null-city and malformed-pointer safety guards: original record-minus-one reads are undefined safety exceptions, not behavior to reproduce. T250 implements these destination, boundary and caller corrections, preserving Improved behavior.

Correction scope must include the callers: returning the raw F670 pick alone leaves `AiMilitaryPhase.AddMarchCandidate`'s single-walk `MarchMakesProgress` suppression able to replace an adjacent/blocked original destination with fallback. Both the tree proposal and `AiTurn.RunGarrisonFallback` emergency entry points need the Faithful policy, and post-execution fallback must use the original AI continuation walk rather than a default single walk. Bounded cases must distinguish raw adjacent-city and blocked-first-step swing/stay-put from Improved emergency, preserving honest issued/rejected counters and safety refusals. The existing one-march seat limit remains separate. T250 lets an accepted Faithful no-op march continue to other armies (the marched-army guard prevents repetition), rather than stopping their whole seat; other no-op handling is unchanged. These observations do not establish complete original driver scheduling.

The [boundary controls at research `78b63aa`](https://github.com/diegoami/imperial-conquest-2-research/blob/78b63aa/docs/reports/2026-10-30-garrison-and-mercenary-boundaries144.md) settle [#1640](https://github.com/diegoami/imperial_conquest_2/issues/1640)'s helper domains: 16 original and 15 native controls establish EBE8 distances 9/10/11 as nearest/capital/capital and E84C offers 19/20/21 as taken/none/none. EBE8 ties use the first **city-table record**, with no ownership eligibility gate; E84C ties use the first **mercenary-pool slot**, independently of city-record permutations. T250 corrects the native inclusive boundaries and city-id ties within Faithful. The absent-capital original control reads record −1; keep the native null result. E84C's missing exact-position-city guard is code-only evidence; preserve the native guard. These synthetic emulation controls establish neither live play nor native Windows behavior.

The corrected [army-3 report at `c159c93`](https://github.com/diegoami/imperial-conquest-2-research/blob/c159c93/docs/reports/2026-10-30-carthage-army3-branch138.md) reads the branch inputs **after E41C support**: 24,600 troops, hence `troops/500 = 49`, and supplies 246, rather than the earlier pre-pass 37/90. The branch and T248 classifier attribution are unchanged. Its original city score remains unhooked; 91 remains the patched-remake attribution. T248's file-free `SupplyTons = 40` input is a declared synthetic regression, not a reconstruction of that post-pass state.

This intake reads the promoted reports and current source, without rerunning the original, remake gameplay or release-integrity checks. Rounds beyond five, full march scheduling, global RNG and native Windows acceptance remain outside this comparison. Merged T245/T248/T249 contracts remain historical; the owner's new policy requires a separate correction contract. The archived design audit is a frozen pointer, with Q1–Q10 already decided; this evidence changes no release allocation.

T249 applies these two caller corrections: faithful `MarchAtCityTarget` uses the scorer target without resupply resolution; capital defence checks legal contact before fallback when already adjacent and after an approach otherwise, dispatches the existing attack, and refreshes live threats before later responders. Tests distinguish death and survival, zero moves, peace, carried requests, legacy/custom policy, counters and support ordering. A real tactical-session check opens the human battle and resumes the same AI turn through the normal replay gate. Designed/custom destinations and the one-march gate remain. CLI case B keeps seed3 and all commands but answers the natural Gallic trade at turn4; other transcripts are attributed to the corrected actions, without changing the pinned treaty fixture. No new original-game or campaign-parity claim follows.

## Diplomacy and economy comparisons on T249

The [diplomacy comparison at research `3ece080`](https://github.com/diegoami/imperial-conquest-2-research/blob/3ece080/docs/reports/2026-10-30-ai-diplomacy-vs-remake-main.md) covers 1,280 seat-turns on the same 16 starts/five rounds at main `05b02936`, with 80/80 hooked/plain pairs identical. It isolates one deterministic defect: the original visits partner then replacement in table order in a **single pass per seat**, never restarting after a swap. The native greedy proposal restarts `FindTradeSwap`; a newly acquired lower-index partner can be swapped again. A diagnostic one-pass patch removes all ten deterministic first divergences; 833/833 pre-divergence war candidates agree. War/alliance/treaty roll differences between generators are not measured rule bugs. The separately observed greedy phase ordering has no attributed row and is not a newly measured defect. Human diplomacy, later rounds, same-hit alliance selection and native Windows remain unestablished.

The [economy comparison at research `7d4168c`](https://github.com/diegoami/imperial-conquest-2-research/blob/7d4168c/docs/reports/2026-10-31-ai-economy-vs-remake-main.md) covers 1,200 AI seat-turns: 1,055 agree, 141 begin with earlier state/budget differences, and four differ in recruitment city. The original chooses the order city once before the order loop; native `AiFaithfulEconomy.PlaceOrders` calls `ChooseCity` inside each iteration, letting the capital queue-fraction gate flip mid-turn. The diagnostic city-once patch matches all four. Equal-budget seats match 1,059/1,059 deployments and 113/113 week-11 taxes; all 265 ordering seats follow the stopping rule using each side's own random draws. This does not establish identical draws, all city-selector branches, consolidation, merging, supply, mercenary hire, later rounds or Windows parity. The measured defects are owned by [#1644](https://github.com/diegoami/imperial_conquest_2/issues/1644) (swap pass) and [#1645](https://github.com/diegoami/imperial_conquest_2/issues/1645) (order city); T251 supplies a transient forward-only swap cursor in the existing scored driver, waiting until fresh trades are exhausted and rechecking live eligibility before every command. T252 selects the order-city identity once after mobilization and reads the live city/nation for each order; losing eligibility stops the pass rather than selecting another town. Improved keeps its existing diplomacy and economy. File-free regressions cover lower/higher-index gained partners, relation-table permutations, caps, rejection and replay; order-city queue-threshold crossing, independent invocation, full/partial queues and loss of ownership. These implementation checks are not new original-game observations.

T250 validation records accepted original march origins, first aims and raw goals as non-news instrumentation, so an exact dispatched swing/stay-put is distinguished from an unrelated move. The campaign decision check also verifies explicit target destruction, the exact original garrison fallback and its accepted movement receipt; wrong goals or missing receipts fail controls. Original SAV export still preserves signed negative moves; the pre-existing import policy reports and clamps them to zero, now exercised naturally by the 24-turn round trip. Neither save schema nor import/export policy changes.

## v0.9.2 native regression coverage

The promoted [v0.9.1 recheck](https://github.com/diegoami/imperial-conquest-2-research/blob/570aff8/docs/reports/2026-11-03-remake-v091-recheck.md) independently confirms the Sidon correction on released `0d9d4729`: 145 anchored turns without remake error; enemy-city stay/move rows 1 → 0. Only Seleucid turn 10 changes the reported comparison counts; the other 144 turns retain those counts, not a claimed byte-for-byte identity of every end state. The registry reports 113 unchanged / 11 fixed / 11 regressed / four changed; intake did not independently verify the draft's row-for-row v0.9.0 comparison. No new fleet defect or native Windows parity is established.

Its new 40-turn Ptolemaic run isolates [#1739](https://github.com/diegoami/imperial_conquest_2/issues/1739): Galatia army 12 has 19,300 troops and eight moves against AAB4's recomputed allowance ten before the mid-round refill, so the original does not call EBE8. The no-tree-command remake path previously tested only `mayMarch`. It now shares the existing `MovesForActionCheck` equality under Classical Faithful navigation, preserving the supply-product penalty and never resetting moves. Full-budget controls retain fallback; partial-budget controls stay put, including a supply-penalty case; Improved/custom paths and ordinary tree commands are unchanged. The hook/plain comparison is inert, but intake did not directly read the remake army's incoming moves or trace why the original has eight. The later attribution below accounts for the remaining fourteen position nation-turns, with five draw-dependent rows still lacking a specific draw trace. This correction is native regression evidence against the promoted rule, not a new independent original replay of the corrected remake.

[T274](../tasks/T274.md) shares the existing busy/protection/neighbour/power/relation predicate between the final war-target query and the fresh-trade ordering query. Original AI ranks first-sweep non-leaders before skipped leaders, in table order within each sweep; the invocation retains its initial leader set across war/alliance writes while each proposal rechecks live trade eligibility/capacity. A fixed-seed war-roll hit and a fresh later invocation protect that lifetime. Improved retains its single scan; war/alliance priorities, named rolls and the T251 closing swap cursor are unchanged. `FaithfulFreshTradeTests` uses actual AI-turn dispatch with two successive leaders and binding/nonbinding caps, plus live partner-cap, equal-ratio, human, protected, non-neighbour, busy and cooldown controls. This addresses code-confirmed D3 within the existing scored driver, not complete atomic FUN_0044FB7C or global-RNG parity.

The [v0.9.1 review](https://github.com/diegoami/imperial_conquest_2/pull/1735#issuecomment-6600429027) found two caller-coverage gaps, tracked by [#1736](https://github.com/diegoami/imperial_conquest_2/issues/1736). `FaithfulWalkEndContactTests` now exercises a real offer walk followed by its retained mercenary city continuation, and the separate `AddCityTargetCandidate` approach-city march. Both receive only one scored action: normal siege dispatch must happen inside the walk's contact handling, so a later greedy siege cannot mask removal of the caller's opt-in. Explicit fixture thresholds hold the intended tree arm across AI preparation without changing production values. The old no-offer test is named for its actual city-target path. These are native engine regressions, not additional original-game observations or a retrospective change to v0.9.1's PARTLY MET verdict.

Review NOTE 3's two interruption guards are retained defensively. Measured contact candidates currently use `AiCandidate.Single`, so the break after the nested contact in `AiTurn.Execute` has no remaining candidate command to skip. A mercenary continuation currently carries City or None: its contact can produce `BesiegeCityCommand`, whereas `GameSession.AiPhaseBattleGate.OpensABattle` suspends only `AttackArmyCommand` against a human army. Thus neither guard has an independently reachable production continuation shape to isolate in a behavioral test today; no synthetic suspension is counted as original-behavior evidence. The actual army-contact session pause/resume remains covered by `Walk_contact_opens_the_human_battle_and_resumes_without_replaying_the_walk` and capital-defence tactical-gate tests.


### Ptolemaic position attribution after the v0.9.1 recheck

The [promoted position report at research `bc8d720`](https://github.com/diegoami/imperial-conquest-2-research/blob/bc8d720/docs/reports/2026-11-03-ptolemaic-longrun-positions.md) examines nine starts from the original's own pre-End-turn saves, on remake v0.9.1 `0d9d4729`. Its fifteen position nation-turns split into ten rows attributed to four causes and five draw-dependent rows. The five are an inference from different original random streams reaching the remake's tiles, not identification of the random calls. Research verified all fifteen hook/plain inertness records and the relevant original code; it did not independently repeat the 85/3/1/2 decision subtotals, Galatia-pinning control or excluded-city explanation. This intake read the promoted report and current code, without downloading save archives or running either original or remake.

- **No-command budget gate (four rows, including a dependent Bithynia target).** Two additional Seleucid armies have only one move; they add measured cases of the same #1739 cause. Main `36ab15ec` already applies `MovesForActionCheck` equality to the no-command path. This source run predates that fix and is not an anchored recheck of corrected main.
- **Capital defence while a human battle is pending (two rows).** EFC8 has no battle-flag break. An attack against the human leaves the threat alive until the battle resolves, so the second responder still marches toward it. This refines the earlier AI-threat-death example rather than contradicting it. Current `AiCapitalDefence.Run` breaks at both interruption sites; [#1741](https://github.com/diegoami/imperial_conquest_2/issues/1741) records the discrepancy. The tactical dispatcher admits one pending battle, so simply deleting the breaks is unsafe: staged responders must preserve that boundary. Instant resolution of a human-defended battle has no original counterpart here; its intended treatment is an owner design decision, not inferred from the tactical observation.
- **F670 resupply-city tie (two rows).** The original's strict adjusted-distance replacement retains the first city-table record. Celtiberia army 10 picks Segovia rather than Saguntum (three cities tie at 12); Galatia army 9 picks Pessinus rather than Gordium (two tie at five). At the intake baseline `36ab15ec`, `ScoreResupplyCity` uses ordinal city-ID ties for its winner and nearest-own fallback; [#1742](https://github.com/diegoami/imperial_conquest_2/issues/1742) distinguishes this selector from corrected enemy-score, EBE8 city and E84C pool-slot ties. Why nearer Numantia/Gangra are excluded remains untraced; both games exclude them in the comparison.
- **Fleet launch (two rows).** A050's mode-0 `FUN_0044939c` scans expanding rings, x outer/y inner, retains the last matching tile and stops after the first ring with a match. It accepts terrain codes 0–1 while excluding x≥248 or x≥214 with y≥99; the exclusion is geographical, not occupancy. Original Siga/Issus launches are (37,83)/(219,55), versus the remake's (36,83)/(219,54). At the intake baseline `36ab15ec`, fleet completion uses `CoastalCity.FirstAdjacentSeaTile`, the first row-major adjacent sea tile. This strengthens existing [#628](https://github.com/diegoami/imperial_conquest_2/issues/628), rather than creating another launch defect. The same original helper's AI-disembark mode 1 (codes 2–11, no region test) and E1FC sea-side caller were code-only related edges in this report; the later placement report below observes them and corrects the inference of a remake landing gap.

**T275 correction scope.** [T275](../tasks/T275.md) changes Original-policy resupply ties to retain the first city record, both for eligible adjusted scores and the nearest-own fallback. Improved keeps ordinal-ID ties. Fleet completion uses a launch-only `CoastalCity.LaunchTile`: on the original classical world under Original policy it implements the measured mode-0 ring scan and geographical exclusions; other worlds/policies keep adjacent row-major-first placement. Coastal order eligibility, build-city choice, disembark, routing and split placement are separate. Synthetic real-completion tests reproduce Siga and Issus without original assets and exercise scan order, outer rings, boundaries, no-sea retry and occupied-tile semantics. This does not constitute a new anchored original replay or native Windows acceptance; #1741's pending-human-battle continuation remains outside T275.

**Hook labels corrected, rule evidence retained.** Research `bc8d720` corrects reused hook binaries' `watch` labels in the new position report and the earlier Sidon and Galatia reports. Those binaries watched armies 2 and 5, irrespective of their displayed labels. The cited Sidon nine moves/AAB4 result, Galatia eight moves and later responder's walk come from the acting army's own record/caller sequence, so the measured rules remain supported. Do not infer the watched army from an old label.

All new runtime observations are Wine-only; native Windows confirmation and whole-campaign/global-RNG parity remain separate. This intake creates no release allocation, changes no historical task acceptance, and leaves Improved behavior and the stronger Improved general's v0.10 plan unchanged.


## Released v0.9.2 recheck and November 4 follow-ups

The [recheck at research `b288cac`](https://github.com/diegoami/imperial-conquest-2-research/blob/b288cac/docs/reports/2026-11-03-remake-v092-recheck.md) compares released remake `1723c673` against six stored original games over 265 anchored turns without remake error. It confirms the no-command full-budget gate, resupply table-order ties and Siga placement corrections. It also measures a launch regression: `CoastalCity.LaunchTile` reads static terrain, so at Issus it places a new fleet on an existing fleet's tile instead of skipping its live marker ([#1748](https://github.com/diegoami/imperial_conquest_2/issues/1748)). The original's geographical exclusion does not replace its live-code test; both apply.

The [placement/Macedonia report, research `b288cac`](https://github.com/diegoami/imperial-conquest-2-research/blob/b288cac/docs/reports/2026-11-03-placement-and-macedonia-longrun.md), corrects the earlier landing inference: Classical Faithful AI disembark and sea-target replacement already use the live-map ring helper; generic `LandingTile` is a different caller. The observed launch is the discrepant caller. Its AI fleet-build-city code also excludes x≥248 or x≥214 with y≥99; four Macedonia turns show Media building at Ardabil only in the remake. Existing [#600](https://github.com/diegoami/imperial_conquest_2/issues/600) carries this AI finding. AI build-city ties retain table order (code-confirmed, not observed in play); busy-city score 2000 never beats initial best 1000, so skipping busy cities is equivalent. The busy-test global is calendar week 0–11, not a fleet-record lifecycle field. This evidence does not independently settle every human Build fleet caller merely because the remake shares the helper.

**Bounded walk correction.** Numidia turn 37 and [24 more hooked AI turns, research `b288cac` §1](https://github.com/diegoami/imperial-conquest-2-research/blob/b288cac/docs/reports/2026-11-04-v092-followups.md) narrow T239: a failed intermediate search suppresses that walk's swing and recursion; its immediately following same-army EBE8 garrison walk in the same F31C pass also suppresses them when it takes the direct path. Otherwise the measured T239 continuation applies. The original local word can be −1; the earlier 25-turn sample's nonnegative values were not universal. [#1749](https://github.com/diegoami/imperial_conquest_2/issues/1749) records the mismatch, without proposing global/saved stack emulation or extrapolation across armies, seats or unmeasured caller sequences. The DBA8-entry gap-B hook was non-inert on this case and is excluded; the within-frame walk hook was inert. Earlier individually inert gap-B runs retain their bounded conclusions.

**Embarked offer target.** The [port/carried-army report, research `f249cd1` §2](https://github.com/diegoami/imperial-conquest-2-research/blob/f249cd1/docs/reports/2026-11-04-port-rows-and-carried-army.md) attributes Numidia t29–31's Rome army/fleet rows: original E84C routes the carrier toward Syracusae's mercenary offer; the remake's embarked `MercenaryRun` branch falls through to Rome when no city continuation applies, without consulting `MercenaryRunDestination` ([#1750](https://github.com/diegoami/imperial_conquest_2/issues/1750)). Capital-defence staging remains the separate measured behavior described under [#1741](https://github.com/diegoami/imperial_conquest_2/issues/1741).

**Comparison method and limits.** Imported saves reset remake `TurnIndex` and seed; anchored replays without `--turn-seed` therefore repeat correlated draws. The follow-up reruns the stored games with per-turn seeds and removes the Alexandria/Salamis port rows (gap C as reported was a harness artefact). Subsequent seed sweeps in the port/carried-army report produce both leave/stay outcomes for the Aradus and Leptis Magna rows in both games; Aradus's particular draw is not traced. These observations warrant no generic port-rule patch or universal port-correctness claim. The 37 original versus 42/43 remake war-pair counts are one sample, not a distribution/equivalence test. Writing the original's logged RandSeed reproduces whole army/fleet tables for 13/13 Numidia rounds; this does not align the remake's RNG with the original's. The Greece t21 Antioch→Aradus in-round state change and Numidia t24/t32 remain unattributed. Some Numidia probe start labels were end-of-turn states, corrected in the follow-up; the stale-word t37 start was correct.

All new original runtime findings are Wine-only. Research intake verified selected logs, code and hook inertness but did not download the cited save tarballs or independently repeat every route/seed-sweep/grouping claim; this intake read the promoted ranges and current remake source only. None establishes native Windows acceptance, full-state/global-RNG parity or a new release allocation.
