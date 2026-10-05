# Goal: 299/299 within a measured calculation/RAM budget

Initial evaluation budget accepted by user: 2,000,000 expanded states and
2,147,483,648 bytes peak worker RAM per level. This is an evaluation threshold,
not yet evidence that the final solver meets it. No solver time limit is added.

v11 adds a route-clearance ranking penalty for single-hole target routes:
reconstruct a suggested corridor from occupancy distances, then BFS each blocking
rigid linked group against static mechanics to find a parking footprint disjoint
from the entire corridor, or a consumption event. Other movable groups are
ignored; inability to clear penalizes this suggested route but does not prune
any legal move or goal. Linked target routes retain existing scoring for now.
This is an initial feasibility heuristic, not a complete dependency planner.

v12 caches per-phase/per-state/per-target clearance penalties (bounded at 4096
entries) and emits progress every 4096 expansions in short recursive passes as
well as DFS. v11 short-pass progress can lag substantially; final completed
costs are authoritative and over-budget completion is marked by the runner.

Frozen v12 binary: clearance-v12-bin/Benchmark.dll; source snapshot:
experiments/CatLevelSolver.clearance-v12.cs.snapshot; input/source manifest:
clearance-v12-bin/inputs-manifest.json. v10 source snapshot is preserved too.

Tests passed after v12: 504 small-board complete-search comparisons, corridor,
gates, links, cancellation, movable-but-unclearable two-cell blocker, and an
alternative route around that blocker. FindNextCat may return Solved when the
consumption also completes the board; the alternative-route test accepts both
successful statuses.

Latest persisted partial replay audit: 67 unique completed levels, all solved
plans replay valid. This does not establish 299/299 or improvements on the four
hard levels. Partial comparisons are in route-clearance-v12-all.

Benchmark processes launched this turn (revalidate live process/session before
waiting or restarting; no result file alone establishes liveness):

- session 25285: v11 hard levels 189,206,233,246, currently 189.
- session 57280: v11 smoke 1,20,50,100,149,179,289, currently 149.
- session 14942: v11 full299.
- session 36238: v12 hard levels 206,233,246,189, currently 206.
- session 24959: v12 full299.

All runs use Run-BoundedRoute.ps1. budget.json records source hash and budgets.
HighCostStopped is an external benchmark stop, not proof of unsolvability.
Next work: inspect hard-level results and specific captured subproblems; improve
blocker parking dependencies, support linked target corridors, compare and
independently replay full299. Keep the goal active until all requirements pass.

## v13 dependency-focused traversal

Current core adds a focused best-first traversal after two cheap short passes.
It keeps a target fixed, reconstructs its corridor, includes blocking rigid
groups, finds relaxed parking paths for single-hole blockers and recursively
includes holes blocking those paths. Exact Generate enforces every actual move.
It tries up to three targets, 1024 frontier pops each, with a bounded 2048-entry
frontier; these are accelerator switches, not solver stop conditions. Failure
always returns to complete parking search. Linked targets/linked parking-path
dependencies remain supported by fallback, not fully modeled by the accelerator.

Frozen binary dependency-v13-bin; source snapshot and full hash manifest saved.
Tests PASS504 plus trapped blocker, alternative route, and explicit C→B→A
dependency corridor. Level67 Solved376 expansions/60919808 RAM bytes vs baseline
133795 expansions; persisted plan independently replayed. Partial replay PASS44
unique v13 levels. At later metrics snapshot first49 completed levels have 58005
expansions vs99724 baseline on the same49. This is partial evidence only.

New live benchmark sessions (revalidate before waiting):44017 v13 hard
67,206,233,246;42279 v13 heavy189,149,179,289;58203 v13 full299.
Old v11/v12 runs were still live at last inspection and retained as comparisons.
Runner now captures OS peak RAM and preserves Cancelled partial JSON plus an
explicit incomplete measurement sidecar on external stops. Already launched
runners have their earlier script loaded; inspect their actual outputs before
merging stopped results. Full299 and hard-level success remain unproven.

## Further continuation: v14–v16

v13 progressed beyond116, but116 regressed to NoNextCatReachable (baseline
Solved). This contradicts no-regression completion. Last consumption leaves no
relaxed movable next-target component. Latest v13 audit PASS96 unique completed
plans; later metrics reached122 attempted results including this failure.

v14 weights relaxed blocker-parking paths by occupancy of other groups, so
moving C off B's parking path changes the score. It tries every target with
4096 focused frontier pops rather than3 targets/1024. v15 suppresses duplicate
focused queue states unless their depth improves, with32768-entry bookkeeping
bound. Both builds/tests PASS504 and mechanic/dependency cases. Captured
subproblem probes for189 and206 under v14 reached external100000-expansion
budgets (Cancelled102400) without a next-cat solution; improvement on these
phases remains unproven.

v16 adds precommit consumption simulation: accept only a cleared board or a
board passing the necessary relaxed next-target test. It restores every phase
array and mechanic state before continuing search. This is not rollback of a
committed event; unknown continuations are preserved. Rejected consumption
moves never become parking steps; compaction checks the same gate. Build and
tests PASS. Regression116 is currently live, not yet solved.

Frozen current binary dependency-v16-final-bin; source snapshot
experiments/CatLevelSolver.dependency-v16.cs.snapshot and manifest saved.
Relevant new sessions:11948 v14 hard233,246,206,189;71519 v15 hard246,233,206,189;
76305 v16 regression116,67,100. Probes54996/59512 completed Cancelled102400.
Existing v13 sessions44017,42279,58203 remain live at last inspection. Old
v11/v12 workers were explicitly superseded and stopped; their partial outputs
remain, recorded in results/superseded-clearance-workers.json. Do not mark these
manual experiment stops as budget stops or unsolvability.

Next: resolve116 regression and hard-phase planning, inspect complete v13
dataset failures, verify current-source299 results within budgets and replay
all plans. Goal remains active; neither299/299 nor four-hard-level success is
established.

## Current v18 continuation

v17 changes focused intents from a hole with any goal to a specific
hole/destination anchor; clearance score cache and dependency corridors include
that destination. Focused traversal diversifies over specific placements, up to
1024 pops per placement and32768 total focused pops before complete fallback.
v18 adds the existing necessary remaining-cat capacity/reachability matching
test before committing a consumption and switches complete traversal to DFS
after depth3 (rather than6). Both matching and next-target tests only reject
proven dead ends in relaxed models; neither proves eventual victory.

v16 regression116 reached over520000 phase expansions/63million generated
moves without success; thus its check was insufficient and short-pass repeated
work was material. v18 regression116 is still live, most recent total33573,
not yet solved. v18 full run completed86 levels, all Solved at snapshot.

Build/tests PASS504 plus explicit C→B→A dependency, unclearable blocker,
alternate route and a new capacity reservation test: a hole unable to fit an
edge cat must eat the interior cat first. No committed consumption is rolled
back. Frozen dependency-v18-bin, source snapshot and full manifest saved.

Live sessions to revalidate:25178 v18 regression116,67,100;23742 v18 full299;
74808 v18 hard206,233,246,189;28901 v18 probe206 (external100000-expansion
budget). v13–v16 workers were superseded and stopped after known116 regression,
with preserved partial results and peak RAM in
results/superseded-dependency-workers.json. These manual stops are not budget
stops or unsolvability. Current full299,116 regression and four hard-level
successes remain required and unproven.
Latest v18 persisted audit PASS100 unique completed results, all Solved and
replayed. Matched first100 calculations: baseline860565 vsv18 59620. This is
partial scope only and does not establish full299 budget compliance.

## Current authoritative continuation (v23)

v21 adds a monotone relaxed progression guard retaining box queue order,
current other-color cat obstruction, and layer-color unlock dependencies;
exposed consumption opportunities remain available as witnesses for all holes.
Initial v20 greedy witnesses falsely rejected baseline237; corrected v21 and
v22 passed all295 baseline plans. v22 includes current occupied anchors as
possible drop sites after mechanic transitions and keys the guard by positions.
v23 tracks each dependent blocker's required clearance region separately and
scores outstanding clearance requests. Four-stage D→C→B→A regression test,
box/layer cycle test, and504 oracle cases passed. Frozen v23 source snapshot
and full manifest are saved under experiments and dependency-v23-bin.

Verified new evidence: v21 level233 Solved192042 calculations/265617408 peak
RAM bytes;246 Solved201255/238854144. Both plans independently replayed with
all guards under v23. v22 first188 allSolved and within2million/2GiB budget;
all188 plans guard/replay PASS under v23. Matched cost baseline14399514 vs
v22 526091; v22 maximum peak RAM210079744 bytes. This does not certify v23
costs or299 completion. v23 regression116 Solved1419,67 Solved456,100
Solved13186 (v22 116 had509, so v23 has known small cost tradeoffs).

Live sessions to revalidate:76585 v23 tail190..299;6022 v23 hard189,206,233,246;
v23 head1..188 launched this continuation (session in tool history).
Still-live older comparison workers observed: v21 206(pid24288),189(pid16040),
v21-all189(pid8620),v22-all189(pid8060). Preserve these until benchmark stop or
intentional supersession; do not restart from stale files. Remaining189/206
success and a single-current-version full299 audit are unproven. Goal active.
Same continuation: v23 hard189 completed Solved2644 calculations/67866624
peak RAM bytes; session6022 advanced to206. Head session98043. Await persisted
guard/replay audit for189 before claiming validated success.
Next continuation:189 persisted guard/replay PASS under current v23. v23
partial replay PASS62 unique completed plans. Head advanced64 and tail207
advanced218, including216 Solved11032 calculations/75165696 peak RAM bytes.
Current206 hard phase starts after96 moves,33795 total calculations at latest
snapshot; still live and within budget, not yet solved. Older v21/v22 comparison
workers intentionally superseded/stopped; log superseded-v21-v22-workers.json.
New sessions65534 tail207..299,31849 tail240..269,28139 tail270..299;
head98043,hard6022,tail19076585 still live. Runs may overlap; use same-version
deduplication and stop duplicate workers only after retaining their completed
outputs. Full299 audit still incomplete; goal active.
Next continuation: created Merge-Dependency.ps1 to validate current sourceHash,
known dataset names, matching counters and RAM, RouteClearing/no internal time
budgets/no committed backtracks and per-run calculation/RAM limits. First merged
snapshot168/299 allSolved within budget, output route-dependency-current;
guard/replay audit live session28584, already50 plans passed. This is not final.
Live new split runs8479 tail247..269 and11737 tail234..239 bypass slow active
246/232 workers; existing same-version outputs retained. Current v23 hard206
live, latest46083 total calculations. No source change this continuation.
Continuation: merged168 guard/replay PASS168. Added Write-DependencyReport.ps1 (matched comparison.csv, explicit partial report). Tail270 completed270..299 allSolved; revalidate session28139 terminal state. Middle102..188 session52918 advanced108. Hard206 latest70659;101/233/246 workers live. Refresh merge/audit/report session49960 pending. Goal active; full299 required.
Continuation: merged220 current-v23 results all within budget; guards/replay PASS220, report updated. Merge now verifies full frozen input/source manifest and binary hashes. Duplicate206 tail190 worker4092 stopped intentionally; hard206 worker17572 retained, log duplicate206-stop.json. New middle130..188 session75025. Refresh merge/audit/report session21070 pending. Latest20699331,246102959,23341145 calculations; all live within budget. Goal remains active.
Same continuation: middle150..188 launched session20049 to bypass active133;150Solved8 calculations. Merge21070 may be slow while validating large manifests/results; keep polling same session, never restart solely from observation timeout.
Continuation: refreshed238 merged results allSolved within budget; guard/replay PASS238 and report updated. Matched238 baseline24077566 vs v23467926 calculations; maxRAM121651200 bytes. Duplicate133 workers8744/14976 stopped; middle102 worker22744 retained, log duplicate133-stops.json. New sessions59183 middle134..149,74194 middle166..188. Refresh merge/audit/report launched this turn (session in history). Latest206115715,246139823,23365581,26826283 calculations; live under budget. No solver source changes. Full299 still unproven.
Continuation: merge7438 snapshot280 allSolved within budget, audit live125 passed at latest. v23 level149 completed7153 calculations/~63MB peak bytes; saved in middle134/middle102 after merge snapshot, include next refresh. Duplicate176 worker3620 stopped; middle150 retained. New177..188 session19895. Hard206144387 total calculations, verifiedlive17572, RAM~149MB. Goal active, no source change.
Continuation: audit280 completed PASS280/report updated. v23 level246 completed177462 calculations/202584064 RAM bytes in tail240, within budget.268 completed36054/81887232;269 worker live.206156675,23381965 still live. Refresh merge/audit/report session96376. Duplicate already-completed workers middle102/middle177/tail240 intentionally stopped after retaining all flushed outputs, log duplicate-completed-workers-stop.json. Goal active; full299 audit required.
Continuation: snapshot294 guard/replay PASS294/report updated. All runs currently297 unique completed, missing206/233 only; refresh297 merge/audit/report session28421 pending. Verifiedlive only solve workers17572(206),18604(233).206 total189443/currentphase184320/generated3619283/startmove96;233 total102445/currentphase49152/generated814423/startmove147. Both below budget; retain running processes. No source change. Full299 and final-state audit still required.
Continuation verified wait: merge297 all within budget, audit28421 progressed225 plans. Solver workers17572/18604 confirmedlive with increasing CPU;206201731 calculations/~197MB peak,233114733/~106MB peak, both within budget. Waited same session6022 for30sec, still live; do not restart. No source change. Full299 completion unproven.
Continuation verified wait:297 guard/replay complete/report297. Matched297 baseline38896890 (includes stopped lowerbounds) vsnew905815 calculations; peak202584064 bytes among completed. Confirmedlive20617572 CPU906.14/peak213282816/226307 calculations;23318604 CPU653.78/peak123396096/131117. Waited same sessions6022/65534, still live. Independent partial final-state cats/initial-playable-holes audit session83631 underway; full299 box queue audit remains required. Goal active, no code change.
Continuation verified wait: final-state audit83631 completed PASS297 (cats empty and initial playable holes finished); artifact partial-final-state-audit.json.20617572 CPU1001.44/peak234889216/254979 calculations;23318604 CPU749.11/peak142872576/151597. Waited same sessions6022/65534; still running. No code change, no restart; goal active. Full299 queued-box/final audit remains required.
Continuation verified wait:206283651/peak247980032 CPU1076.92;233167981/peak152555520 CPU824.14 confirmedlive. Wait6022 returnedstillrunning. Started partial box/tower queue final audit session2668, using verified SolverJson.cs input order catBoxInfos then towerInfos, counts against final consumed. Do not claim audit success until command finishes.297 guard/replay and cats/holes remain verified; goalactive.
Continuation: partial-box-audit PASS297 completed; report generator now explicitly records cats/holes/box audits with their actual scope.297 report regenerated. Confirmedlive206312323/peak264556544 CPU1164.89;233188461/peak166580224 CPU912.08. Same session6022/65534 waited, stillrunning. No solver change; goalactive.
Continuation verifiedwait:206332803/peak286842880 CPU1234.25;233204845/peak177307648 CPU981.56. Same sessions65534/6022 remainrunning. Added Assert-DependencyComplete.ps1 requiring exact299 names, budgets,299 guard audits and299 final-state replay audit; not invoked as success yet, current297 cannot pass. No solver change; goalactive.
USER UPDATED BUDGET: max200000 expansions perlevel, RAM remains2GiB. Both206/233 exceeded new threshold; externally stopped and preserved Cancelled partial JSON/memory/CSV, records updated-budget-stops.json.206lowerbound472067,233294957. All297 completed v23 levels are below200000. Runner/assert/report/merge now use200000; older budget files retain original limits as provenance. Need optimize206/233 and run new attempts under200000; goalactive.
USER SUPERSEDING BUDGET: max100000 expansions perlevel, RAM2GiB unchanged. Runner/merge/assert/report updated. Reclassified completed results over100000 as outsidebudget (plans preserved).206/233 already stopped; goal requires optimizing all overbudget cases too.
New optimization v24: after depth3, before complete DFS, run a broader target-fixed best-first accelerator allowing all legal hole groups, with weaker clearance request weight16 instead128. Bounded16384 pops total/2048 pertarget; failure still complete fallback, no consumption rollback. Build/tests PASS504 and dependency/cycle cases. Frozen snapshot/manifest saved. New session22617 checks101,246,206,233; separate hard workers launched this turn. All new runs max100000. Goalactive; costs/success not yet established.
Budget confirmation: latest asynchronous reply explicitly retains current goal100000 expansions/level and2GiB peakRAM; earlier initial2million reply superseded. v24 level101 externally stopped102426 expansions/108367872bytes, unresolved budget case. Other v24 hard workers remain live; no success claim or source change.
v25 optimization: exact DFS now retains bounded sibling lists (8192 candidates, at least active frame) instead of regenerating every parent after each child. Search order/legal successors unchanged; bounded cache eviction preserves regeneration fallback. Build and route-tests PASS504. Frozen snapshot/manifest dependency-v25-bin. Benchmark101/246,206,233 started under100000/2GiB. v24 confirmed101/206/246 stopped abovebudget;233 not yet verified terminal. Goalactive; v25 success unproven.
v26 optimization: broad accelerator now ranks progress across all holes using RoutePolicy, including linked groups, rather than spending per-target effort only on single-hole goals. One bounded16384-pop all-legal-moves pass; exact DFS still fallback. v25 sibling cache retained. Build/tests PASS504. Frozen v26 snapshot/manifest. v26check101,246,206,233 launched; no full299 completion evidence.
v26 level101 stopped102426 expansions/196857856bytes. Baseline v23 phase101move9 firstSolutionDepth100293 then compacted to16drags; illustrates poor exhaustive traversal ordering. v27 DFS removes consecutive same-group parking drags after terminal check: Generate already enumerates all reachable endpoints for that rigid group, direct equivalent available at parent. Bounded sibling cache retained. Build/tests PASS504; frozen snapshot/manifest saved. v27check101/246/206/233 launched, success not yet proven.
v27 level101 stopped102426/189427712bytes, so consecutive-group pruning alone insufficient. v28 replaces broad best-first pass with layered beam128, up to16384 expansions, diverse quota per movedhole and dedup selected expanded states. All Generate/AcceptTerminal legality retained, failure continues exactDFS; no consumption rollback. Build/tests PASS504. Frozen snapshot/manifest dependency-v28-bin. New sessionscheck101/246,206,233 started under100000/2GiB. Need same-current-version299 validation; goalactive.
v28 level101 externally stopped102426 expansions/177451008peakbytes; beam128 not sufficient, no success improvement claimed. v27 level246 stopped100201/160387072bytes. v28check session83209 now246; v28single206 session18790 and233 session6274 retained live. Current sourcev28, testsPASS, goalactive. Next use captured failed input and dependency heuristic evidence rather than claiming cache/beam solves dense rearrangements.
v28 breakthrough: level233 Solved62392 expansions/93487104peakbytes (<100000/2GiB), guard audit and completed replay PASS1 using frozen v28binary.206 progressedpastoldmove96 to138 but stilllive;246stilllive. Currentgoal remains incomplete101overbudget. Starting same-version broader regression runshead1..100,middle102..205,tail207..232+234..245+247..299, skipping completed233 and active206/246. Need exact299 currentversionmerge/fullguard/replay/final audits beforecompletion.
v29: focused dependency ranking now uses requested-hole weighted distance to a parking footprint outside clearance region (movable groups crossed only as heuristic penalties), replacing binary uncleared count. Supports rigid linked blocker parking paths in recursive dependency discovery instead of skippinglinkedgroups. No new impossibility pruning; exactGenerate/AcceptTerminal unchanged, beam128/fallback retained. Build/tests PASS504. Frozen snapshot/manifest saved. Probes101phase9,206phase138,246phase385 launched with20000 expansion budgets, results unproven. Merge-Dependency now validates version-specific frozen core snapshot instead of requiring currentcore, still validates actualotherinputs/binary. v28partial merge session98752 underway while v28regressionworkers retained.
v28 merged257 attempts254SolvedWithinBudget; guard+replay PASS254. Head1..100 and middle102..205 now allSolved (204), tailstilllive. v29 probes101/206/246 stopped20480 withoutconsumption, notsolvabilityverdict. Full v29check session72941 launched under100000. v30 experiment increases beam width128to512 withsame16384 expansion cap, keeping alternatives across groups; targets costly early246phase55 (v28spent77150). Build underway.
Confirmed v28 complete299 attempts296withinbudget, guardPASS296 and fullreplay-audit299 (296Solved,3Cancelled101206246). v29/v30 all4hardcases stopped100000, regression233 so revertcore tov28. v31 experiment beamonly uses ReachableSum instead of minimum-dominated RoutePolicy, focusedstandard andexactDFS v28unchanged. Build/tests andprobes pending; no goalcompletion.
v31 breakthrough: beamonly ReachableSum (focusedstandard andDFS fromv28) solves10113721/67149824bytes and24625523/83038208bytes; persisted guard+completedreplay PASS2. Build/testsPASS504. Frozenmanifest/snapshot saved. Fullsameversionrunshead43816,middle58285,tail7290 launched allremaining exceptactive2067420/23390354. Goalactive; nofull299claim. v28fullreplay299 andguard296 passed, v29v30regression233 retainedasfailedexperiments.
v31 onlybeamReachableSum improved10113721 and24625523 bothguardreplayPASS; but live206phase39~91928,233phase112~74762 indicates differentcommittrajectory. v32 experiment two bounded beam passes, v28minimumfirst thenv31sum iffirstfails, beforeexactDFS. Allbothpasswork counted; no catrollback, retain233knownfirstpassordering. Frozenv32snapshot/manifest build/tests pending. v31fullregression remainsrunning; partial154withinbudget mergeguard session3771.
v32 testsPASS504, two-passbeam minimumthenReachableSum gives101Solved30105/68997120bytes and246Solved41907/82452480bytes, guardreplayPASS2. Currentfrozenv32source. Fullregressionhead64580,middle73081,tail98150 launched. Hard20683416 PID4544 phase39 and23394916 PID17248 active. v31 hard206stopped100120/193060864,233stopped103434/184709120; v31partial154guardPASS and broaderregressionlive. Goalactive, no299claim.
v33 experiment: minimumbeam then12000-expansion DFS accelerator before sumbeam, finallycompleteDFSwithoutcap. Motivation v28level233phase26 solution26307including16384beam; v32sum changescommitphase112highcost. Allworkcounted, no committedrollback, acceleratorlimit notoverallsolvercap. BuildPASS; tests/frozenmanifest saved. v32fullregression retainedlive; hard206phase39/233phase112 nearbudget, noachievementclaim.
v33testsPASS504. 101Solved42105/77070336 guardPASS1 earlier;246Solved53907/81895424, combinedguard pendingnow. Hard206session27244 PID23672 progressed138moves/42525expansions;233session77190 PID17168 move102/51399, retainedlive. v32hard206stopped100276,233also terminalconfirmedprocessmissing; partial214attempt212within guardreplayPASS212. v33currentfrozen source unchanged. Goalactive; fullsameversion299 stillrequired.
v33 level233Solved62392/90726400bytes, guardreplayPASS1. Threehardcases101246233 nowverifiedwithinbudget usingcurrentv33. Fullregressionhead34412,middle51451,tail15741 started; hard20627244retained. Goalactive, need206 andexact299 audits. Sourceunchanged frozenv33.
v33hard206 confirmedHighCostStopped102657/108056576bytes phase13877824expanded; sourcepreserved. v34 adds thirdbeamordering ReachableBottleneckPlusSum only afterv33beamminimum,12000DFS,andbeamsum fail; exactDFS fallbackstillcomplete. Build/testsPASS504, frozenv34snapshot/manifest saved. Probephase138 inputcapturedfromv33 start, session21866 max60000. v33regressioncontinues, merge6528/guard pending. Goalactive, full299notproven.
v34phase138probe cancelled60156/moves0, noimprovementproof. v35 thirdbeam usesReachableSum with32of128 slots deterministichash-arrangementdiversity,96ranked/groupquota; firstv33passes unchanged. Build/tests pending/snapshotmanifest saved. v33head100allSolved,middle81/tail58stilllive; partial175guardreplayPASS. No299achievement.
v35testsPASS504. phase138probe session69406 max65000 andfull206session91948 max100000 live; authoritative workerinspection recordednext. v33refreshmergeguard session45997 running. Currentcodev35firstpassesv33unchanged, thirddiversebeamordering only; goalactive.
v35full206Stopped102657/95268864, thirddiversebeamnoimprovement. v33regressionnewfailure269100108/137809920; tailnowcomplete299, refresh53695 underway. Earlierfullreplay61606failedmissing289 becausepartialmerge, notplanfailure; guard286passed. v36restorev33plus extra distance-only focusedpass(allgroupslegal,4096pergoal8192total,no clearancepenalty) aftersum beforeexactDFS. BuildPASS tests/snapshotmanifest saved, currentcodev36; needprobesandfullregression. Goalactive.
v33complete299 merged297SolvedWithinBudget, guardreplayPASS297 andfullroute-verifyPASS299 (297Solved,Cancelled206269). Audit artifactsroute-dependency-v33-current. v36269Stopped100108/133328896bytes; 206session21176 PID6080retainedlive. Needdiagnoseearly269phase and206phase138, no goalcompletion. Currentcodev36frozen andtestsPASS504.
v36full206Stopped102657/108167168,269100108/133328896. v37 adds complete priorityfrontier for <=4 activeholes, alllegalparkingstates dedupatdiscovery, no depth/time caps or committedrollback; otherwisev36 passes unchanged. Motivation269phase73only4holes, v28solutionfirstdepth60828/79801expansions thencompact; v33stoppedphase98304. Build/testsPASS504 frozenv37snapshot/manifest saved. Sessions26994227,hardcheck101246233 and206 newlylaunched, currentcodev37. Goalactive, requiresnew299sameversionaudits.
v37 level269Solved82130/128757760bytes, persistedguardreplayPASS1 andcompletedreplayPASS1. 101Solved42109/77045760bytes. Fullcurrentv37regressionhead84069,middle66067,tail39196 startedexcluding269/activecheck/hard206. Worker20619364 session11686 confirmedlive, hardcheck90179 active246. Goalactive; no combinedcrossversion299claim.
v37 hard206Stopped102657/106037248bytes; partial95SolvedwithinbudgetguardreplayPASS95. v38 experiment adds targetfixed beam128afterminimumbeam before12000DFS, picks minimum occupancy-distance singleholegoal andranksalllegalmoves bysamegoal distance throughout. NooccupancyignoranceinGenerate/no committedrollback; fallbackcomplete. Frozenv38source/manifest, buildtests pending. v37fullregression retained; goalactive.
v37hard233Solved62395/90894336. v38probephase138session4479PID19788 andfull20666838PID22804confirmedlive. v37regression head/middleterminal, tail267live15980; refreshmergeguard45250running. Currentcodev38testsPASS504. No299currentcompletion.
v38phase138probeCancelled40960moves0, no successful optimization established. v37hardcheckguardPASS3 (101246233);269guardpreviousPASS1. v37regressioncontinues tail267. v38full206retainedlive22804/66838. Goalactive, no goalcompletion; needmodel/heuristic evidence beyondfailedaccelerators.
v38full206Stopped100605/84369408bytes. v37partial273attempt272withinbudgetguardreplayPASS272. v39 targetfixedbeam adds alternatehole(secondbest relaxed occupancy-distance holegoal) beforeclosesttargetpass, no removal/pruning/committedrollback. BuildPASS tests/frozenmanifest saved. Needphase138/full206 evidence; currentcorev39, v37tailretainedlive.
v39phase138probeCancelled40220moves0, alternativegoal notsuccessfulyet; full206session70487PID20972 confirmedlive. v37tailfinished299, fullrefreshmergeguardreplay started thisturn (sessionreturnednext), needterminalconfirmation. Currentcorev39, goalactive, no299completionclaim.
v37full299attempt298SolvedWithinBudget guardPASS298/fullreplayPASS299 (206Cancelledonly); authoritativeartifactsroute-dependency-v37-current. v39full206Stopped102173/74346496. v40restoreknownv37core plus RouteClearing terminal selection existingConsumptionUtility (finishes/unlocks bonus) beforefinishing/shortdrag ties; nocommittedrollback. BuildPASS tests/frozenmanifest saved. Neednew206/knownhardcasesthenfull299.
v37Write-DependencyReport regenerated299results/298audited (goalnotcomplete). v40testsPASS504, full206session50006PID5456 confirmedlive andcheck14686PID8144active246;101Solved42109/77062144. Currentcorefrozenv40. No206successclaim. Nextfinishhardcasesverify, compareterminaltrajectoryutility vs v37.
v40utility terminalordering doesnotchange206 phase138, Stopped102657/101752832; known4hardcasesallSolved. v41adds heuristic keygoalpriority: alivekeycat matchescurrentholecolor and footprintgoal, openscurrentlylockedhole, keyroutescore(distance+clearance)/8 vsnormalmin. Exactlegality/necessaryguardsunchanged; no newpruning, rollback or internaldeadline. BuildPASStests pending, frozenmanifest saved. Currentcorev41; needprobe/full206 andregressions.
v41testsPASS504; full206session65738 andphase138probe42077 max50000 launched andworkersconfirmedlive. Currentfrozenv41. v37remainsverified298/299, goalactive.
v41breakthrough phase138probe NextCatFound6497expanded/47moves, keygoalpriority escapes previouslystuckstate. Full206session65738PID5356stilllive; nofullsolveclaim yet, no replayofprobe successasfulllevel. Currentv41 sourceunchanged; goalactive. Needfull206resultthenhardregression/full299sameversionaudit.
v41full206 nowdifferenttrajectory phase39 (phase28spent31011) vsoldphase138, latest51895 total; remainslive5356/65738. Hardregressioncheck101246233269189 session84149 launched. Needdistinguish successfulkeyheuristicprobe fromwholelevel regression. Currentcodeunchangedv41testsPASS; goalactive.
v41full206Stopped103926/89845760 phase39 (differenttrajectory), keyprobephase138success6497 cannot implyfullsolve. v42 gateskeypriority to extra beamonly afternormalbeam+12000DFSfail andonlywhenremaininglockedhole; restoresv37normalorderingfirst/otherpassheuristics, allworkcounted. Existingutilityv40retained. Build/tests/snapshotmanifest saved; currentv42. Needfull206then299currentaudits; goalactive.
v42testsPASS504. hard206session26479/check90348 launched under100000/2GiB; workerinspectionconfirmslive. Currentcorefrozenv42. v37verified298/299 remainsbaseline; nofullachievement. Needgatedkeyordering206trajectory thensameversion299.
v42 breakthrough full206Solved64364/87703552bytes; guard+completedreplayPASS1. Gated keypriority afterminimumbeam+shortDFS escapesphase13834881expansions thenfinishes. Hardchecksession90348retained233/269/189. Fullsameversionhead71628,middle63360(skip189),tail95696 launched. Needexact299mergeguard/fullreplay/finalsourcebudgetauditbeforegoalcomplete. Currentv42sourcefrozen/testsPASS504, no codechange. All299claimunprovenuntilregressionends.
v42hardcheckcomplete10142109,24653910,23362395,26982130,1892646 allwithin100000/2GiB.20664364alreadyguardPASS. Fullregressionstilllivehead18804level72,middle7532level118,tail3364level222. Partialmergeguard78053running; currentv42sourceunchanged. Goalactivefull299required.
v42partial198resultsallwithinbudget, guardreplayPASS198(session91763). Head100terminalpassed; middle/tailconfirmedlive. Reportgenerator nowconditionalcomplete onlyaftercompletion-auditPassed299 and metrics/guardcounts299, includeskeybeam/smallfrontier improvementsandnextgoals. Currentcorev42unchangedfrozen. Goalactivefull299auditpending.
FINAL v42verified299/299SolvedWithinBudget. FrozenC197D036052C200215C3B712EA10D568E28312510128945A3EBFEF94F9388AB4manifest/binary/source/inputprovenancechecked. guardPASS299, fullreplay299 andfinalcats/playableholes/queuedboxchecksPASS, Assert-DependencyCompletePASS; completion-audit.jsonPassed. Total914398expansions,max82130level269,maxpeak121102336bytes(~115.49MiB), allbelow100000/2GiB. report/comparisongenerated withbaselinecounterlowerboundcaveat andnextgoals. summarygoalCompletetrue. Goalreadycomplete; no solverworkerremaining.
