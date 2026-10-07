$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
Set-Location $projectRoot
function Run-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $Arguments" }
}
$screen = Get-Content docs/benchmarks/v2/hybrid-screen.json -Raw | ConvertFrom-Json
$library = Join-Path $projectRoot 'Verification/hybrid-v2-bin/IndependentSolver.dll'
if ($screen.count -ne 299 -or $screen.rows.Count -ne 299) { throw 'Full 299-level hybrid screen required.' }
if (@($screen.rows | Where-Object preserved).Count -ne 278) { throw 'All 278 baseline plans must be preserved.' }
foreach ($row in $screen.rows) {
    if ($row.expanded -ne $row.baselineExpanded + $row.improvedExpanded -or $row.baselineExpanded -gt 10000) { throw 'Invalid hybrid accounting.' }
    if ($row.status -eq 'Solved' -and !$row.replayed) { throw 'Independent replay missing.' }
    if ($row.status -ne 'Solved' -and $row.level -notin @('Level00206','Level00233')) { throw "Unexpected failure: $($row.level)" }
}
if ((Get-FileHash -LiteralPath $library).Hash -ne $screen.assemblyHash) { throw 'Binary differs from verified screen.' }
$integration = Get-Content docs/benchmarks/v2/integration.json -Raw | ConvertFrom-Json
if ($integration.status -ne 'Passed' -or $integration.binaryHash -ne $screen.assemblyHash) { throw 'Matching integration verification required.' }
$destination = Join-Path $projectRoot 'artifacts/v2'
New-Item -ItemType Directory -Force "$destination/nuget", "$destination/cli/win-x64", "$destination/unity" | Out-Null
Run-DotNet @('pack','IndependentSolver/IndependentSolver.csproj','-c','Release','--no-build','--no-restore',"/p:OutputPath=$projectRoot/Verification/hybrid-v2-bin/",'-o',"$destination/nuget")
Run-DotNet @('publish','Solver.Cli/Solver.Cli.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-m:1','/p:BuildInParallel=false','/p:UseSharedCompilation=false','-o',"$destination/cli/win-x64")
if ((Get-FileHash "$destination/cli/win-x64/IndependentSolver.dll").Hash -ne $screen.assemblyHash) { throw 'Published binary differs from verification.' }
Copy-Item -LiteralPath $library -Destination "$destination/unity/IndependentSolver.dll"
$jsonDll = Join-Path $env:USERPROFILE '.nuget/packages/newtonsoft.json/13.0.3/lib/netstandard2.0/Newtonsoft.Json.dll'
Copy-Item -LiteralPath $jsonDll -Destination "$destination/unity/Newtonsoft.Json.dll"
@'
<linker>
  <assembly fullname="IndependentSolver" preserve="all" />
  <assembly fullname="Newtonsoft.Json" preserve="all" />
</linker>
'@ | Set-Content "$destination/unity/link.xml"
foreach ($folder in @("$destination/unity", "$destination/cli/win-x64")) {
    Copy-Item docs/API.md "$folder/README.md"
    Copy-Item CHANGELOG.md "$folder/CHANGELOG.md"
    $license = Join-Path $env:USERPROFILE '.nuget/packages/newtonsoft.json/13.0.3/LICENSE.md'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination "$folder/NEWTONSOFT-LICENSE.md" }
}
Compress-Archive -Path "$destination/cli/win-x64/*" -DestinationPath "$destination/CatDom.Solver.Cli-2.0.0-win-x64.zip" -Force
Compress-Archive -Path "$destination/unity/*" -DestinationPath "$destination/CatDom.Solver.Unity-2.0.0.zip" -Force
Get-ChildItem $destination -File -Recurse | Where-Object Name -ne 'checksums.json' | ForEach-Object {
    [pscustomobject]@{path=$_.FullName.Substring($destination.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
} | ConvertTo-Json -Depth 4 | Set-Content "$destination/checksums.json"
Write-Output 'Created v2 packages; known diagnostic timeouts 206/233 remain documented; v1 artifacts untouched.'
