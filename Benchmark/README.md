# Solver baseline benchmark

Run from the repository root:

```powershell
dotnet run --project Benchmark/Benchmark.csproj -c Release -- "D:/Solver VIPPRO"
```

Requires .NET SDK 10. The runner references Newtonsoft.Json bundled with the SDK, so no external NuGet package is needed.

The runner leaves the solver source unchanged and solves every `Levels/*.json` sequentially with `new SolverConfig()` (10 seconds per level, subproblem depth 12). One sample is run first to warm up the JIT. Each measured solve starts from a freshly loaded board. Successful solves include the solver's built-in replay check; this validates the solver model, not the actual game engine.

Outputs in `Benchmark/results/baseline-default/`:

- `levels.csv`: per-level status, time, moves, search counters, remaining state, and message.
- `Level*.json`: full results, plans, snapshots, and subproblem statistics.
- `summary.json`: configuration, runtime, core source hashes, totals, and timing statistics.

A rerun overwrites this output directory's matching files. Copy results elsewhere before comparing a changed solver. `TimedOut` and `NoSolutionWithinDepthLimit` do not prove a level unsolvable. Moves in failed results are partial plans. The CSV `steps` column sums path lengths as stored by the solver.

## Route-clearing base

```powershell
dotnet build Benchmark/Benchmark.csproj -c Release
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll route-tests
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll route "D:/Solver VIPPRO" "*" Level00001 route-memory-v10
```

Arguments after the root are: exact level name or `*`, first level name, output directory name. To run a single level without overwriting the full run: `route "D:/Solver VIPPRO" Level00117 Level00117 route-single-check`. The active full-run output is `results/route-memory-v10/`. A run replaces metrics files in its output directory; choose another output directory for subset checks.

Use the new base from code:

```csharp
var result = CatLevelSolver.Solve(board, SolverConfig.CreateRouteClearingDefault(), cancellationToken);
```

`RouteClearing` commits each consumption event and never searches earlier consumption decisions. It first searches short chains through depth 6, then continues using a complete explicit DFS stack and visited states, ordering moves by route cost and pressure on other reachable goals. Depth 6 switches traversal; it never causes failure. Linked route scores require all surviving partners to fit and satisfy movement axes and gates. Locked or otherwise immovable groups obstruct relaxed routes. After DFS discovery, exact move generation skips parking detours that one legal drag can replace. The exact move generator handles footprints, linked groups and gates, and every successful full plan is replayed. `useRouteDepthFirst = false` retains the earlier unbounded iterative-deepening implementation for comparisons.

Legacy strategies are kept as comparison code; the new strategy bypasses adaptive models, decision backtracking, time deadlines and endpoint pruning. A user cancellation token remains supported. Exhausting the finite reachable parking graph reports `NoNextCatReachable`, which does not establish whether an earlier committed consumption choice could have won the original level.

`metrics.csv` records calculations as `SolveResult.expanded`, including repeated short-chain passes and states expanded while compacting parking plans, plus peak working set bytes measured inside a fresh process per level. RAM is read after Solve and replay: it includes the .NET runtime and diagnostic captures, but is measured before final detail serialization. The old baseline has no RAM measurement. Intermediate `.progress.json` files are diagnostic only; their status is not a completed result.

Pending DFS frames discard unneeded sibling move lists. If a branch returns, the deterministic list is regenerated; this regeneration counts as another state expansion. This lowers memory retention without dropping any alternative move.

After the full run, generate and audit the comparison with `./Benchmark/Summarize-Route.ps1`. This refuses incomplete or duplicate level sets and checks result details against metrics and current core hashes.

The measured run is split across `route-memory-v10`, `route-memory-v10-check`, `route-memory-v10-middle106`, and the `tail150`, `tail180`, `tail190`, `tail210`, `tail220`, `tail240`, `tail250`, `tail270`, `tail290` directories prefixed with `route-memory-v10-`. Use `./Benchmark/Merge-Route.ps1` after all 299 unique results are completed. It checks matching core versions, preserves per-level provenance and creates `results/route-final/`. Overlapping completed results are deduplicated in the script's run order. Then run `dotnet Benchmark/audit-bin/Benchmark.dll route-verify "D:/Solver VIPPRO" route-final` to replay all solved plans with no timeout and inspect remaining playable holes and queued cats.

While workers are running, `route-verify-completed ROOT RUN...` audits only flushed completed CSV rows from the named output directories. It writes `results/completed-replay-audit.json`; it does not replace the final 299-level audit.

Additional queue segments `route-memory-v10-tail207` and `route-memory-v10-tail234` use the same frozen solver and manifest. They are included in the merge script defaults; any completed overlaps are deduplicated.

The `route-memory-v10-tail247` queue segment is also included in the merge defaults.

The final queued217–219 segment uses `route-memory-v10-tail217`, also included in the merge defaults.

Tests cover a corridor requiring multiple parking moves despite a configured depth of 1, disabled timeout, gate direction, explicit cancellation, linked partners and incompatible axes, and 504 small one-cat boards compared against the legacy complete-search oracle.
Final result: results/route-final/report.md covers all299 attempted levels;295 solved/replayed,4 high-cost incomplete (189,206,233,246). Stopped metrics are checkpoint/observed RAM lower bounds, not complete solve costs. Solver has no internal timeout.
