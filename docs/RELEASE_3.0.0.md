# CatDom Solver 3.0.0

Improved solver for Expand, Bomb, Hole Spawner and Curtain; cached geometry and exact mechanic replay. Includes support for additional level mechanics in the current source.

Countdown is ignored during search and checked against original limits afterward. Plans exceeding limits return CountdownExceeded (CLI exit 3); no countdown-aware retry. TestNewSolver.bat builds current source and defaults to one worker with 60-second deadlines, with a separate countdown-exceeded.csv.

Measured development results: Expand 81/81 under 60s; Bomb 86/93; Spawner 29/39 versus 27/39 in the saved original report; LevelNew Countdown 79/80 with independent replay; Curtain 26/28, with 86 and 261 explicitly skipped. These are mechanic-specific development runs, not a full-corpus guarantee for the final 3.0.0 binary. Spawner regressions 637/845/958 remain documented. Curtain reduces search on some levels but a general reduction in backtracking is unproven. LevelNew 72 null queued-cat position remains a known parser issue.

Release assets: Windows x64 self-contained CLI, Unity/.NET Standard 2.1 DLL bundle, NuGet package and SHA256 checksums. Newtonsoft.Json 13.0.3 is required for library consumers. Unity bundle includes linker preservation; no new IL2CPP validation is claimed.
