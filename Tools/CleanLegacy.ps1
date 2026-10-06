$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$summary=Get-Content (Join-Path $root 'docs/benchmarks/summary.json') -Raw|ConvertFrom-Json
$integration=Get-Content (Join-Path $root 'docs/benchmarks/integration.json') -Raw|ConvertFrom-Json
if($summary.acceptance -ne 'Passed' -or $summary.levels -ne 299 -or $integration.status -ne 'Passed'){throw 'Performance and integration acceptance must pass before cleanup.'}
$benchmark=Join-Path $root 'Benchmark'
foreach($item in Get-ChildItem -LiteralPath $benchmark -Force){
    if($item.Name -eq 'results'){
        $targets=Get-ChildItem -LiteralPath $item.FullName -Force|Where-Object Name -ne 'route-dependency-v42-current'
    }else{$targets=@($item)}
    foreach($target in $targets){
        $resolved=[IO.Path]::GetFullPath($target.FullName)
        if(!$resolved.StartsWith($benchmark+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe cleanup target: $resolved"}
        if($target.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Do not recursively delete a link: $resolved"}
        if($target.PSIsContainer -and @(Get-ChildItem -LiteralPath $resolved -Recurse -Force -Attributes ReparsePoint).Count){throw "Nested links require review: $resolved"}
        if($target.PSIsContainer){Remove-Item -LiteralPath $resolved -Recurse -Force}else{Remove-Item -LiteralPath $resolved -Force}
    }
}
Write-Output 'Removed legacy benchmark code, binaries and experiments; frozen v42 evidence retained.'
