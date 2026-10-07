# Final goal: below 30,000 states and median below 3 seconds

Common-binary acceptance: 19 / 19. Status: complete under user-revised scope.

On 2026-10-07 the user explicitly excluded Level00206 and Level00233 to conclude research on the remaining 19 levels. Exclusion does not claim either level meets the thresholds. Historical 21-level results are retained.

Product assembly SHA256: 8E320198858F25280D8B495A2DB74779E1FE957974EC9E691B258F7338F15584

Acceptance includes one target warm-up, three sequential Release samples, all solved, all independent v42 replays valid, and every expanded count below 30,000. Median includes parsing, validation, precompute, search and internal replay.

| Level | Accepted | Expanded (baseline -> current) | Median ms | Drags (baseline -> current) | Steps (baseline -> current) |
|---|---|---:|---:|---:|---:|
| Level00100 | True | 16199 -> 3915 | 299.7 | 190 -> 163 | 366 -> 382 |
| Level00101 | True | 42109 -> 1427 | 115.5 | 148 -> 69 | 409 -> 261 |
| Level00117 | True | 55358 -> 20394 | 1517.1 | 151 -> 156 | 385 -> 380 |
| Level00149 | True | 14927 -> 7770 | 694.4 | 73 -> 105 | 287 -> 343 |
| Level00176 | True | 13728 -> 5648 | 435.6 | 156 -> 134 | 384 -> 431 |
| Level00184 | True | 15415 -> 182 | 87.7 | 85 -> 56 | 262 -> 222 |
| Level00186 | True | 12129 -> 19671 | 2283.2 | 117 -> 126 | 381 -> 418 |
| Level00213 | True | 15927 -> 4426 | 294.8 | 184 -> 122 | 495 -> 376 |
| Level00216 | True | 10072 -> 24487 | 2375.1 | 158 -> 149 | 521 -> 515 |
| Level00219 | True | 12377 -> 2149 | 560.7 | 105 -> 133 | 383 -> 429 |
| Level00225 | True | 11682 -> 3883 | 604.4 | 194 -> 130 | 565 -> 431 |
| Level00239 | True | 26253 -> 5351 | 1783.3 | 291 -> 214 | 744 -> 719 |
| Level00246 | True | 53910 -> 16000 | 2496.8 | 235 -> 125 | 622 -> 447 |
| Level00259 | True | 20942 -> 12822 | 607.8 | 164 -> 156 | 379 -> 373 |
| Level00267 | True | 52894 -> 17674 | 1285.3 | 81 -> 72 | 192 -> 174 |
| Level00268 | True | 19586 -> 7856 | 973.3 | 139 -> 137 | 454 -> 432 |
| Level00269 | True | 82130 -> 2096 | 160.1 | 94 -> 66 | 356 -> 262 |
| Level00274 | True | 47332 -> 9585 | 898.5 | 186 -> 159 | 572 -> 513 |
| Level00289 | True | 29138 -> 26244 | 2280.4 | 287 -> 250 | 693 -> 653 |

Failed screens are diagnostic evidence only. Cancelled partial plans do not count as solved or valid full solutions. Existing solved plans for failing levels do not meet the cost/time criteria.

The current heuristic selects target-first for inputs with at least 13 holes or multiple towers. Target work adapts to input hole count; the first broad min beam gives DFS a turn after 4,096 states on target-first inputs, while pressure-first inputs keep the 16,384-state beam. Subsequent complete fallback remains. It applies to board geometry without level IDs or stored answers. Directed edge caching preserves route costs. This is an experimental strategy, not a guarantee for unseen levels.

Drags and grid steps are reported as solution quality. Acceptance does not imply shortest plans; increases relative to baseline remain visible above.
