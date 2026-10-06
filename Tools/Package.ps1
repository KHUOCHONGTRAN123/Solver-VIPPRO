$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Set-Location $root
function Invoke-DotNet([string[]]$Arguments){ & dotnet @Arguments; if($LASTEXITCODE -ne 0){throw "dotnet failed: $Arguments"} }
$summaryPath=Join-Path $root 'docs/benchmarks/summary.json'
& {
    $summary=Get-Content $summaryPath -Raw|ConvertFrom-Json
    if($summary.acceptance -ne 'Passed' -or $summary.levels -ne 299){throw '299-level acceptance is required before packaging.'}
    $dll=Join-Path $root 'IndependentSolver/bin/Release/netstandard2.1/IndependentSolver.dll'
    if((Get-FileHash $dll).Hash -ne $summary.binaryHashes.optimized){throw 'Library differs from measured binary.'}
    foreach($entry in $summary.sourceHashes.PSObject.Properties){
        if((Get-FileHash (Join-Path $root $entry.Name)).Hash -ne $entry.Value){throw "Source differs from acceptance: $($entry.Name)"}
    }
}
$artifacts=Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force "$artifacts/nuget","$artifacts/cli/win-x64","$artifacts/unity"|Out-Null
Invoke-DotNet -Arguments @('pack','IndependentSolver/IndependentSolver.csproj','-c','Release','--no-build','--no-restore','-o',"$artifacts/nuget")
Invoke-DotNet -Arguments @('publish','Solver.Cli/Solver.Cli.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-o',"$artifacts/cli/win-x64")
$library=Join-Path $root 'IndependentSolver/bin/Release/netstandard2.1/IndependentSolver.dll'
if((Get-FileHash "$artifacts/cli/win-x64/IndependentSolver.dll").Hash -ne (Get-FileHash $library).Hash){throw 'Published CLI contains a different library.'}
Copy-Item -LiteralPath $library -Destination "$artifacts/unity/IndependentSolver.dll"
$unityNewtonsoft=Join-Path $env:USERPROFILE '.nuget/packages/newtonsoft.json/13.0.3/lib/netstandard2.0/Newtonsoft.Json.dll'
Copy-Item -LiteralPath $unityNewtonsoft -Destination "$artifacts/unity/Newtonsoft.Json.dll"
@'
<linker>
  <assembly fullname="IndependentSolver" preserve="all" />
  <assembly fullname="Newtonsoft.Json" preserve="all" />
</linker>
'@|Set-Content "$artifacts/unity/link.xml"
Copy-Item docs/API.md "$artifacts/unity/README.md"
Copy-Item docs/API.md "$artifacts/cli/win-x64/README.md"
$license=Join-Path $env:USERPROFILE '.nuget/packages/newtonsoft.json/13.0.3/LICENSE.md'
if(Test-Path $license){Copy-Item $license "$artifacts/unity/NEWTONSOFT-LICENSE.md";Copy-Item $license "$artifacts/cli/win-x64/NEWTONSOFT-LICENSE.md"}
Compress-Archive -Path "$artifacts/cli/win-x64/*" -DestinationPath "$artifacts/CatDom.Solver.Cli-1.0.0-win-x64.zip" -Force
Compress-Archive -Path "$artifacts/unity/*" -DestinationPath "$artifacts/CatDom.Solver.Unity-1.0.0.zip" -Force
Get-ChildItem $artifacts -File -Recurse|Where-Object Name -ne 'checksums.json'|ForEach-Object{[pscustomobject]@{path=$_.FullName.Substring($root.Length+1);sha256=(Get-FileHash $_.FullName).Hash}}|ConvertTo-Json -Depth 4|Set-Content "$artifacts/checksums.json"
Write-Output 'NuGet, self-contained CLI and Unity DLL bundle created.'
