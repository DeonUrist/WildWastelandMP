param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2020.3.49f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$crawlProject = Join-Path $PSScriptRoot 'Unity'
$crawlOutput = Join-Path $crawlProject 'Build'
New-Item -ItemType Directory -Path $crawlOutput -Force | Out-Null
$crawlLog = Join-Path $crawlOutput 'asset-build.log'
$crawlArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $crawlProject + '"'), '-executeMethod', 'CrawlAssetBuild.Build', '-logFile', ('"' + $crawlLog + '"'))
$crawlProcess = Start-Process -FilePath $UnityEditor -ArgumentList $crawlArgs -WindowStyle Hidden -Wait -PassThru
if ($crawlProcess.ExitCode -ne 0) { throw "Crawl asset authoring failed: $crawlLog" }
$crawlTarget = Join-Path $PSScriptRoot '..\..\src\Modules\PlayerBody\Embedded\Crawl\low-crawl.bundle'
New-Item -ItemType Directory -Path (Split-Path $crawlTarget) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $crawlOutput 'low-crawl.bundle') -Destination $crawlTarget
Write-Output "Embedded crawl bundle updated. Nothing deployed to the game. Log: $crawlLog"
