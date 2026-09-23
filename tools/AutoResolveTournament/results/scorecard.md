| Clause | Band | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- | --- |
| CS-T | ≥ 0.20 | 1.0000 — **pass** | 0.7711 — **pass** | 0.9000 — **pass** | 0.9216 — **pass** | 0.9061 — **pass** |
| CS-P | ≥ 0.20 | 0.0000 — **FAIL** | 0.7535 — **pass** | 1.0000 — **pass** | 0.9975 — **pass** | 0.6596 — **pass** |
| NT | ≥ 1 three-cycle and no dominant mix | 0 three-cycles; 0 edges; dominant: none — **FAIL** | 148 three-cycles; 190 edges; dominant: none — **pass** | 0 three-cycles; 210 edges; dominant: A — **FAIL** | 2 three-cycles; 207 edges; dominant: LI — **FAIL** | 109 three-cycles; 181 edges; dominant: none — **pass** |
| UR(0.90) | [0.1, 0.45] | 0.0000 (0/2400) — **FAIL** | 0.2942 (706/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0258 (62/2400) — **FAIL** | 0.2850 (684/2400) — **pass** |
| UR(0.80) | [0.03, 0.35] | 0.0000 (0/2400) — **FAIL** | 0.1829 (439/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0000 (0/2400) — **FAIL** | 0.1788 (429/2400) — **pass** |
| UR(0.67) | [0.005, 0.2] | 0.0000 (0/2400) — **FAIL** | 0.0771 (185/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0000 (0/2400) — **FAIL** | 0.0688 (165/2400) — **pass** |
| UR(0.50) | [0, 0.08] | 0.0000 (0/2400) — **pass** | 0.0075 (18/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** |
| UR-monotone | non-increasing as κ falls (±0.02) | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** |
| UR | every UR clause | outside a band — **FAIL** | all bands — **pass** | outside a band — **FAIL** | outside a band — **FAIL** | all bands — **pass** |
| CD-a | ≥ 0.15 | 0.0333 (median of 25200) — **FAIL** | 0.8096 (median of 29261) — **pass** | 0.1734 (median of 26400) — **pass** | 0.0888 (median of 28202) — **FAIL** | 0.8138 (median of 29622) — **pass** |
| CD-b | ≥ 2 distinct clear-top types | 1 distinct clear-top types (LC) — **FAIL** | 3 distinct clear-top types (HC, LC, LI) — **pass** | 1 distinct clear-top types (LI) — **FAIL** | 2 distinct clear-top types (A, LI) — **pass** | 2 distinct clear-top types (HC, LI) — **pass** |
| EN-a | cap ≤ 0.05 | n/a | 0.0000 — **pass** | n/a | 0.1777 — **FAIL** | 0.0000 — **pass** |
| EN-b | collapse + withdrawal in [0.10, 0.90] | n/a | 0.6994 — **pass** | n/a | 0.8223 — **pass** | 0.9997 — **FAIL** |
| EN-c | cascade in [0.10, 0.90] | n/a | 0.4731 — **pass** | n/a | n/a | 0.2436 — **pass** |
| SV-a | ≥ 0.10 | 0.6463 (median σ) — **pass** | 0.0000 (median σ) — **FAIL** | 0.4099 (median σ) — **pass** | 0.6530 (median σ) — **pass** | 0.3084 (median σ) — **pass** |
| SV-b | ≥ 0.10 | -0.0008 (median 1−σ 0.3537 − median ω 0.3545) — **FAIL** | 0.5856 (median 1−σ 1.0000 − median ω 0.4144) — **pass** | 0.3838 (median 1−σ 0.5901 − median ω 0.2063) — **pass** | 0.2352 (median 1−σ 0.3470 − median ω 0.1118) — **pass** | 0.3513 (median 1−σ 0.6916 − median ω 0.3403) — **pass** |
| SV-c | ≥ 0.05 | -0.0001 (σ(Z) 0.6461 over 25200 − σ(H) 0.6462 over 50400) — **FAIL** | 0.0000 (σ(Z) 0.0000 over 18282 − σ(H) 0.0000 over 55871) — **FAIL** | -0.0527 (σ(Z) 0.3351 over 33200 − σ(H) 0.3878 over 39600) — **FAIL** | 0.0193 (σ(Z) 0.6716 over 29309 − σ(H) 0.6523 over 43800) — **FAIL** | 0.0857 (σ(Z) 0.3694 over 20065 − σ(H) 0.2838 over 53367) — **pass** |
| SV-d | ≥ 0.05 | 0.0029 (mean τ over 88200 battles with survivors) — **FAIL** | undefined (mean τ over 0 battles with survivors) — **undefined** | 0.1627 (mean τ over 78182 battles with survivors) — **pass** | 0.0380 (mean τ over 88200 battles with survivors) — **FAIL** | 0.2518 (mean τ over 88200 battles with survivors) — **pass** |
| DET | 100/100 byte-identical (two processes) and 100/100 draws as stated; no other randomness source | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** |
| COST mean t_c | (reported) | 0.0106 ms | 0.0147 ms | 0.0032 ms | 0.0108 ms | 0.0119 ms |
| COST p99 | ≤ 50 ms | 0.0388 ms — **pass** | 0.0511 ms — **pass** | 0.0067 ms — **pass** | 0.0351 ms — **pass** | 0.0346 ms — **pass** |
| COST E_c = E0 + B × (t_c − t_1) | ≤ 240 s | 1.9000 s — **pass** | 1.9006 s — **pass** | 1.8989 s — **pass** | 1.9000 s — **pass** | 1.9002 s — **pass** |

Per-composition score s(A), T-scale / P-scale:

| Composition | C1 s_T / s_P | C2 s_T / s_P | C3 s_T / s_P | C4 s_T / s_P | C5 s_T / s_P |
| --- | --- | --- | --- | --- | --- |
| LI | 0.000 / 0.500 | 0.215 / 0.514 | 0.150 / 0.700 | 0.077 / 1.000 | 0.003 / 0.535 |
| HI | 0.900 / 0.500 | 0.587 / 0.260 | 0.150 / 0.100 | 0.542 / 0.093 | 0.800 / 0.258 |
| A | 0.125 / 0.500 | 0.116 / 0.005 | 0.900 / 1.000 | 0.087 / 0.477 | 0.138 / 0.082 |
| LC | 0.375 / 0.500 | 0.574 / 0.497 | 0.650 / 0.350 | 0.976 / 0.621 | 0.535 / 0.460 |
| HC | 1.000 / 0.500 | 0.887 / 0.650 | 0.250 / 0.000 | 0.849 / 0.003 | 0.909 / 0.634 |
| LI+HI | 0.375 / 0.500 | 0.515 / 0.611 | 0.100 / 0.450 | 0.361 / 0.780 | 0.593 / 0.602 |
| LI+A | 0.050 / 0.500 | 0.144 / 0.531 | 0.750 / 0.950 | 0.054 / 0.936 | 0.064 / 0.597 |
| LI+LC | 0.125 / 0.500 | 0.433 / 0.524 | 0.350 / 0.600 | 0.677 / 0.862 | 0.257 / 0.489 |
| LI+HC | 0.575 / 0.500 | 0.656 / 0.759 | 0.200 / 0.400 | 0.522 / 0.770 | 0.661 / 0.722 |
| HI+A | 0.575 / 0.500 | 0.383 / 0.214 | 0.950 / 0.800 | 0.215 / 0.228 | 0.500 / 0.284 |
| HI+LC | 0.675 / 0.500 | 0.516 / 0.377 | 0.500 / 0.200 | 0.794 / 0.342 | 0.555 / 0.363 |
| HI+HC | 0.950 / 0.500 | 0.836 / 0.478 | 0.050 / 0.050 | 0.723 / 0.054 | 0.857 / 0.400 |
| A+LC | 0.250 / 0.500 | 0.246 / 0.429 | 0.800 / 0.850 | 0.414 / 0.501 | 0.219 / 0.429 |
| A+HC | 0.675 / 0.500 | 0.514 / 0.422 | 0.850 / 0.750 | 0.278 / 0.158 | 0.562 / 0.416 |
| LC+HC | 0.800 / 0.500 | 0.871 / 0.723 | 0.550 / 0.150 | 0.922 / 0.301 | 0.855 / 0.676 |
| uniform | 0.500 / 0.500 | 0.552 / 0.621 | 0.600 / 0.550 | 0.503 / 0.651 | 0.545 / 0.627 |
| LI-heavy | 0.200 / 0.500 | 0.485 / 0.676 | 0.300 / 0.650 | 0.321 / 0.902 | 0.364 / 0.672 |
| HI-heavy | 0.750 / 0.500 | 0.553 / 0.365 | 0.450 / 0.300 | 0.538 / 0.374 | 0.638 / 0.357 |
| A-heavy | 0.300 / 0.500 | 0.246 / 0.641 | 0.850 / 0.900 | 0.185 / 0.535 | 0.261 / 0.742 |
| LC-heavy | 0.450 / 0.500 | 0.388 / 0.473 | 0.700 / 0.500 | 0.783 / 0.630 | 0.392 / 0.438 |
| HC-heavy | 0.850 / 0.500 | 0.785 / 0.733 | 0.400 / 0.250 | 0.682 / 0.283 | 0.792 / 0.718 |

CD-b clear-top type in the uniform winner, per opponent:

| Opponent | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| LI | LC | LI | no data | no data | LI |
| HI | LC | HC | none | none | HC |
| A | LC | LC | no data | A | none |
| LC | LC | HC | none | none | none |
| HC | LC | none | none | none | none |
| LI+HI | LC | none | LI | no data | none |
| LI+A | LC | LI | no data | no data | LI |
| LI+LC | LC | HC | no data | no data | none |
| LI+HC | LC | HC | LI | no data | HC |
| HI+A | LC | LI | no data | A | none |
| HI+LC | LC | HC | none | none | HC |
| HI+HC | LC | LI | none | none | HC |
| A+LC | LC | HC | no data | A | HC |
| A+HC | LC | none | no data | A | LI |
| LC+HC | LC | none | none | none | HC |
| uniform | LC | LI | LI | LI | LI |
| LI-heavy | LC | HC | no data | no data | HC |
| HI-heavy | LC | HC | none | none | HC |
| A-heavy | LC | none | no data | A | none |
| LC-heavy | LC | LI | LI | LI | none |
| HC-heavy | LC | none | none | none | none |

Endings over the P-scale schedule, and mean σ by ending (§8.8 diagnostic, no band):

| Ending | C1 share / mean σ | C2 share / mean σ | C3 share / mean σ | C4 share / mean σ | C5 share / mean σ |
| --- | --- | --- | --- | --- | --- |
| annihilation | 0.0000 | 0.3006 / 0.0000 | 0.0000 | 0.0000 | 0.0003 / 0.0634 |
| cap | 0.0000 | 0.0000 | 0.0000 | 0.1777 / 0.7134 | 0.0000 |
| collapse | 0.0000 | 0.6994 / 0.0000 | 0.0000 | 0.8223 / 0.6473 | 0.0174 / 0.3490 |
| decided | 1.0000 / 0.6462 | 0.0000 | 1.0000 / 0.3645 | 0.0000 | 0.0000 |
| withdrawal | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.9823 / 0.3087 |

Diagnostics (no band):

| Diagnostic | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| attacker win rate, P-scale | 0.0000 | 0.4538 | 0.4762 | 0.4983 | 0.4533 |
| attacker win rate, T-scale | 0.4671 | 0.4277 | 0.4762 | 0.4954 | 0.4373 |
| battle-phase draws per P-scale battle (mean) | 18.6 | 435.0 | 18.6 | 110.3 | 387.8 |
| battle-phase draws per P-scale battle (min..max) | 8..36 | 118..1270 | 8..36 | 12..254 | 80..1497 |
| draw-formula mismatches (all battles) | 0 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 |
| rounds per P-scale battle (mean) | 0.00 | 13.47 | 0.00 | 17.88 | 9.57 |
