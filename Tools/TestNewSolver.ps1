# Windows PowerShell 5.1; builds current IndependentSolver source (.NET 10 required).
$ErrorActionPreference = 'Stop'
# Intercept the Windows console event before PowerShell aborts the pipeline.
# A managed flag lets the normal loop/finally flush reports and kill workers.
if (!("CatDomBatchCancel" -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CatDomBatchCancel {
    public static volatile bool Requested;
    private delegate bool Handler(uint type);
    private static readonly Handler Callback = OnControl;
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    private static bool OnControl(uint type) {
        if (type != 0 && type != 1) return false;
        Requested = true;
        return true;
    }
    public static void Install() { Requested = false; SetConsoleCtrlHandler(Callback, true); }
    public static void Remove() { SetConsoleCtrlHandler(Callback, false); }
}
'@
}
$folder = $null
$workers = 1
for ($i = 0; $i -lt $args.Count; $i++) {
    if ($args[$i] -eq '--workers') {
        $i++
        $value = 0
        if ($i -ge $args.Count -or ![int]::TryParse($args[$i], [ref]$value) -or $value -lt 1) {
            Write-Host '--workers requires a positive integer.'; exit 2
        }
        $workers = $value
    } elseif ($args[$i] -eq '--help') {
        Write-Host 'TestNewSolver.bat "folder" [--workers N]   (builds current source; 60s per process; default 1 worker; CSV only; requires .NET 10)'
        exit 0
    } elseif ($null -eq $folder -and !$args[$i].StartsWith('--')) {
        $folder = $args[$i]
    } else { Write-Host "Unknown argument: $($args[$i])"; exit 2 }
}
if ([string]::IsNullOrWhiteSpace($folder)) { $folder = (Read-Host 'Level folder').Trim().Trim('"') }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildFolder = Join-Path $repo ('artifacts\test-new-solver\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$cli = Join-Path $buildFolder 'cat-solver.exe'
Write-Host 'Building current IndependentSolver source...'
& dotnet publish (Join-Path $repo 'Solver.Cli\Solver.Cli.csproj') -c Release --no-restore -m:1 -p:UseSharedCompilation=false -o $buildFolder
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$resultsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'TestNewSolver-results'))
try {
    $item = Get-Item -LiteralPath $folder
    if (!$item.PSIsContainer) { throw 'Input must be a folder.' }
    $inputRoot = $item.FullName.TrimEnd('\') + '\'
    if (!(Test-Path -LiteralPath $cli -PathType Leaf)) { throw "Release CLI missing: $cli" }
    # Exclude all previous reports even when testing the repository/tool folder.
    $files = @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Filter '*.json' |
        Where-Object { !$_.FullName.StartsWith($resultsRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and !$_.FullName.StartsWith((Join-Path $PSScriptRoot 'TestLevels-results') + '\', [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName)
    $outputRoot = Join-Path $resultsRoot ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
    [void][IO.Directory]::CreateDirectory($outputRoot)
} catch { Write-Host $_.Exception.Message; exit 2 }
$csvPath = Join-Path $outputRoot 'summary.csv'
$columns = 'levelPath','status','wallTimeMs','solveTimeMs','moveCount','expanded','searchAlgorithm','algorithmVersion','meets60sTarget','message'
$utf8 = New-Object Text.UTF8Encoding($true)
$writer = New-Object IO.StreamWriter($csvPath, $false, $utf8)
$writer.AutoFlush = $true
$writer.WriteLine(($columns -join ','))
$active = New-Object Collections.ArrayList
$clock = [Diagnostics.Stopwatch]::StartNew()
$next = 0; $done = 0; $solved = 0; $timeouts = 0; $errors = 0

function Save-Row($job, $status, $message, $result) {
    $row = [pscustomobject][ordered]@{
        levelPath = $job.File.FullName; status = $status
        wallTimeMs = [Math]::Round($job.Timer.Elapsed.TotalMilliseconds, 2)
        solveTimeMs = $null; moveCount = $null; expanded = $null
        searchAlgorithm = ''; algorithmVersion = ''; meets60sTarget = $false; message = $message
    }
    if ($null -ne $result) {
        $row.solveTimeMs = $result.solveTimeMs
        $row.moveCount = @($result.moves).Count
        $row.expanded = $result.expanded
        $row.searchAlgorithm = $result.searchAlgorithm
        $row.algorithmVersion = $result.algorithmVersion
        $row.meets60sTarget = $status -eq 'Solved' -and $null -ne $result.solveTimeMs -and $result.solveTimeMs -le 60000
    }
    $lines = @($row | ConvertTo-Csv -NoTypeInformation)
    $writer.WriteLine($lines[1])
}

Write-Host "Levels: $($files.Count) | Workers: $workers | Timeout: 60s"
Write-Host "Solver: $cli"
Write-Host "Results: $outputRoot"
$runInfo = [ordered]@{ solverCli=$cli; cliSha256=(Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash; solverDllSha256=(Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($cli)) 'IndependentSolver.dll') -Algorithm SHA256).Hash; inputFolder=$item.FullName; levelCount=$files.Count; workers=$workers; wallTimeoutSeconds=60; startedAt=(Get-Date).ToString('o'); cancellation='Ctrl+C stops workers and preserves completed rows' }
$runInfo | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'run-info.json') -Encoding UTF8
[CatDomBatchCancel]::Install()
try {
    while ($next -lt $files.Count -or $active.Count -gt 0) {
        if ([CatDomBatchCancel]::Requested) { break }
        if ($next -lt $files.Count -and $active.Count -lt $workers -and ![CatDomBatchCancel]::Requested) {
            $file = $files[$next]; $next++
            $process = New-Object Diagnostics.Process
            $process.StartInfo.FileName = $cli
            $process.StartInfo.Arguments = '"' + $file.FullName + '"'
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.CreateNoWindow = $true
            $process.StartInfo.RedirectStandardError = $true
            $process.StartInfo.RedirectStandardOutput = $true
            $job = [pscustomobject]@{ File=$file; Process=$process; Timer=[Diagnostics.Stopwatch]::StartNew(); Stderr=$null; Stdout=$null }
            # Register before launch so finally can clean up even if interrupted.
            [void]$active.Add($job)
            try {
                [void]$process.Start()
                $job.Stderr = $process.StandardError.ReadToEndAsync()
                $job.Stdout = $process.StandardOutput.ReadToEndAsync()
            } catch {
                $job.Timer.Stop()
                Save-Row $job 'Error' $_.Exception.Message $null
                $errors++; $done++
                $process.Dispose(); $active.Remove($job)
            }
        }
        foreach ($job in @($active.ToArray())) {
            $process = $job.Process
            if (!$process.HasExited -and $job.Timer.Elapsed.TotalSeconds -lt 60) { continue }
            if (!$process.HasExited) {
                $process.Kill(); $process.WaitForExit(); $job.Timer.Stop()
                Save-Row $job 'Timeout' 'Not completed within 60 seconds; solvability unknown.' $null
                $timeouts++
            } else {
                $job.Timer.Stop()
                try {
                    $json = $job.Stdout.GetAwaiter().GetResult()
                    if ([string]::IsNullOrWhiteSpace($json)) { throw "CLI exit $($process.ExitCode): $($job.Stderr.GetAwaiter().GetResult())" }
                    $result = $json | ConvertFrom-Json
                    if ([string]::IsNullOrWhiteSpace([string]$result.algorithmVersion) -or $result.status -notin @('Solved','NoNextCatReachable','InvalidInput','UnsupportedMechanics','Cancelled','CountdownExceeded')) {
                        throw 'Missing algorithmVersion or invalid result status.'
                    }
                    Save-Row $job $result.status $result.message $result
                    if ($result.status -eq 'Solved') { $solved++ } else { $errors++ }
                } catch { Save-Row $job 'Error' $_.Exception.Message $null; $errors++ }
            }
            $done++
            $process.Dispose(); $active.Remove($job)
            Write-Host ("[{0}/{1}] {2} | Solved {3}, Timeout {4}, Other/Error {5} | {6:N1}s" -f $done,$files.Count,$job.File.Name,$solved,$timeouts,$errors,$clock.Elapsed.TotalSeconds)
        }
        if ($active.Count -gt 0 -and ($active.Count -ge $workers -or $next -ge $files.Count)) { Start-Sleep -Milliseconds 25 }
    }
} finally {
    # PowerShell invokes finally on Ctrl+C; flush each completed row immediately.
    foreach ($job in @($active.ToArray())) {
        try {
            if (!$job.Process.HasExited) { $job.Process.Kill(); $job.Process.WaitForExit() }
            $job.Timer.Stop()
            Save-Row $job 'Cancelled' 'Batch interrupted.' $null
        } catch { } finally { $job.Process.Dispose() }
    }
    $writer.Dispose()
    $exceeded = @(Import-Csv -LiteralPath $csvPath | Where-Object status -eq 'CountdownExceeded')
    if ($exceeded.Count -gt 0) {
        $notePath = Join-Path $outputRoot 'countdown-exceeded.csv'
        $exceeded | Export-Csv -LiteralPath $notePath -NoTypeInformation -Encoding UTF8
        Write-Host "Countdown exceeded (no retry): $notePath"
    }
    [CatDomBatchCancel]::Remove()
    $clock.Stop()
    Write-Host ("Finished: {0}/{1}, Solved {2}, Timeout {3}, Other/Error {4}, {5:N1}s" -f $done,$files.Count,$solved,$timeouts,$errors,$clock.Elapsed.TotalSeconds)
    Write-Host "Report: $csvPath"
}
if ([CatDomBatchCancel]::Requested) { exit 130 }
if ($errors -gt 0 -or $timeouts -gt 0) { exit 1 }
exit 0


