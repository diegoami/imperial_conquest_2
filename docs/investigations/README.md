# Investigations

This repository's own evidence write-ups: questions the research repository's reports left open that a build task or an evidence pass had to settle before code could be written against them. They are cited as provenance exactly like a research-repo report ([build-process.md §4.2](../build-process.md#42-what-the-reviewer-checks) gate 2), and use the same tagging convention as `design-audit.md`: **[confirmed]**, **[derived]**, **[designed]**, **[open]**.

A new investigation gets a row here in the same documentation update that lands it ([build-process.md §4.8](../build-process.md#48-documentation-update-after-every-merge) part B).

| Document | Question | Result | Consumed by |
| --- | --- | --- | --- |
| [dat-file-layout.md](dat-file-layout.md) | What is the DAT's own file layout, and does it contain a nation table? | Solved: not a SAV with another extension — it diverges after the shared map and city table, has no record-count words, a 1,055-byte nation record and a full 16-nation table; the complete read order accounts for all 140,706 bytes. | T30, T29, T21 |
| [siege-defender-strength.md](siege-defender-strength.md) | What does `FUN_0044A98C`, the siege defender-strength function, compute, and which fields do its weights belong to? | Solved: `loyalty × 150 + finishedFortificationPercent × 250 + populationThousands × 200`, followed by two separate adjustments in two functions; T02's `Ruleset.Siege` had the field identities wrong. | T31, T07, T16, T17 |
| [thracia-supply-morale.md](thracia-supply-morale.md) | Does supply affect army morale? | Solved: yes — once per turn in the turn tick `FUN_004514EC`, by supply percentage, with a dead band and hard 51…70 clamps; one save is one turn is two weeks. | T06, T07, T08, T14 |
