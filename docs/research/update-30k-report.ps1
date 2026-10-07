param(
    [string]$AcceptanceExperiment = 'final-19-acceptance',
    [string]$ScreenExperiment = 'goal-30k-selective-beam-all'
)
$ErrorActionPreference = 'Stop'
$researchRoot = $PSScriptRoot
$projectRoot = Split-Path (Split-Path $researchRoot -Parent) -Parent
$scope = @(Import-Csv (Join-Path $projectRoot 'docs/benchmarks/performance.csv') | Where-Object { [long]$_.expanded -gt 10000 } | ForEach-Object { $_.level })
if ($scope.Count -ne 21) { throw 'Scope must contain 21 levels.' }
$excludedLevels = @('Level00206', 'Level00233')
$scope = @($scope | Where-Object { $_ -notin $excludedLevels })
if ($scope.Count -ne 19) { throw 'Final scope must contain 19 levels.' }
$baseline = @(Get-Content (Join-Path $researchRoot 'baseline-three-all/summary.json') -Raw | ConvertFrom-Json)
$accepted = @(Get-Content (Join-Path $researchRoot "$AcceptanceExperiment/summary.json") -Raw | ConvertFrom-Json)
$screen = @(Get-Content (Join-Path $researchRoot "$ScreenExperiment/summary.json") -Raw | ConvertFrom-Json)
$hashes = @($accepted.assemblyHash | Sort-Object -Unique)
if ($hashes.Count -ne 1) { throw 'Acceptance must use one common binary.' }
if (@($accepted.level | Sort-Object -Unique).Count -ne $accepted.Count) { throw 'Duplicate acceptance levels.' }
foreach ($entry in $accepted) {
    if ($entry.level -notin $scope) { throw 'Acceptance level outside scope.' }
    $entryBaseline = $baseline | Where-Object { $_.level -eq $entry.level }
    if ($entry.machine -ne $entryBaseline.machine -or $entry.runtime -ne $entryBaseline.runtime) { throw 'Baseline/current machine or runtime mismatch.' }
    $entryScreen = $screen | Where-Object { $_.level -eq $entry.level }
    # Historical screen can use an earlier binary; acceptance is authoritative.
}
$rows = @()
foreach ($levelName in $scope) {
    $original = $baseline | Where-Object { $_.level -eq $levelName }
    $current = $accepted | Where-Object { $_.level -eq $levelName }
    $screening = $screen | Where-Object { $_.level -eq $levelName }
    $passed = $false
    if ($null -ne $current) {
        if ($current.samples.Count -ne 3) { throw "Invalid sample count: $levelName" }
        $median = @($current.samples.solveTimeMs | Sort-Object)[1]
        $validSamples = @($current.samples | Where-Object { $_.status -eq 'Solved' -and $_.replayed -eq $true -and [long]$_.expanded -lt 30000 })
        $passed = $validSamples.Count -eq 3 -and $median -lt 3000 -and $current.warmupStatus -eq 'Solved'
        if ($passed -ne $current.passed) { throw "Acceptance flag disagrees with recomputation: $levelName" }
    }
    $rows += [pscustomobject]@{
        level = $levelName
        accepted = $passed
        evidence = $(if ($null -ne $current) { 'target warm-up + 3 sequential samples + independent v42 replay' } else { 'single screening only; no acceptance claimed' })
        baselineExpanded = $original.samples[0].expanded
        baselineMedianMs = $original.medianMs
        expanded = $(if ($null -ne $current) { $current.samples[0].expanded } else { $screening.expanded })
        medianMs = $(if ($null -ne $current) { $current.medianMs } else { $null })
        screenStatus = $screening.status
        screenMs = $screening.solveTimeMs
        baselineDrags = $original.samples[0].drags
        baselineSteps = $original.samples[0].steps
        drags = $(if ($null -ne $current) { $current.samples[0].drags } else { $screening.drags })
        steps = $(if ($null -ne $current) { $current.samples[0].steps } else { $screening.steps })
    }
}
$passCount = @($rows | Where-Object accepted).Count
$finalStatus = if ($passCount -eq 19) { 'complete under user-revised scope' } else { 'active; incomplete' }
$report = [pscustomobject]@{generatedUtc=[DateTime]::UtcNow.ToString('o');status=$finalStatus;scopeCount=19;excludedLevels=$excludedLevels;acceptedCount=$passCount;expandedExclusiveLimit=30000;medianExclusiveLimitMs=3000;assemblyHash=$hashes[0];referenceAssemblyHash=$accepted[0].referenceAssemblyHash;harnessAssemblyHash=$accepted[0].harnessAssemblyHash;catalogHash=$accepted[0].catalogHash;acceptanceExperiment=$AcceptanceExperiment;screenExperiment=$ScreenExperiment;runtime=$accepted[0].runtime;machine=$accepted[0].machine;rows=$rows}
$report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $researchRoot 'GOAL_30K_STATUS.json') -Encoding UTF8
$lines = @('# Final goal: below 30,000 states and median below 3 seconds', '', "Common-binary acceptance: $passCount / 19. Status: $finalStatus.", '', 'On 2026-10-07 the user explicitly excluded Level00206 and Level00233 to conclude research on the remaining 19 levels. Exclusion does not claim either level meets the thresholds. Historical 21-level results are retained.', '', "Product assembly SHA256: $($hashes[0])", '', 'Acceptance includes one target warm-up, three sequential Release samples, all solved, all independent v42 replays valid, and every expanded count below 30,000. Median includes parsing, validation, precompute, search and internal replay.', '', '| Level | Accepted | Expanded (baseline -> current) | Median ms | Drags (baseline -> current) | Steps (baseline -> current) |', '|---|---|---:|---:|---:|---:|')
foreach ($row in $rows) {
    $timeText = $(if ($null -ne $row.medianMs) { [Math]::Round($row.medianMs, 1) } else { 'not accepted' })
    $partialSuffix = $(if ($row.screenStatus -eq 'Cancelled') { ' (partial)' } else { '' })
    $lines += "| $($row.level) | $($row.accepted) | $($row.baselineExpanded) -> $($row.expanded) | $timeText | $($row.baselineDrags) -> $($row.drags)$partialSuffix | $($row.baselineSteps) -> $($row.steps)$partialSuffix |"
}
$lines += @('', 'Failed screens are diagnostic evidence only. Cancelled partial plans do not count as solved or valid full solutions. Existing solved plans for failing levels do not meet the cost/time criteria.', '', 'The current heuristic selects target-first for inputs with at least 13 holes or multiple towers. Target work adapts to input hole count; the first broad min beam gives DFS a turn after 4,096 states on target-first inputs, while pressure-first inputs keep the 16,384-state beam. Subsequent complete fallback remains. It applies to board geometry without level IDs or stored answers. Directed edge caching preserves route costs. This is an experimental strategy, not a guarantee for unseen levels.', '', 'Drags and grid steps are reported as solution quality. Acceptance does not imply shortest plans; increases relative to baseline remain visible above.')
$lines | Set-Content (Join-Path $researchRoot 'GOAL_30K_STATUS.md') -Encoding UTF8
Write-Output "Recomputed common-binary acceptance: $passCount / 19"
