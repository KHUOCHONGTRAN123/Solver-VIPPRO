param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
# Read the credential for the configured GitHub service into memory only.
# Never print it, write it to disk, or include it in process arguments.
$credentialLines = "protocol=https`nhost=github.com`n`n" | git credential fill
if($LASTEXITCODE -ne 0){throw 'GitHub credential manager did not return a credential.'}
$credential=@{}
foreach($line in $credentialLines){$parts=$line.Split('=',2);if($parts.Count -eq 2){$credential[$parts[0]]=$parts[1]}}
if(!$credential['password']){throw 'No GitHub credential is available.'}
$headers=@{Authorization=('Bearer '+$credential['password']);Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='CatDom-Solver-Release'}
$repository='KHUOCHONGTRAN123/Solver-VIPPRO'
$repo=Invoke-RestMethod -Uri "https://api.github.com/repos/$repository" -Headers $headers
if(!$repo.permissions.push){throw 'Current GitHub credential does not have push permission.'}
if($CheckOnly){Write-Output "GitHub access verified: $($repo.full_name), push permission present.";exit 0}
$release=$null
try{$release=Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/tags/v2.0.0" -Headers $headers}catch{if([int]$_.Exception.Response.StatusCode -ne 404){throw}}
if(!$release){
    $notes=Get-Content -LiteralPath (Join-Path $projectRoot 'docs/RELEASE_2.0.0.md') -Raw
    $body=@{tag_name='v2.0.0';name='CatDom Solver 2.0.0';body=$notes;draft=$false;prerelease=$false}|ConvertTo-Json
    $release=Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repository/releases" -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
$artifactRoot=Join-Path $projectRoot 'artifacts/v2'
$files=@('CatDom.Solver.Cli-2.0.0-win-x64.zip','CatDom.Solver.Unity-2.0.0.zip','nuget/CatDom.Solver.2.0.0.nupkg','checksums.json')
foreach($file in $files){
    $path=Join-Path $artifactRoot $file;$name=[IO.Path]::GetFileName($path)
    if($release.assets.name -contains $name){Write-Output "Existing release asset retained: $name";continue}
    $upload="https://uploads.github.com/repos/$repository/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($name))"
    Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType 'application/octet-stream' -InFile $path | Out-Null
    Write-Output "Uploaded: $name"
}
$verified=Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/tags/v2.0.0" -Headers $headers
if($verified.draft -or $verified.assets.Count -lt 4){throw 'Published release is incomplete.'}
foreach($file in $files){
    $path=Join-Path $artifactRoot $file;$name=[IO.Path]::GetFileName($path)
    $asset=$verified.assets|Where-Object name -eq $name
    if(!$asset -or $asset.size -ne (Get-Item -LiteralPath $path).Length){throw "Asset size mismatch: $name"}
    if($asset.digest -and $asset.digest -ne ('sha256:'+(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant())){throw "Asset digest mismatch: $name"}
}
$record=@{url=$verified.html_url;tag=$verified.tag_name;assets=@($verified.assets | Select-Object name,size,browser_download_url,digest)}
$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $projectRoot 'docs/benchmarks/v2/github-release.json') -Encoding UTF8
Write-Output "Published and verified: $($verified.html_url)"
