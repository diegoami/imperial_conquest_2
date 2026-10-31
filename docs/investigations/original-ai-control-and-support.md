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

## T250–T252 rerun and army execution limits

The [rerun at research `cecb674`](https://github.com/diegoami/imperial-conquest-2-research/blob/cecb674/docs/reports/2026-10-31-ai-rerun-remake-8e2d9053.md) compares main `8e2d9053` on the same 16 starts × five rounds, seed 12345, replaying each round from the original's own round-start save. Its final army **decision-record** attribution is 691 same + 41 diplomacy + 17 state = 749, zero unexplained. All 250 resupply rows and seven of eight fallback rows match after T250. The remaining Galatia row follows destruction of its target in an earlier battle with a different outcome; it is not evidence of a missing mercenary offer. Diplomacy has zero deterministic first divergences (1,110 same seats; 22 draw and 21 battle first divergences). Economy has 1,063 same seats, zero city differences and 137 earlier-state rows; all 263 ordering seats satisfy the stopping rule on each side's own draws, and 113/113 week-11 taxes match. These counts confirm the bounded T250–T252 corrections, not identical RNG or whole-turn parity.

The [corrected boundary account at `921859a`](https://github.com/diegoami/imperial-conquest-2-research/blob/921859a/docs/reports/2026-10-30-garrison-and-mercenary-boundaries144.md) and rerun establish that T250 already includes all three Faithful-only boundary/tie corrections. Both layouts together match 28/30 controls. The two absent-capital cases intentionally retain the native null result rather than the original's record −1 read. No further boundary correction follows from these two controls.

**Whole armies in table order ([#1651](https://github.com/diegoami/imperial_conquest_2/issues/1651)).** The [army-order report at `d489203`](https://github.com/diegoami/imperial-conquest-2-research/blob/d489203/docs/reports/2026-10-31-army-order-within-a-seat.md) reads F31C as one table loop, with the count latched at entry (`0x44F32F`). Each own army is handled whole before advancing (`0x44F4DE`): target tree, walks/contact and fallback, with later armies seeing the earlier actions' live effects. Main `AiTurn.Select` instead compares candidates across armies and draws on equal scores. On the report's 679 eligible first-action rows, main matches 668; a diagnostic first-army filter matches 678, correcting ten and breaking none. The remaining Galatia row is the battle-state difference above. Initial decision records are logged on the first proposal pass, before execution, so their zero-unexplained count did not reveal this execution-order defect. The diagnostic is evidence, not an implementation contract. Upfront support/hire placement and global diplomacy/economy interleaving were not corrected or validated by this diagnostic; no broader phase rewrite or full-turn equivalence follows.

**Mercenary-run continuation ([#1652](https://github.com/diegoami/imperial_conquest_2/issues/1652)).** The [fleet attribution at `fbd6ba9`](https://github.com/diegoami/imperial-conquest-2-research/blob/fbd6ba9/docs/reports/2026-10-31-fleet0-rows-attributed174.md) classifies all 27 differing Carthage fleet-0 labels: eight equivalent `none`/`idle`, 13 earlier diplomacy/state differences and six continuation-gap rows. Original E84C walks toward the offer through DBA8; F31C then calls DBA8 in the same army turn toward the army target when score >70, otherwise the city target when score >85. Its ferry request can write the fleet route before the fleet phase. `AiMilitaryPhase` instead breaks after proposing the offer walk, and the driver's marched-army guard suppresses another march. T249's city-target correction alone does not supply this ordering. In S06 rounds 3/4/5, S08 round 2 and S13 rounds 4/5, the missing continuation explains the original-only ferry route and resulting fleet port difference; one of those rows also carries earlier battle-state divergence. This does not reopen the settled saved-route/import lifetime policy.

A ferry write with **zero army moves is supported but not proven**. DBA8's reviewed move comparison follows DB38; D9A8's reviewed gates concern fleet fields, but DB38 locals were not traced. Matching original endpoints and the remake spending all nine moves in S06 round 4 are indirect evidence; the original hook did not read army moves at the write. [Research #159](https://github.com/diegoami/imperial-conquest-2-research/issues/159) asks for those locals and an observation-only army-moves read at continuation entry and route write, with hooked/plain inertness checks. Do not harden this into an unconditional zero-move rule before that result.

Research reran the four comparers and army attribution against existing replays, obtaining byte-identical outputs; it did not rerun the remake replays, helper probes or diagnostic .NET builds. It separately checked release integrity (the army/economy releases again), original save decodes and disassembly. The army-order classifier and fleet-route comparer were likewise rerun on existing data; the fleet-route release has 160/160 verified files and 80/80 hooked/plain save pairs identical. This intake reads those reports and production source; it does not repeat original runtime or artifact verification. The evidence establishes neither whole campaigns, identical RNG, rounds 6+, other seeds, hunt coverage in this sample nor native Windows parity. Archived audits and historical task contracts are unchanged; defects go through the linked bug issues and release allocation remains on GitHub labels.
