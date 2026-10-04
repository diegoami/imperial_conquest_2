| Clause | Band | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- | --- |
| CS-T | ≥ 0.20 | 1.0000 — **pass** | 0.9440 — **pass** | 1.0000 — **pass** | 0.9604 — **pass** | 0.9454 — **pass** |
| CS-P | ≥ 0.20 | 0.0000 — **FAIL** | 0.9188 — **pass** | 0.9500 — **pass** | 0.9526 — **pass** | 0.9156 — **pass** |
| NT | ≥ 1 three-cycle and no dominant mix | 0 three-cycles; 0 edges; dominant: none — **FAIL** | 10 three-cycles; 173 edges; dominant: none — **pass** | 60 three-cycles; 210 edges; dominant: none — **pass** | 33 three-cycles; 206 edges; dominant: LI — **FAIL** | 0 three-cycles; 174 edges; dominant: none — **FAIL** |
| UR(0.90) | [0.1, 0.45] | 0.0000 (0/2400) — **FAIL** | 0.2846 (683/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.1342 (322/2400) — **pass** | 0.2829 (679/2400) — **pass** |
| UR(0.80) | [0.03, 0.35] | 0.0000 (0/2400) — **FAIL** | 0.1813 (435/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0063 (15/2400) — **FAIL** | 0.1796 (431/2400) — **pass** |
| UR(0.67) | [0.005, 0.2] | 0.0000 (0/2400) — **FAIL** | 0.0767 (184/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0000 (0/2400) — **FAIL** | 0.0692 (166/2400) — **pass** |
| UR(0.50) | [0, 0.08] | 0.0000 (0/2400) — **pass** | 0.0088 (21/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** |
| UR-monotone | non-increasing as κ falls (±0.02) | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** |
| UR | every UR clause | outside a band — **FAIL** | all bands — **pass** | outside a band — **FAIL** | outside a band — **FAIL** | all bands — **pass** |
| CD-a | ≥ 0.15 | 0.0333 (median of 25200) — **FAIL** | 0.8928 (median of 24169) — **pass** | 0.3632 (median of 19200) — **pass** | 0.2039 (median of 23490) — **pass** | 0.9124 (median of 25157) — **pass** |
| CD-b | ≥ 2 distinct clear-top types | 1 distinct clear-top types (LC) — **FAIL** | 4 distinct clear-top types (A, HC, LC, LI) — **pass** | 1 distinct clear-top types (A) — **FAIL** | 1 distinct clear-top types (A) — **FAIL** | 4 distinct clear-top types (A, HC, LC, LI) — **pass** |
| EN-a | cap ≤ 0.05 | n/a | 0.0000 — **pass** | n/a | 0.0000 — **pass** | 0.0000 — **pass** |
| EN-b | collapse + withdrawal in [0.10, 0.90] | n/a | 0.7801 — **pass** | n/a | 1.0000 — **FAIL** | 0.9999 — **FAIL** |
| EN-c | cascade in [0.10, 0.90] | n/a | 0.5975 — **pass** | n/a | n/a | 0.3257 — **pass** |
| SV-a | ≥ 0.10 | 0.6463 (median σ) — **pass** | 0.0000 (median σ) — **FAIL** | 0.5591 (median σ) — **pass** | 0.5980 (median σ) — **pass** | 0.2936 (median σ) — **pass** |
| SV-b | ≥ 0.10 | -0.0008 (median 1−σ 0.3537 − median ω 0.3545) — **FAIL** | 0.6370 (median 1−σ 1.0000 − median ω 0.3630) — **pass** | 0.1641 (median 1−σ 0.4409 − median ω 0.2768) — **pass** | 0.2211 (median 1−σ 0.4020 − median ω 0.1809) — **pass** | 0.3699 (median 1−σ 0.7064 − median ω 0.3365) — **pass** |
| SV-c | ≥ 0.05 | -0.0001 (σ(Z) 0.6461 over 25200 − σ(H) 0.6462 over 50400) — **FAIL** | 0.0000 (σ(Z) 0.0000 over 23126 − σ(H) 0.0000 over 53563) — **FAIL** | -0.0424 (σ(Z) 0.4649 over 26800 − σ(H) 0.5073 over 50400) — **FAIL** | 0.0657 (σ(Z) 0.6168 over 30864 − σ(H) 0.5510 over 43819) — **pass** | 0.0731 (σ(Z) 0.3467 over 23927 − σ(H) 0.2736 over 51740) — **pass** |
| SV-d | ≥ 0.05 | 0.0029 (mean τ over 88200 battles with survivors) — **FAIL** | undefined (mean τ over 0 battles with survivors) — **undefined** | 0.1770 (mean τ over 86997 battles with survivors) — **pass** | 0.0888 (mean τ over 88200 battles with survivors) — **pass** | 0.3029 (mean τ over 88200 battles with survivors) — **pass** |
| DET | 100/100 byte-identical (two processes) and 100/100 draws as stated; no other randomness source | 100/100 byte-identical across two processes; draw count as stated: 0/100 — **FAIL** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** |
| COST mean t_c | (reported) | 0.0116 ms | 0.0154 ms | 0.0035 ms | 0.0051 ms | 0.0128 ms |
| COST p99 | ≤ 50 ms | 0.0564 ms — **pass** | 0.0474 ms — **pass** | 0.0081 ms — **pass** | 0.0119 ms — **pass** | 0.0299 ms — **pass** |
| COST E_c = E0 + B × (t_c − t_1) | ≤ 240 s | 1.7300 s — **pass** | 1.7302 s — **pass** | 1.7296 s — **pass** | 1.7297 s — **pass** | 1.7301 s — **pass** |

Per-composition score s(A), T-scale / P-scale:

| Composition | C1 s_T / s_P | C2 s_T / s_P | C3 s_T / s_P | C4 s_T / s_P | C5 s_T / s_P |
| --- | --- | --- | --- | --- | --- |
| LI | 0.000 / 0.500 | 0.102 / 0.333 | 0.000 / 0.750 | 0.099 / 1.000 | 0.020 / 0.350 |
| HI | 0.900 / 0.500 | 0.966 / 0.922 | 0.900 / 0.900 | 0.889 / 0.605 | 0.965 / 0.921 |
| A | 0.125 / 0.500 | 0.022 / 0.003 | 0.100 / 0.150 | 0.016 / 0.287 | 0.079 / 0.005 |
| LC | 0.375 / 0.500 | 0.642 / 0.792 | 0.700 / 0.950 | 0.721 / 0.663 | 0.558 / 0.770 |
| HC | 1.000 / 0.500 | 0.850 / 0.498 | 1.000 / 0.550 | 0.976 / 0.071 | 0.914 / 0.487 |
| LI+HI | 0.375 / 0.500 | 0.533 / 0.522 | 0.550 / 0.550 | 0.538 / 0.807 | 0.503 / 0.498 |
| LI+A | 0.050 / 0.500 | 0.044 / 0.230 | 0.050 / 0.550 | 0.053 / 0.820 | 0.057 / 0.261 |
| LI+LC | 0.125 / 0.500 | 0.256 / 0.391 | 0.250 / 0.600 | 0.340 / 0.883 | 0.196 / 0.361 |
| LI+HC | 0.575 / 0.500 | 0.544 / 0.461 | 0.200 / 0.200 | 0.327 / 0.684 | 0.538 / 0.428 |
| HI+A | 0.575 / 0.500 | 0.677 / 0.732 | 0.500 / 0.300 | 0.497 / 0.189 | 0.702 / 0.806 |
| HI+LC | 0.675 / 0.500 | 0.741 / 0.824 | 0.800 / 0.850 | 0.812 / 0.592 | 0.695 / 0.823 |
| HI+HC | 0.950 / 0.500 | 0.915 / 0.832 | 0.950 / 0.850 | 0.939 / 0.387 | 0.908 / 0.811 |
| A+LC | 0.250 / 0.500 | 0.180 / 0.185 | 0.350 / 0.350 | 0.276 / 0.364 | 0.194 / 0.140 |
| A+HC | 0.675 / 0.500 | 0.409 / 0.180 | 0.400 / 0.000 | 0.232 / 0.047 | 0.471 / 0.139 |
| LC+HC | 0.800 / 0.500 | 0.775 / 0.726 | 0.850 / 0.700 | 0.824 / 0.315 | 0.789 / 0.705 |
| uniform | 0.500 / 0.500 | 0.540 / 0.411 | 0.450 / 0.350 | 0.486 / 0.482 | 0.533 / 0.446 |
| LI-heavy | 0.200 / 0.500 | 0.262 / 0.450 | 0.150 / 0.500 | 0.275 / 0.885 | 0.230 / 0.483 |
| HI-heavy | 0.750 / 0.500 | 0.709 / 0.715 | 0.750 / 0.650 | 0.720 / 0.363 | 0.730 / 0.746 |
| A-heavy | 0.300 / 0.500 | 0.258 / 0.199 | 0.300 / 0.150 | 0.215 / 0.366 | 0.290 / 0.263 |
| LC-heavy | 0.450 / 0.500 | 0.400 / 0.504 | 0.600 / 0.550 | 0.597 / 0.558 | 0.417 / 0.466 |
| HC-heavy | 0.850 / 0.500 | 0.677 / 0.592 | 0.650 / 0.050 | 0.670 / 0.132 | 0.712 / 0.591 |

CD-b clear-top type in the uniform winner, per opponent:

| Opponent | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| LI | LC | none | no data | no data | LI |
| HI | LC | no data | no data | none | no data |
| A | LC | LC | A | A | LC |
| LC | LC | none | no data | none | LI |
| HC | LC | none | no data | none | LI |
| LI+HI | LC | none | no data | no data | none |
| LI+A | LC | LI | no data | no data | LI |
| LI+LC | LC | none | no data | no data | none |
| LI+HC | LC | none | A | A | LI |
| HI+A | LC | none | A | A | no data |
| HI+LC | LC | none | no data | none | LI |
| HI+HC | LC | none | no data | none | LI |
| A+LC | LC | A | A | A | A |
| A+HC | LC | A | A | none | A |
| LC+HC | LC | LI | no data | none | LI |
| uniform | LC | LI | A | A | LI |
| LI-heavy | LC | LI | no data | no data | LI |
| HI-heavy | LC | none | no data | none | none |
| A-heavy | LC | LI | A | A | LI |
| LC-heavy | LC | LI | no data | none | LI |
| HC-heavy | LC | HC | A | none | HC |

Endings over the P-scale schedule, and mean σ by ending (§8.8 diagnostic, no band):

| Ending | C1 share / mean σ | C2 share / mean σ | C3 share / mean σ | C4 share / mean σ | C5 share / mean σ |
| --- | --- | --- | --- | --- | --- |
| annihilation | 0.0000 | 0.2199 / 0.0000 | 0.0000 | 0.0000 | 0.0001 / 0.0819 |
| cap | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 |
| collapse | 0.0000 | 0.7801 / 0.0000 | 0.0000 | 1.0000 / 0.5818 | 0.0135 / 0.3586 |
| decided | 1.0000 / 0.6462 | 0.0000 | 1.0000 / 0.4999 | 0.0000 | 0.0000 |
| withdrawal | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.9864 / 0.2956 |

Diagnostics (no band):

| Diagnostic | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| attacker win rate, P-scale | 0.0000 | 0.3912 | 0.4762 | 0.4971 | 0.3867 |
| attacker win rate, T-scale | 0.4671 | 0.4238 | 0.4762 | 0.4991 | 0.4289 |
| battle-phase draws per P-scale battle (mean) | 18.6 | 406.7 | 18.6 | 85.3 | 385.4 |
| battle-phase draws per P-scale battle (min..max) | 8..36 | 144..1296 | 8..36 | 6..222 | 95..1382 |
| draw-formula mismatches (all battles) | 186000 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 |
| rounds per P-scale battle (mean) | 0.00 | 12.99 | 0.00 | 5.31 | 9.91 |
