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
resolve the 318-log targeting investigation. Its comparison remains dependent
on a **reviewed, integrated control correction** and the copy-in/exhausted-move
corrections, as required by [build issue 1107](https://github.com/diegoami/imperial_conquest_2/issues/1107)
and [research issue 25](https://github.com/diegoami/imperial-conquest-2-research/issues/25).
Report promotion alone does not clear that gate. [Build issue 1104](https://github.com/diegoami/imperial_conquest_2/issues/1104)
covers the control correction; the `improved` cap, remnant, pursuit and exact
restore design is separate and is unchanged.

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

The remake's merged `AiResupplyPass` excludes embarked armies; its fleet
radius reuses the army's <= 4. `AutomaticResupply.ForFleet` shares the army
500-based purse hygiene and lacks automatic repair. `AiResupplyPass` also
discards results with zero admitted tons and zero price, even when purse and
treasury changed. The findings go through the bug lane, not an implementation
on this evidence branch. Existing [759](https://github.com/diegoami/imperial_conquest_2/issues/759),
[823](https://github.com/diegoami/imperial_conquest_2/issues/823) and
[938](https://github.com/diegoami/imperial_conquest_2/issues/938) cover embarked
support, fleet support and the missing region classifier respectively.
