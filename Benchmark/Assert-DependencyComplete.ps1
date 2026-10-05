param([string]$Run='route-dependency-current')
$ErrorActionPreference='Stop'
$folder=Join-Path $PSScriptRoot "results/$Run"
$root=Split-Path $PSScriptRoot
$expected=@(Get-ChildItem (Join-Path $root 'Levels') -Filter '*.json'|ForEach-Object BaseName|Sort-Object)
$rows=@(Import-Csv (Join-Path $folder 'metrics.csv'))
if($expected.Count -ne 299 -or $rows.Count -ne 299){throw 'Require exactly299 dataset levels and result rows.'}
if(Compare-Object $expected @($rows.level|Sort-Object)){throw 'Dataset/result names differ.'}
if(@($rows.level|Select-Object -Unique).Count -ne 299){throw 'Duplicate result rows.'}
foreach($row in $rows){
    if($row.status -ne 'Solved' -or $row.withinBudget -ne 'True' -or [long]$row.calculations -gt 100000 -or [long]$row.peakWorkingSetBytes -gt 2147483648){throw "Unfinished/overbudget level: $($row.level)"}
}
$guard=Get-Content -Raw (Join-Path $folder 'guard-audit.json')|ConvertFrom-Json
if($guard.checkedPlans -ne 299 -or $guard.status -ne 'Passed'){throw 'Require299 guard/replay audits.'}
$replay=@(Get-Content -Raw (Join-Path $folder 'replay-audit.json')|ConvertFrom-Json)
if($replay.Count -ne 299 -or @($replay|Where-Object {$_.status -ne 'Solved' -or $_.replay -ne 'Solved'}).Count){throw 'Require299 final-state/queued-box replay audits.'}
if(Compare-Object $expected @($replay.level|Sort-Object)){throw 'Replay coverage differs.'}
$summary=Get-Content -Raw (Join-Path $folder 'summary.json')|ConvertFrom-Json
$currentHash=(Get-FileHash (Join-Path $root 'IndependentSolver/Core/CatLevelSolver.cs')).Hash
if($summary.sourceHash -ne $currentHash){throw 'Source changed since merge.'}
# Merge-Dependency must be run immediately before the final audits; its full
# manifest and binary validation supply the frozen-version provenance gate.
@{levels=299;solvedWithinBudget=299;guardReplay=299;finalStateReplay=299;sourceHash=$currentHash;status='Passed'}|
    ConvertTo-Json|Set-Content (Join-Path $folder 'completion-audit.json')
Write-Output 'PASS completion gates:299 solved, replayed and within budget.'


