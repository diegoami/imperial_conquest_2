## §9.1 Seleucid 45,100 v Ptolemaic 27,700 ([designed] rosters: §8.0 split, q 6, M 59, Seleucid attacks)

Seleucid units: light_infantry 15,000, light_infantry 12,300, heavy_infantry 6,000, heavy_infantry 2,400, archers 3,500, archers 1,700, light_cavalry 1,800, heavy_cavalry 2,400; Ptolemaic units: light_infantry 15,000, light_infantry 6,300, heavy_infantry 3,200, light_cavalry 2,300, heavy_cavalry 900

| Candidate | smoke verdict | Seleucid (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.172 / 0.177 / 0.182 | 0.171 / 0.178 / 0.186 | 0.169 / 0.176 / 0.186 | 0.165 / 0.173 / 0.185 | 0.167 / 0.178 / 0.189 | 0.167 / 0.175 / 0.183 | 0.000 |
| C2 | pass | 0.353 | 0.517 / 0.672 / 0.993 | 0.829 / 1.000 / 1.000 | 0.010 / 0.253 / 1.000 | 0.070 / 0.155 / 1.000 | 0.000 / 0.005 / 1.000 | 0.000 / 0.000 / 0.872 | 0.416 (LI 203, LI+A 49, LI+HC 66, LI+HI 5, LI+HI+A 26, LI+HI+A+LC 53, LI+HI+HC 14) |
| C3 | pass | 1.000 | 0.171 / 0.177 / 0.184 | 0.214 / 0.223 / 0.233 | 0.081 / 0.085 / 0.090 | 0.132 / 0.138 / 0.148 | 0.172 / 0.183 / 0.195 | 0.060 / 0.063 / 0.065 | 0.000 |
| C4 | pass | 1.000 | 0.071 / 0.093 / 0.120 | 0.089 / 0.116 / 0.149 | 0.036 / 0.051 / 0.070 | 0.055 / 0.070 / 0.088 | 0.072 / 0.094 / 0.122 | 0.026 / 0.035 / 0.047 | 0.000 |
| C5 | pass | 0.991 | 0.394 / 0.498 / 0.530 | 0.651 / 0.823 / 0.876 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 |

Seat swap (measured, no band): the same two rosters and seeds `k = 0 … 999`, Seleucid attacking and then defending.

| Candidate | Seleucid wins attacking | Seleucid wins defending |
| --- | --- | --- |
| C1 | 1.000 | 1.000 |
| C2 | 0.353 | 0.995 |
| C3 | 1.000 | 1.000 |
| C4 | 1.000 | 1.000 |
| C5 | 0.991 | 1.000 |

## §9.2 Rome v Gaul, `1_rome_270_winter_7.sav` (rosters read from the save; Rome attacks)

Roster assertions: all passed (totals, HC 755 + 2,432, 4th Bowmen 3,312, slot qualities, Rome M = 68). Owners: army 0 Rome, army 13 Gaul. Gaul's +14 = 62.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.182 / 0.186 / 0.189 | 0.180 / 0.187 / 0.193 | 0.180 / 0.186 / 0.190 | 0.179 / 0.184 / 0.191 | 0.176 / 0.188 / 0.198 | 0.171 / 0.178 / 0.191 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 80,751–81,948) |
| C2 | pass | 0.994 | 0.478 / 0.571 / 0.700 | 0.474 / 0.510 / 0.673 | 0.491 / 0.689 / 0.872 | 0.399 / 0.532 / 0.741 | 0.246 / 0.363 / 0.573 | 0.786 / 1.000 / 1.000 | 0.520 (HC 496, HI 3, HI+A 1, HI+A+LC+HC 2, HI+HC 1, HI+LC 1, HI+LC+HC 4, LC 3, LC+HC 9) | 63,282: 1.000; 75,536: 1.000 (range 14,177–61,970) |
| C3 | pass | 1.000 | 0.208 / 0.212 / 0.217 | 0.278 / 0.288 / 0.297 | 0.111 / 0.114 / 0.117 | 0.188 / 0.193 / 0.200 | 0.219 / 0.234 / 0.246 | 0.075 / 0.078 / 0.084 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 77,851–79,447) |
| C4 | pass | 1.000 | 0.146 / 0.181 / 0.226 | 0.192 / 0.235 / 0.291 | 0.095 / 0.124 / 0.164 | 0.121 / 0.143 / 0.171 | 0.157 / 0.193 / 0.239 | 0.059 / 0.076 / 0.098 | 0.000 | 63,282: 0.000; 75,536: 0.021 (range 72,153–89,081) |
| C5 | pass | 1.000 | 0.202 / 0.257 / 0.277 | 0.318 / 0.357 / 0.385 | 0.000 / 0.044 / 0.086 | 0.089 / 0.198 / 0.226 | 0.226 / 0.342 / 0.470 | 0.674 / 0.758 / 0.762 | 0.000 | 63,282: 0.000; 75,536: 0.711 (range 65,421–81,948) |

## §9.3 Rome v Gaul, `7.sav → 8.sav`

Exact-total lookup in `7.sav`: 1 army record(s) match Rome's start column, 1 match Gaul's.
Path taken: **[confirmed by exact match]** — Rome's column matches army 0 (Rome) at (102,44), M 70; Gaul's matches army 9 (Gaul) at (103,43), M 68.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.130 / 0.132 / 0.135 | 0.126 / 0.133 / 0.138 | 0.130 / 0.133 / 0.136 | 0.119 / 0.125 / 0.138 | 0.124 / 0.128 / 0.134 | 0.000 | 39,941: 0.000 (range 43,770–44,235) |
| C2 | pass | 1.000 | 0.353 / 0.423 / 0.497 | 0.259 / 0.389 / 0.542 | 0.288 / 0.394 / 0.478 | 1.000 / 1.000 / 1.000 | 0.415 / 0.427 / 0.437 | 1.000 (LC 1000) | 39,941: 1.000 (range 21,456–36,876) |
| C3 | pass | 0.000 | — | — | — | — | — | 0.000 | 39,941: no attacker win |
| C4 | pass | 0.000 | — | — | — | — | — | 0.000 | 39,941: no attacker win |
| C5 | pass | 1.000 | 0.176 / 0.198 / 0.216 | 0.204 / 0.320 / 0.412 | 0.082 / 0.093 / 0.096 | 0.941 / 0.974 / 0.991 | 0.320 / 0.364 / 0.379 | 0.000 | 39,941: 0.124 (range 39,287–42,766) |

