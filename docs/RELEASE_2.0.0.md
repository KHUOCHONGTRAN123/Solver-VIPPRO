# CatDom Solver 2.0.0

Two algorithms with a fixed **10,000-state baseline budget**. The solver first runs the 1.0.0 search; if another full-state expansion is needed before completion, it discards the partial plan and restarts from the original level with the improved search. No elapsed-time or level-ID routing is used.

## API changes

- `algorithmVersion`: `2.0.0`.
- New `searchAlgorithm`, `baselineExpanded`, `improvedExpanded` fields.
- `expanded` is the sum of both attempts; `solveTimeMs` includes both attempts, parsing and internal replay.
- Existing level JSON, move structure, cancellation and concurrent invocation remain supported.

## Validation

- 299-level hybrid diagnostic screen: 278 levels preserve the exact 1.0.0 plan/counter; 19 hard levels switch and pass independent v42 replay.
- Hybrid budget boundary, restart, cumulative cost and cancellation checks; 504 exhaustive tiny-board comparisons; 57,936 route-distance oracle checks.
- External project and NuGet consumers, self-contained CLI input/error paths, Unity 2022.3 Editor Mono smoke.

## Known limits

- Level00206 and Level00233 still exceed the diagnostic 10-second screen limit. This limit is imposed by the test harness, not the API. No guarantee of fast solution on these or unseen levels.
- The historical <30,000/<3s result for 19 hard levels applies to improved search alone. Hybrid total adds up to 10,000 baseline expansions and its elapsed time.
- Some improved plans use more moves. Keeping baseline for easy levels avoids those regressions. Timing depends on the machine. IL2CPP player was not exercised.

## Downloads

- `CatDom.Solver.2.0.0.nupkg`: .NET Standard 2.1 library, Newtonsoft.Json dependency.
- `CatDom.Solver.Cli-2.0.0-win-x64.zip`: Windows x64 CLI, self-contained .NET runtime.
- `CatDom.Solver.Unity-2.0.0.zip`: library, Newtonsoft.Json DLL, linker file and documentation.
- `checksums.json`: SHA256 checksums.

Benchmarks, plan comparisons and reproducible scripts are included in the repository under `docs/benchmarks/v2` and `docs/research`. Version 1.0.0 assets are retained.
