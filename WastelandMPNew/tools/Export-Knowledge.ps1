param(
    [string]$WW2Root = 'V:\Repos\Apocalypter\WildWasteland2',
    [string]$NativeReviewRoot = 'V:\Repos\Apocalypter\_staging\apocapatrol-multiplayer-audit-20261011\mp-source',
    [string[]]$PastedPlans = @()
)
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $packageRoot -Parent
$referenceRoot = Join-Path $packageRoot 'reference'
New-Item -ItemType Directory -Force -Path $referenceRoot | Out-Null
$manifest = [System.Collections.Generic.List[object]]::new()

function Export-Reference([string]$source, [string]$relative, [string]$kind) {
    $target = Join-Path $referenceRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    $info = Get-Item -LiteralPath $source
    $manifest.Add([pscustomobject]@{ source=$source; bundled=('reference/' + $relative.Replace('\','/')); kind=$kind; bytes=$info.Length; sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash })
}

# These are our reports, not ApocalypterMP or optional-mod source code.
$documents = & rg -l -i --glob '*.md' 'multiplayer|ApocalypterMP|Vistas|EngineSim|ChaseCamera|Apocapocket|Dog Companion|Ultimate Tools' (Join-Path $repoRoot 'compat') (Join-Path $repoRoot 'notes') (Join-Path $repoRoot 'docs')
foreach ($source in ($documents | Sort-Object -Unique)) {
    $relative = $source.Substring($repoRoot.Length + 1)
    Export-Reference $source ('wwmp/' + $relative) 'WWMP historical evidence; latest dated correction wins'
}
Export-Reference (Join-Path $repoRoot 'AGENTS.md') 'wwmp/AGENTS.md' 'WWMP rules and dated evidence at extraction'
foreach ($file in @('README.md','core-modapi.md','work-packages.md','STATUS.md')) {
    $source = Join-Path $WW2Root ('docs/refactor/' + $file)
    if (Test-Path -LiteralPath $source) { Export-Reference $source ('ww2-refactor/' + $file) 'WW2 amended plan; section 0 overrides older pasted plan' }
}
for ($i = 0; $i -lt $PastedPlans.Length; $i++) {
    Export-Reference $PastedPlans[$i] ('pasted-plans/plan-' + ($i + 1) + '.txt') 'User-supplied planning context; not an instruction to execute refactor waves'
}

$sourceRows = [System.Collections.Generic.List[object]]::new()
$sources = & rg -l --glob '*.cs' 'Multiplayer|ApocalypterMP|OwnedElsewhere|WorldRunning|ClientReady|Vistas' (Join-Path $repoRoot 'src')
foreach ($source in ($sources | Sort-Object -Unique)) {
    $info = Get-Item -LiteralPath $source
    $sourceRows.Add([pscustomobject]@{ path=$source.Substring($repoRoot.Length + 1).Replace('\','/'); bytes=$info.Length; sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash })
}
$sourceRows | Export-Csv -LiteralPath (Join-Path $packageRoot 'SOURCE-INVENTORY.csv') -NoTypeInformation -Encoding UTF8

# Hashes and locator metadata only. Never bundle the inspected author's implementation.
if (Test-Path -LiteralPath $NativeReviewRoot) {
    $nativeRows = foreach ($file in (Get-ChildItem -LiteralPath $NativeReviewRoot -Recurse -File -Filter '*.cs')) {
        [pscustomobject]@{ path=$file.FullName.Substring($NativeReviewRoot.Length + 1).Replace('\','/'); bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    $nativeRows | Export-Csv -LiteralPath (Join-Path $packageRoot 'NATIVE-REVIEW-INVENTORY.csv') -NoTypeInformation -Encoding UTF8
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageRoot 'REFERENCE-MANIFEST.json') -Encoding UTF8
$index = [System.Collections.Generic.List[string]]::new()
$index.Add('# Frozen reference index')
$index.Add('')
$index.Add('Extracted 2026-10-22 from a dirty WWMP checkout. These are evidence snapshots, not current integration instructions. Read README.md, REVIEW.md, COMPATIBILITY.md and INTEGRATION.md first. Historical build hashes and packet numbers belong to WWMP, not this new protocol. No third-party source or binaries are bundled.')
$index.Add('')
foreach ($entry in $manifest) { $index.Add('- [' + $entry.bundled + '](' + $entry.bundled.Replace(' ','%20') + ')') }
$index | Set-Content -LiteralPath (Join-Path $packageRoot 'REFERENCE-INDEX.md') -Encoding UTF8
Write-Output ('Exported ' + $manifest.Count + ' evidence documents and indexed ' + $sourceRows.Count + ' WWMP source files.')
