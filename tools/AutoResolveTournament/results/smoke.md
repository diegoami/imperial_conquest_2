## §9.1 Seleucid 45,100 v Ptolemaic 27,700 ([designed] rosters: §8.0 split, q 6, M 59, Seleucid attacks)

Seleucid units: light_infantry 15,000, light_infantry 12,300, heavy_infantry 6,000, heavy_infantry 2,400, archers 3,500, archers 1,700, light_cavalry 1,800, heavy_cavalry 2,400; Ptolemaic units: light_infantry 15,000, light_infantry 6,300, heavy_infantry 3,200, light_cavalry 2,300, heavy_cavalry 900

| Candidate | smoke verdict | Seleucid (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.172 / 0.177 / 0.182 | 0.171 / 0.178 / 0.186 | 0.169 / 0.176 / 0.186 | 0.165 / 0.173 / 0.185 | 0.167 / 0.178 / 0.189 | 0.167 / 0.175 / 0.183 | 0.000 |
| C2 | pass | 1.000 | 0.646 / 0.706 / 0.748 | 1.000 / 1.000 / 1.000 | 0.026 / 0.082 / 0.235 | 0.250 / 0.613 / 1.000 | 0.000 / 0.006 / 1.000 | 0.000 / 0.000 / 0.033 | 0.974 (LC 9, LI 789, LI+A 114, LI+A+LC 1, LI+LC 61) |
| C3 | pass | 1.000 | 0.180 / 0.186 / 0.193 | 0.220 / 0.229 / 0.239 | 0.052 / 0.054 / 0.057 | 0.252 / 0.264 / 0.281 | 0.093 / 0.099 / 0.105 | 0.050 / 0.052 / 0.055 | 0.000 |
| C4 | pass | 1.000 | 0.075 / 0.121 / 0.187 | 0.093 / 0.150 / 0.230 | 0.018 / 0.033 / 0.055 | 0.103 / 0.172 / 0.264 | 0.051 / 0.074 / 0.107 | 0.021 / 0.035 / 0.055 | 0.000 |
| C5 | pass | 1.000 | 0.217 / 0.321 / 0.368 | 0.359 / 0.531 / 0.608 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 |

Seat swap (measured, no band): the same two rosters and seeds `k = 0 … 999`, Seleucid attacking and then defending.

| Candidate | Seleucid wins attacking | Seleucid wins defending |
| --- | --- | --- |
| C1 | 1.000 | 1.000 |
| C2 | 1.000 | 1.000 |
| C3 | 1.000 | 1.000 |
| C4 | 1.000 | 1.000 |
| C5 | 1.000 | 1.000 |

## §9.2 Rome v Gaul, `1_rome_270_winter_7.sav` (rosters read from the save; Rome attacks)

Roster assertions: all passed (totals, HC 755 + 2,432, 4th Bowmen 3,312, slot qualities, Rome M = 68). Owners: army 0 Rome, army 13 Gaul. Gaul's +14 = 62.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.182 / 0.186 / 0.189 | 0.180 / 0.187 / 0.193 | 0.180 / 0.186 / 0.190 | 0.179 / 0.184 / 0.191 | 0.176 / 0.188 / 0.198 | 0.171 / 0.178 / 0.191 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 80,751–81,948) |
| C2 | pass | 1.000 | 0.247 / 0.285 / 0.334 | 0.249 / 0.310 / 0.393 | 0.030 / 0.069 / 0.110 | 0.399 / 0.404 / 0.416 | 0.273 / 0.473 / 0.733 | 0.776 / 0.802 / 0.847 | 0.004 (HC 4) | 63,282: 0.006; 75,536: 0.963 (range 60,032–79,672) |
| C3 | pass | 1.000 | 0.173 / 0.177 / 0.181 | 0.225 / 0.233 / 0.241 | 0.057 / 0.058 / 0.060 | 0.267 / 0.275 / 0.284 | 0.101 / 0.108 / 0.113 | 0.052 / 0.054 / 0.058 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 81,555–82,898) |
| C4 | pass | 1.000 | 0.104 / 0.171 / 0.247 | 0.140 / 0.227 / 0.323 | 0.028 / 0.054 / 0.085 | 0.155 / 0.260 / 0.374 | 0.084 / 0.121 / 0.164 | 0.033 / 0.056 / 0.084 | 0.000 | 63,282: 0.000; 75,536: 0.056 (range 70,287–90,801) |
| C5 | pass | 1.000 | 0.153 / 0.185 / 0.224 | 0.152 / 0.205 / 0.263 | 0.001 / 0.004 / 0.013 | 0.162 / 0.210 / 0.248 | 0.295 / 0.511 / 0.750 | 0.579 / 0.697 / 0.762 | 0.000 | 63,282: 0.000; 75,536: 0.008 (range 73,937–87,137) |

## §9.3 Rome v Gaul, `7.sav → 8.sav`

Exact-total lookup in `7.sav`: 1 army record(s) match Rome's start column, 1 match Gaul's.
Path taken: **[confirmed by exact match]** — Rome's column matches army 0 (Rome) at (102,44), M 70; Gaul's matches army 9 (Gaul) at (103,43), M 68.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.130 / 0.132 / 0.135 | 0.126 / 0.133 / 0.138 | 0.130 / 0.133 / 0.136 | 0.119 / 0.125 / 0.138 | 0.124 / 0.128 / 0.134 | 0.000 | 39,941: 0.000 (range 43,770–44,235) |
| C2 | pass | 1.000 | 0.188 / 0.229 / 0.263 | 0.367 / 0.539 / 0.565 | 0.042 / 0.055 / 0.078 | 1.000 / 1.000 / 1.000 | 0.423 / 0.558 / 0.718 | 1.000 (LC 1000) | 39,941: 0.804 (range 33,998–43,404) |
| C3 | pass | 1.000 | 0.069 / 0.070 / 0.072 | 0.176 / 0.185 / 0.192 | 0.044 / 0.045 / 0.046 | 0.071 / 0.075 / 0.083 | 0.039 / 0.040 / 0.043 | 0.000 | 39,941: 0.000 (range 46,982–47,266) |
| C4 | pass | 1.000 | 0.080 / 0.129 / 0.187 | 0.219 / 0.336 / 0.460 | 0.044 / 0.079 / 0.122 | 0.133 / 0.181 / 0.232 | 0.052 / 0.083 / 0.119 | 0.000 | 39,941: 0.001 (range 39,769–47,277) |
| C5 | pass | 1.000 | 0.114 / 0.154 / 0.171 | 0.174 / 0.358 / 0.416 | 0.009 / 0.022 / 0.032 | 0.961 / 0.983 / 0.994 | 0.193 / 0.300 / 0.520 | 0.000 | 39,941: 0.000 (range 41,458–46,003) |

