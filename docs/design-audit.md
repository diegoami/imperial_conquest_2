# Design audit (archived)

The audit of the original design (missing mechanics, a review of the existing `[confirmed]`/`[derived]` claims, and ten
open questions for the user) was finished long ago and has been archived. **Full text, frozen:**
[docs/design-audit.md @ archive/desktop-2026-10-20](https://github.com/diegoami/imperial_conquest_2/blob/archive/desktop-2026-10-20/docs/design-audit.md).

The audit's findings live in [game-design.md](game-design.md); a claim that needs its evidence cites the research repository's reports.
**All of Q1–Q10 are answered** (the user's decisions, already folded into `game-design.md`); none is open:

| Q | Question | Decision |
| --- | --- | --- |
| Q1 | Which battle model should auto-resolve actually use? | option A, the original's instant resolver; extended by v0.6.0 to the original's two models |
| Q2 | Is the naval subsystem in scope for a first playable version? | yes, full naval |
| Q3 | How faithful should diplomacy be, now that the original's model is recoverable? | ship both, as a ruleset flag |
| Q4 | Keep per-army and per-fleet money purses? | ship both, as the same ruleset flag family |
| Q5 | Should "conquer every city" be the shipped default victory condition? | yes, as the `classical-faithful` default; `improved` defaults friendlier |
| Q6 | Reproduce the original's human-versus-AI asymmetries? | `classical-faithful` reproduces them, `improved` normalises them |
| Q7 | Is city development a direction to expand, or stay at exactly one order? | leave the door open |
| Q8 | What is the policy on reproducing original bugs? | `classical-faithful` reproduces them, `improved` fixes them silently |
| Q9 |  |  |
| Q10 | Does the newly-found rout mechanic change `combat.onDefeat`'s `improved` design? | no change |
