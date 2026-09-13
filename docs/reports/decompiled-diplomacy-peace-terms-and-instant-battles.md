# Diplomacy, the reparations formula, and the original's own instant battle resolution

Two of this project's longest-standing "not recoverable" conclusions turn out to be wrong:

- `decompiled-fleet-tax-and-mercenary-formulas.md` and `decompilation-plan.md` item 3 record the **diplomatic reparation formula** as a dead end — *"the AI-to-AI reparation seen in saves must be computed in unnamed AI code (`ComputerGeneral` or similar), not reachable by class name"*. It is reachable by class name: it is in `TBattlePols`, a form class that was sitting in `delphi_symbols.tsv` the whole time.
- `game-design.md` accordingly designs diplomacy from scratch as `[designed, dead end in the original's code]`. The relation model, the legality rules, the war-drags-in-allies propagation, the peace terms and the reparation arithmetic are all confirmable.

Separately, the original already contains a **non-tactical, instant battle resolver**, used whenever no human is involved — which is directly relevant to `game-design.md`'s decision to replace tactical battles with auto-resolve.

## The relation matrix

Each nation record carries a **16-entry `short` array at `+0x26`** holding its relation toward every nation (runtime base `0x00474696`, nation stride `0x494`). `decompiled-turn-and-calendar-sequencing.md` already identified one of the SAV's unlabelled blocks as a 16-entry table; this is the per-nation diplomatic row.

| Value | Meaning |
| ---: | --- |
| `0` | peace |
| `1` | trade |
| `2` | alliance |
| `3` | war |
| `< 0` | peace, plus a **cooldown counter** that must climb back to 0 before trade or alliance is possible again |

`FUN_00449B40(a, b, state)` is the single setter and writes **both** `[a][b]` and `[b][a]` — the matrix is symmetric by construction. Setting `state = 0` (plain peace) is translated into a cooldown instead, by the previous state:

| Previous state | Cooldown written |
| --- | ---: |
| trade (1) | **−8** |
| alliance (2) | **−24** |
| war (3) | **−18** |

Two propagation rules live in the same function:

- Setting **alliance (2)** with `b`: every nation at war with `b` that you are not already at war with becomes **at war with you**.
- Setting **war (3)** with `b`: every nation allied to `b` that you are not already at war with becomes **at war with you**.

The quarterly tick (`FUN_00451B40`, the function in `decompiled-quarterly-billing-and-economy.md`) implements the "gradual diplomatic thaw" that report described in structure only: for each negative entry, `v += 1`, and with probability `1/3`, `v = min(0, v + 3)`. **The thaw loop runs only over the first 8 columns of each row** (`while (sVar6 != 8)`), so a cooldown between two nations both indexed ≥ 8 never decays — an original bug, not a design rule, but one a faithful reimplementation has to decide about consciously.

## Player-initiated diplomacy (`TPolitics`)

`TPolitics_ChangeIR` (`0x00452C9C`) decodes the clicked grid cell into `(nation, newState)` and dispatches:

- **Peace** (`TPolitics_MakePeace` `0x00452D94`): refused with *"`X` does not want to make peace at this time."* if the target is **computer-controlled** (`nation[+0x490] == 0`) **and** currently at war. A human-controlled nation — i.e. another hotseat seat — always accepts.
- **Trade** (`TPolitics_MakeTrade` `0x00452E94`): **maximum 3 trade partners** (*"You can only trade with 3 nations."*); refused if the relation is negative (cooldown, *"`X` does not want to trade with you."*) or greater than 1 (allied or at war, *"You cannot trade with `X`."*), or if the target already has 3 partners and this is a new one.
- **Alliance** (`TPolitics_MakeAlliance` `0x004530D8`): refused against an AI nation if either side is currently at war with anyone, or if the relation is negative. Always accepted from a human seat.
- **War**: set directly, no check.

`TPolitics_OK` (`0x00453230`) commits the row. Notably, if you open trade with a nation that already has three partners, **that nation drops its poorest existing partner** — the one with the lowest nation field `+0x44C` (wealth) — back to peace.

Declaring war is not only done from this screen: `TUnitMap_SelectUnit` auto-declares it. Clicking an enemy city, army or fleet with your own unit selected prompts *"Are you sure you want to attack this …?"* and, on yes, calls `FUN_00449B40(you, them, 3)` before resolving the attack. Attacking **is** declaring war, with the ally-dragging propagation above.

## The peace treaty and the reparation formula

`FUN_00450C68(winner, loser)` is the whole treaty. It is reached from `TBattlePols_Yes` (the post-battle *"After defeating you in battle `X` are willing to end …"* dialog) and, for AI-vs-AI wars, automatically from the instant battle resolver below.

```c
W = nation[loser][+0x44C];                       // wealth (short)
reparations = W/4 + random(W/4) + nation[loser][+0x446] * 10;     // +0x446 = city count

setRelation(winner, loser, 0);                   // → the −18 war cooldown

score(n)  = (nation[n][+0x430] / 100) * nation[n][+0x440];        // population × unity
armies(n) = Σ FUN_0044A8CC(army) over n's armies;                 // total field strength

if (score(winner) < score(loser) || armies(winner) < armies(loser)) {
    news("<winner> and <loser> have agreed to end their war.");   // honourable peace, nothing paid
} else {
    news("<loser> sues <winner> for peace and;");
    news("    <loser> ends all current trading agreements.");
    news("    <loser>  ends all current alliances.");
    news("    <loser> pays reparations of N talents.");
    for each n: if relation[loser][n] is trade or alliance: setRelation(loser, n, -10);
    treasury[loser] -= reparations;  treasury[winner] += reparations;
}
// finally: any ally of either side still at war with the other gets setRelation(..., -8),
// with news "<A> and <B> have agreed to end their war."
```

`TBattlePols_InitializeForm` (`0x004577EC`) previews exactly the same three terms and the same `reparations` expression before the player accepts, which is an independent check that the formula was read correctly.

This is consistent with the one real observation on record — `diplomatic-reparations-and-more-captures.md`'s Ptolemaic treasury going `999 → −1270`, a `−2269` payment — in shape and magnitude, but **the numbers were not independently reproduced**: `nation[+0x44C]` and `nation[+0x446]` for Ptolemaic at that moment were not read back out of the save, and the formula contains a `random(W/4)` term that a single observation cannot pin down anyway. Treating this as *confirmed formula, unverified against the one data point* is the honest label; verifying it is a cheap next check (read the two nation fields from the pre-treaty save, and check `2269` lands in `[W/4 + cities×10, W/2 + cities×10)`).

`THVHBatPols` (`0x00457FA8`) is the human-versus-human equivalent: a straight negotiated payment between two seats, `treasury[A] += amount; treasury[B] -= amount` (`THVHBatPols_OK` `0x00458688`), with no formula at all. It is directly relevant to `game-design.md`'s hotseat design, which currently has no post-battle negotiation step.

## The original's instant battle resolver

`FUN_0044AEE4(attackerArmy, defenderArmy)`, reached from `TUnitMap_SelectUnit` when one army attacks another on the strategic map:

```c
attacker.moves = 0;
if (both nations are computer-controlled) {
    // ---- instant resolution, no tactical battle ----
    pA = FUN_0044A8CC(attacker);  pB = FUN_0044A8CC(defender);   // power = Σ(weight[type]×troops/100)/80 × morale
    winner = (pB < pA) ? attacker : defender;                    // ties go to the defender
    applyCasualties(winner, loserPower * 40 / winnerPower);      // FUN_0044AE20
    winner.money    += loser.money;
    winner.supplies  = min(winner.supplies + loser.supplies, winnerTroops / 100);
    for each surviving unit of the winner:
        quality = max(quality, 6);                               // at least "average"
        if (random(4) == 0) quality = min(quality + 1, 9);       // 1-in-4 promotion
    deleteArmy(loser);
    unity[loserNation] -= 25;   unity[winnerNation] = min(990, unity[winnerNation] + 25);
    news("<winner> destroys army of <loser>.");
    if (random(5) < 2 && unity[loser] > 500 && cities[loser] > 7)
        FUN_00450C68(winnerNation, loserNation);                 // automatic peace + reparations
} else {
    TPremierForm_StartBattle(...);                               // the tactical TBattleMap
}
```

Three things follow that no existing report or design section accounts for:

1. **The original has both battle models.** A human-involved fight goes to the tactical grid; an AI-vs-AI fight is resolved in one shot by a power comparison. `game-design.md`'s choice of instant auto-resolve for *all* battles is therefore closer to the original than that document claims — but the auto-resolve math it proposes (running the tactical melee/shooting exchange loop internally with an invented `"pairing"` rule) is **not** the math the original uses for its own auto-resolve. The original's version is the two functions above: a single deterministic power comparison, total loss for the loser, proportional casualties for the winner.
2. **This is the missing AI-to-AI reparation trigger.** A 2-in-5 chance after any decisive AI-vs-AI field battle, gated on the loser's unity > 500 and city count > 7. That is exactly the kind of event `diplomatic-reparations-and-more-captures.md` observed happening between two AI nations with no player involvement, and it is why searching `TPolitics` for it found nothing.
3. **An explicit quality-promotion instruction exists after all.** `battle-quality-promotion-and-morale-array-decompiled.md` reported that *"None of the recovered battle-related functions contain an explicit 'increment quality' instruction"* — true for the tactical path, but the instant path promotes every surviving unit to at least `6` ("average") and then promotes 1-in-4 further. The empirically-derived adjacency rule in that report applies to the tactical path and is unaffected; this is a second, separate promotion rule on a different code path.

### Naval battles — a third, wholly undocumented combat model

`FUN_0044B5D0(attackerFleet, defenderFleet)`, from clicking an enemy fleet (*"Are you sure you want to attack this fleet ?"*; blocked by *"You cannot attack a fleet docked at its own city !"*):

```c
attacker.moves = 0;
base(f)     = ships × condition / 10 + (f carries an army ? armyPower / 50 : 0);
strength(f) = base(f) + random(4) × (base(f) / 10);        // a 0/10/20/30% random bonus
winner = (strength(defender) < strength(attacker)) ? attacker : defender;   // ties to the defender
unity[loserNation] -= floor(loserShips / 2);
unity[winnerNation] = min(990, unity[winnerNation] + floor(loserShips / 2));
FUN_0044B4F8(winner, winnerStrength, loserStrength);   // damage to the winner
deleteFleet(loser);                                    // and any army it carried
news("<winner> sinks fleet of <loser>.");
```

Winner damage (`FUN_0044B4F8`): with `r = max(1, loserStrength × 100 / winnerStrength)` and `d = r² / 100`, the winner loses `ships × d / 300` ships and `condition × d / 300` condition; a carried army takes casualties (`FUN_0044AE20`), and if `d > 70` it also loses `unitCount × d / 250` whole units at random.

The loser's fleet is destroyed outright regardless of margin — there is no partial naval defeat. Nothing in `docs/game-design.md` covers naval combat at all.

## Victory condition — it is in the code

`game-design.md` lists victory conditions as `[designed, never reverse-engineered]`. `THumanFalls_InitializeForm` (`0x00455E38`), the game-over screen, tests

```c
if (nation[+0x446] < 334)   // city count < the total number of cities
     ... "Your nation has been conquerred by <nation[+0x44E]>." or the year-limit message
else "You have conquerred the Mediterranean, a unique achievement."
```

so the win condition is **holding all 334 cities**, and the screen also handles a time limit: it compares the current year `DAT_004A0332` against `250` (`0xFA`) and reports the reign's length as `270 − year` years, confirming **270 BC start**. It then prints a start-versus-end scorecard from nation fields `+0x430`/`+0x434` (population now/at start), `+0x446`/`+0x448` (cities now/at start) and `+0x438`/`+0x43C` (treasury now/at start).

`TPremierForm_Abdicate` (`0x0045B24C`) and `TPremierForm_HumanLeaderFalls` (`0x0045C238`) both route to the same "nation drops out" handler `FUN_00449078` — voluntary abdication and defeat use one path.

## What this does not establish

- The AI's *decision* to offer or accept diplomacy outside the two triggers above — still unnamed AI code, still out of scope per the roadmap.
- The reparation formula against real save bytes (see the check suggested above).
- Whether the 250 BC comparison is the actual end-of-game trigger or only the game-over screen's wording; the turn loop was not re-read for a year check this pass.
- `nation[+0x44E]` ("conquered by") — inferred from its only use, not otherwise confirmed.
- `FUN_0044AE20`'s exact casualty distribution (already noted as open in `decompiled-defection-and-siege-attrition.md`).

## Reproduction

```text
grep -n "TPolitics_\|TBattlePols_\|THVHBatPols_\|THumanFalls_" delphi_symbols.tsv
# in all_app_functions.txt: FUN_00449b40, FUN_00450c68, FUN_0044aee4, FUN_0044b5d0, FUN_0044b4f8
grep -n "sues \| destroys army of \| sinks fleet of " all_app_functions.txt
```

## Next checks

1. Read `nation[+0x44C]` (wealth) and `nation[+0x446]` (cities) for Ptolemaic out of the save immediately before the `999 → −1270` reparation and check `2269` falls in the formula's range.
2. Search the news logs of existing saves for the literal strings `"sues "`, `" destroys army of "` and `" sinks fleet of "` — every one found is a free confirmation of one of the three resolvers above, with no new play session needed.
3. Re-read the turn loop for a year-250 end-of-game check to confirm or refute the time limit.
