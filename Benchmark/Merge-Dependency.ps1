param([string]$Version='v23',[string]$Output='route-dependency-current')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$destination=Join-Path $PSScriptRoot "results/$Output"
New-Item -ItemType Directory -Force $destination | Out-Null
$manifestPath=Join-Path $PSScriptRoot "dependency-$Version-bin/inputs-manifest.json"
if(!(Test-Path $manifestPath)){throw 'Frozen source/input manifest is required.'}
foreach($entry in Get-Content -Raw $manifestPath|ConvertFrom-Json){
    $verifiedPath=$entry.Path
    if($entry.Path -eq (Join-Path $root 'IndependentSolver/Core/CatLevelSolver.cs')){
        $sourceHash=$entry.Hash
        $verifiedPath=Join-Path $PSScriptRoot "experiments/CatLevelSolver.dependency-$Version.cs.snapshot"
    }
    if(!(Test-Path -LiteralPath $verifiedPath) -or (Get-FileHash -LiteralPath $verifiedPath).Hash -ne $entry.Hash){throw "Frozen input/source changed: $verifiedPath"}
}
$levels=@(Get-ChildItem (Join-Path $root 'Levels') -Filter '*.json' | ForEach-Object BaseName)
$selected=@{}
foreach($folder in Get-ChildItem (Join-Path $PSScriptRoot 'results') -Directory -Filter "route-dependency-$Version-*"){
    $budgetPath=Join-Path $folder.FullName 'budget.json'
    $metricsPath=Join-Path $folder.FullName 'metrics.csv'
    if(!(Test-Path $budgetPath) -or !(Test-Path $metricsPath)){continue}
    $budget=Get-Content -Raw $budgetPath|ConvertFrom-Json
    if($budget.sourceHash -ne $sourceHash){throw "Source mismatch: $($folder.Name)"}
    $frozenBinary=Join-Path $PSScriptRoot "dependency-$Version-bin/Benchmark.dll"
    if(!(Test-Path -LiteralPath $budget.binary) -or (Get-FileHash -LiteralPath $budget.binary).Hash -ne (Get-FileHash -LiteralPath $frozenBinary).Hash){throw "Binary mismatch: $($folder.Name)"}
    foreach($row in Import-Csv $metricsPath){
        if($row.level -notin $levels){throw "Unknown level: $($row.level)"}
        $resultPath=Join-Path $folder.FullName "$($row.level).json"
        if(!(Test-Path $resultPath)){continue}
        $result=Get-Content -Raw $resultPath|ConvertFrom-Json
        $eligible=$row.status -eq 'Solved' -and !$row.budgetReason -and $result.status -eq 'Solved' -and
            [long]$row.calculations -le [Math]::Min(100000,[long]$budget.maxCalculations) -and [long]$row.peakWorkingSetBytes -le [long]$budget.maxRamBytes
        if($eligible){
            if([long]$result.expanded -ne [long]$row.calculations -or $result.backtracks -ne 0 -or
                $result.config.strategy -ne 'RouteClearing' -or $result.config.maxSolveSeconds -ne 0 -or $result.config.maxNextCatSearchSeconds -ne 0){throw "Result invariant mismatch: $resultPath"}
            $memory=Get-Content -Raw (Join-Path $folder.FullName "$($row.level).memory.json")|ConvertFrom-Json
            if([long]$memory.peakWorkingSetBytes -gt [long]$row.peakWorkingSetBytes){throw "RAM mismatch: $resultPath"}
        }
        if(!$selected.ContainsKey($row.level) -or (!$selected[$row.level].eligible -and $eligible)){
            $selected[$row.level]=@{row=$row;folder=$folder.FullName;eligible=$eligible}
        }
    }
}
$rows=foreach($name in $levels|Sort-Object){
    if(!$selected.ContainsKey($name)){continue}
    $item=$selected[$name]
    foreach($suffix in @('.json','.memory.json')){
        $path=Join-Path $item.folder "$name$suffix"
        if(Test-Path $path){Copy-Item -LiteralPath $path -Destination (Join-Path $destination "$name$suffix") -Force}
    }
    [pscustomobject]@{level=$name;status=$item.row.status;calculations=[long]$item.row.calculations;peakWorkingSetBytes=[long]$item.row.peakWorkingSetBytes;withinBudget=$item.eligible;sourceRun=Split-Path $item.folder -Leaf}
}
$rows|Export-Csv (Join-Path $destination 'metrics.csv') -NoTypeInformation
$rows|ConvertTo-Json -AsArray|Set-Content (Join-Path $destination 'metrics.json')
$summary=[pscustomobject]@{attempted=@($rows).Count;solvedWithinBudget=@($rows|Where-Object withinBudget).Count;missing=@($levels|Where-Object {!$selected.ContainsKey($_)});sourceHash=$sourceHash;goalComplete=$false}
$summary|ConvertTo-Json -Depth 4|Set-Content (Join-Path $destination 'summary.json')
$summary|ConvertTo-Json -Depth 4

