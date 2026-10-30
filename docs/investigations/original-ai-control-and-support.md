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

[T245](../tasks/T245.md) supplies the separate faithful prepass before support/hire and ordinary target proposals, using the existing command dispatcher and navigation. It forwards an embarked responder to its linked fleet instead of issuing an illegal land-move command. The existing fleet command's route and undefined/cross-region navigation limits remain; this does not close #1409 or establish native-original outcome parity. The designed AI and save schemas are unchanged.

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
