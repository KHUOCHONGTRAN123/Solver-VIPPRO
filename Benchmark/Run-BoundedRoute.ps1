param(
    [string]$Binary = "$PSScriptRoot/clearance-bin/Benchmark.dll",
    [string]$Run = 'route-clearance-v11',
    [int[]]$Levels = @(189,206,233,246),
    [long]$MaxCalculations = 100000,
    [long]$MaxRamBytes = 2147483648
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = Join-Path $PSScriptRoot "results/$Run"
New-Item -ItemType Directory -Path $output -Force | Out-Null
@{ maxCalculations=$MaxCalculations; maxRamBytes=$MaxRamBytes; binary=$Binary;
    sourceHash=(Get-FileHash (Join-Path $root 'IndependentSolver/Core/CatLevelSolver.cs')).Hash } |
    ConvertTo-Json | Set-Content (Join-Path $output 'budget.json')
$rows = @()
foreach ($number in $Levels) {
    $name = 'Level{0:D5}' -f $number
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in @($Binary,'route-worker',$root,(Join-Path $root "Levels/$name.json"),'unused',$Run)) {
        $start.ArgumentList.Add($argument)
    }
    $worker = [System.Diagnostics.Process]::Start($start)
    $peak = 0L; $calculations = 0L; $reason = $null
    while (!$worker.WaitForExit(500)) {
        $worker.Refresh(); $peak = [Math]::Max($peak,$worker.PeakWorkingSet64)
        $progressPath = Join-Path $output "$name.progress.json"
        if (Test-Path $progressPath) {
            try { $progress = Get-Content -Raw $progressPath | ConvertFrom-Json; $calculations = [long]$progress.expanded } catch {}
        }
        if ($peak -gt $MaxRamBytes) { $reason = 'RamBudget' }
        elseif ($calculations -gt $MaxCalculations) { $reason = 'CalculationBudget' }
        if ($reason) { $worker.Kill($true); $worker.WaitForExit(); break }
    }
    $resultPath = Join-Path $output "$name.json"
    if ($reason) {
        $status = 'HighCostStopped'
        # Preserve an explicitly cancelled partial result, never an unsolvable
        # verdict. Progress counters are lower bounds at the external stop.
        if (Test-Path $progressPath) {
            $partial = Get-Content -Raw $progressPath | ConvertFrom-Json
            $partial.status = 'Cancelled'
            $partial.message = "External benchmark stop: $reason; partial counters are lower bounds."
            $partial | ConvertTo-Json -Depth 100 | Set-Content $resultPath
        }
        @{peakWorkingSetBytes=$peak;measurementComplete=$false;stopReason=$reason} |
            ConvertTo-Json | Set-Content (Join-Path $output "$name.memory.json")
    }
    elseif ($worker.ExitCode -ne 0 -or !(Test-Path $resultPath)) { throw "Worker failed: $name" }
    else {
        $result = Get-Content -Raw $resultPath | ConvertFrom-Json
        $status = $result.status; $calculations = [long]$result.expanded
        $memory = Get-Content -Raw (Join-Path $output "$name.memory.json") | ConvertFrom-Json
        $peak = [Math]::Max($peak,[long]$memory.peakWorkingSetBytes)
        if ($calculations -gt $MaxCalculations -or $peak -gt $MaxRamBytes) { $reason='CompletedOverBudget' }
    }
    $rows += [pscustomobject]@{level=$name;status=$status;calculations=$calculations;peakWorkingSetBytes=$peak;budgetReason=$reason}
    $rows | Export-Csv (Join-Path $output 'metrics.csv') -NoTypeInformation
    $rows | ConvertTo-Json -AsArray | Set-Content (Join-Path $output 'metrics.json')
    Write-Output "$name $status calculations=$calculations RAM=$peak reason=$reason"
    $worker.Dispose()
}


