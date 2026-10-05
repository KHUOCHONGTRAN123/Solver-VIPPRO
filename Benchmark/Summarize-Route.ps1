param([string]$Run = 'route-memory-v10')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('results/' + $Run)
$rows = @(Import-Csv (Join-Path $output 'metrics.csv'))
$levels = @(Get-ChildItem (Join-Path $taskRoot 'Levels') -Filter '*.json')
if ($rows.Count -ne $levels.Count -or $rows.Count -ne 299) { throw "Full run required: $($rows.Count)/299" }
if (@($rows.level | Sort-Object -Unique).Count -ne 299) { throw 'Duplicate level metrics' }
if (@(Compare-Object @($levels.BaseName | Sort-Object) @($rows.level | Sort-Object)).Count -ne 0) { throw 'Metrics do not match the 299 input levels' }
$manifest = Get-Content (Join-Path $output 'manifest.json') -Raw | ConvertFrom-Json
foreach ($property in $manifest.InputHashes.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath $property.Name -Algorithm SHA256).Hash -ne $property.Value) { throw 'Level/IO input changed' }
}
foreach ($source in Get-ChildItem (Join-Path $taskRoot 'IndependentSolver/Core') -Filter '*.cs') {
    if ((Get-FileHash $source.FullName -Algorithm SHA256).Hash -ne $manifest.CoreHashes.($source.Name)) { throw 'Core source changed during benchmark' }
}
$baseline = @(Import-Csv (Join-Path $PSScriptRoot 'results/baseline-default/levels.csv'))
$old = @{}; foreach ($row in $baseline) { $old[$row.level] = $row }
$comparison = @()
foreach ($row in $rows) {
    $detail = Get-Content (Join-Path $output ($row.level + '.json')) -Raw | ConvertFrom-Json
    $memory = Get-Content (Join-Path $output ($row.level + '.memory.json')) -Raw | ConvertFrom-Json
    if ([long]$memory.peakWorkingSetBytes -ne [long]$row.peakWorkingSetBytes -or [long]$row.peakWorkingSetBytes -le 0) { throw "RAM metric mismatch: $($row.level)" }
    if (@($detail.moves).Count -ne [int]$row.moves) { throw "Move metric mismatch: $($row.level)" }
    if ($detail.status -ne $row.status -or $detail.expanded -ne [long]$row.calculations) { throw "Result mismatch: $($row.level)" }
    if ($detail.backtracks -ne 0) { throw "Consumption backtracking: $($row.level)" }
    if ($detail.config.strategy -ne 'RouteClearing' -or $detail.config.maxSolveSeconds -ne 0 -or $detail.config.maxNextCatSearchSeconds -ne 0 -or $detail.config.useAdaptiveModels -or $detail.config.useDecisionBacktracking) { throw 'Unexpected runtime config' }
    if ($detail.status -eq 'Solved' -and @($detail.finalState.remainingCatIds).Count -gt 0) { throw 'Solved result contains living cats' }
    $previous = $old[$row.level]
    $comparison += [pscustomobject]@{level=$row.level; status=$row.status; baselineStatus=$previous.status;
        calculations=[long]$row.calculations; baselineCalculations=[long]$previous.expanded;
        ramMiB=[math]::Round([double]$row.peakWorkingSetBytes/1MB,2); moves=[int]$row.moves; baselineMoves=[int]$previous.moves}
}
$comparison | Export-Csv (Join-Path $output 'comparison.csv') -NoTypeInformation
$counts = @{}; foreach ($group in $rows | Group-Object status) { $counts[$group.Name] = $group.Count }
$common = @($comparison | Where-Object { $_.status -eq 'Solved' -and $_.baselineStatus -eq 'Solved' })
$calculations = @($comparison.calculations | Sort-Object)
$ram = @($comparison.ramMiB | Sort-Object)
$summary = [ordered]@{
    total=299; counts=$counts; baselineSolved=246;
    recovered=@($comparison | Where-Object { $_.status -eq 'Solved' -and $_.baselineStatus -ne 'Solved' }).Count;
    regressions=@($comparison | Where-Object { $_.status -ne 'Solved' -and $_.baselineStatus -eq 'Solved' }).Count;
    calculationsTotal=($calculations | Measure-Object -Sum).Sum;
    calculationsMedian=$calculations[149]; calculationsP95=$calculations[284]; calculationsMax=$calculations[-1];
    ramMedianMiB=$ram[149]; ramP95MiB=$ram[284]; ramMaxMiB=$ram[-1];
    commonSolved=$common.Count; commonBaselineCalculations=($common.baselineCalculations|Measure-Object -Sum).Sum;
    commonNewCalculations=($common.calculations|Measure-Object -Sum).Sum;
    expensiveLevels=@($comparison|Sort-Object calculations -Descending|Select-Object -First 10);
    highRamLevels=@($comparison|Sort-Object ramMiB -Descending|Select-Object -First 10);
    failedLevels=@($comparison|Where-Object status -ne 'Solved');
    audit='299 unique matching details; core hashes match manifest; no consumption backtracking; no deadlines or adaptive models';
    ramScope='PeakWorkingSet64 measured after Solve/replay and diagnostics, before final detail serialization; runtime included';
    comparisonLimit='Expanded state count, including repeated short passes and plan compaction, is a proxy rather than CPU operations. Baseline has no RAM measurement.'
}
$summary | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json')
if ($counts.ContainsKey('Cancelled')) {
    $summary.ramScope += '. Cancelled/HighCostStopped rows use observed working-set lower bounds; exact final peak unavailable.'
    $summary.comparisonLimit += ' Interrupted rows use last checkpoint expanded lower bounds; aggregate costs are incomplete.'
    $summary | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json')
}
$summary | ConvertTo-Json -Depth 4
