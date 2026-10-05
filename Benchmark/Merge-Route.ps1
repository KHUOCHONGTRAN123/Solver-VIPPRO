param(
    [string[]]$Runs = @('route-memory-v10','route-memory-v10-check','route-memory-v10-middle106','route-memory-v10-tail150','route-memory-v10-tail180','route-memory-v10-tail190','route-memory-v10-tail207','route-memory-v10-tail210','route-memory-v10-tail217','route-memory-v10-tail220','route-memory-v10-tail234','route-memory-v10-tail240','route-memory-v10-tail247','route-memory-v10-tail250','route-memory-v10-tail270','route-memory-v10-tail290','route-memory-v10-stopped'),
    [string]$OutputRun = 'route-final'
)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot ('results/' + $OutputRun)
$chosen = @{}; $manifest = $null
foreach ($run in $Runs) {
    $folder = Join-Path $PSScriptRoot ('results/' + $run)
    $current = Get-Content (Join-Path $folder 'manifest.json') -Raw | ConvertFrom-Json
    if ($null -eq $manifest) { $manifest = $current }
    foreach ($property in $manifest.CoreHashes.PSObject.Properties) {
        if ($current.CoreHashes.($property.Name) -ne $property.Value) { throw 'Cannot merge different core versions' }
    }
    foreach ($property in $manifest.InputHashes.PSObject.Properties) {
        if ($current.InputHashes.($property.Name) -ne $property.Value) { throw 'Cannot merge different level/IO inputs' }
    }
    foreach ($row in Import-Csv (Join-Path $folder 'metrics.csv')) {
        if (-not $chosen.ContainsKey($row.level)) { $chosen[$row.level] = @{row=$row;folder=$folder;run=$run} }
    }
}
if ($chosen.Count -ne 299) { throw "Incomplete: $($chosen.Count)/299 unique completed levels" }
New-Item -ItemType Directory -Force $output | Out-Null
$rows = @(); $provenance = @()
foreach ($level in $chosen.Keys | Sort-Object) {
    $entry = $chosen[$level]; $rows += $entry.row
    Copy-Item -LiteralPath (Join-Path $entry.folder ($level + '.json')) -Destination $output
    Copy-Item -LiteralPath (Join-Path $entry.folder ($level + '.memory.json')) -Destination $output
    $provenance += [pscustomobject]@{level=$level;sourceRun=$entry.run}
}
$rows | Export-Csv (Join-Path $output 'metrics.csv') -NoTypeInformation
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'metrics.json')
$provenance | Export-Csv (Join-Path $output 'provenance.csv') -NoTypeInformation
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'manifest.json')
& (Join-Path $PSScriptRoot 'Summarize-Route.ps1') -Run $OutputRun

