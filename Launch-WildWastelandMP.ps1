param(
    [ValidateSet('both','host','client')][string]$Mode = 'both',
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Apocalypter',
    [string]$World = 'WWMP_Local',
    [string]$Address = '127.0.0.1',
    [ValidateRange(1024,65535)][int]$Port = 27861
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Apocalypter.exe')) { $GameDir = $PSScriptRoot }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$mpDir = Join-Path $GameDir 'MP'
$clientDir = Join-Path $mpDir 'Client'
$roles = if ($Mode -eq 'both') { @('host','client') } else { @($Mode) }
foreach ($dll in @('BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll','BepInEx\plugins\ApocalypterMP\ApocalypterMP.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameDir $dll))) { throw "Missing $dll. Build/deploy WildWastelandMP first." }
}
$running = @(Get-CimInstance Win32_Process -Filter "Name='Apocalypter.exe'")
foreach ($role in $roles) {
    $exe = if ($role -eq 'host') { Join-Path $GameDir 'Apocalypter.exe' } else { Join-Path $clientDir 'Apocalypter.exe' }
    if ($running | Where-Object { $_.ExecutablePath -eq $exe }) { throw "$role is already running. Close its window before starting it again." }
}
function Copy-Default([string]$Source, [string]$Target) {
    if (!(Test-Path -LiteralPath $Target) -and (Test-Path -LiteralPath $Source)) {
        [IO.Directory]::CreateDirectory((Split-Path $Target)) | Out-Null
        Copy-Item -LiteralPath $Source -Destination $Target
    }
}
function Link-Directory([string]$Link, [string]$Target) {
    if (!(Test-Path -LiteralPath $Target)) { return }
    if (Test-Path -LiteralPath $Link) {
        $item = Get-Item -LiteralPath $Link -Force
        if ($item.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$item.Target).TrimEnd('\') -ne [IO.Path]::GetFullPath($Target).TrimEnd('\')) { throw "Unexpected existing folder: $Link" }
        return
    }
    [IO.Directory]::CreateDirectory((Split-Path $Link)) | Out-Null
    New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
}
if ($roles -contains 'client') {
    [IO.Directory]::CreateDirectory($clientDir) | Out-Null
    foreach ($file in @('Apocalypter.exe','UnityPlayer.dll','winhttp.dll','doorstop_config.ini','.doorstop_version')) {
        $source = Join-Path $GameDir $file; $target = Join-Path $clientDir $file
        if (Test-Path -LiteralPath $source) {
            if (!(Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $source).LastWriteTimeUtc -ne (Get-Item -LiteralPath $target).LastWriteTimeUtc) { Copy-Item -LiteralPath $source -Destination $target }
        }
    }
    foreach ($folder in @('Apocalypter_Data','MonoBleedingEdge','BepInEx\core','BepInEx\plugins','BepInEx\patchers')) { Link-Directory (Join-Path $clientDir $folder) (Join-Path $GameDir $folder) }
    foreach ($file in @('BepInEx.cfg','ApocalypterMP.cfg','com.denis.apocalypter.wildwasteland.cfg','WildWasteland\settings.cfg','WildWasteland\tools.cfg')) {
        Copy-Default (Join-Path $GameDir ('BepInEx\config\' + $file)) (Join-Path $clientDir ('BepInEx\config\' + $file))
    }
}
[IO.Directory]::CreateDirectory((Join-Path $mpDir 'Logs')) | Out-Null
$token = if ($Mode -eq 'both') { [Guid]::NewGuid().ToString('N') } else { '' }
$records = @()
foreach ($role in $roles) {
    $root = if ($role -eq 'host') { $GameDir } else { $clientDir }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $root 'Apocalypter.exe'; $info.WorkingDirectory = $root
    $info.UseShellExecute = $false; $info.CreateNoWindow = $false
    $info.Arguments = '-screen-fullscreen 0 -screen-width 960 -screen-height 720 -logFile "' + (Join-Path $mpDir ('Logs\' + $role + '.log')) + '"'
    foreach ($key in @('APOCMP_TEST','APOCMP_PROBE_FULL','APOCMP_LAG','APOCMP_SEEDS','WW_MP_SUITE','WW_MP_STRESS','WW_MP_FLAT_RAM','WW_MP_FAULT_SAVE','WW_MP_MANUAL','WW_MP_WORLD')) { $info.EnvironmentVariables.Remove($key) }
    $info.EnvironmentVariables['WWMP_ROLE'] = $role
    $info.EnvironmentVariables['WWMP_SAVES'] = Join-Path $mpDir ('Saves\' + $role)
    $info.EnvironmentVariables['WWMP_WORLD'] = $World
    $info.EnvironmentVariables['WWMP_READY'] = Join-Path $mpDir 'host-ready.txt'
    $info.EnvironmentVariables['WWMP_TOKEN'] = $token
    $info.EnvironmentVariables['WWMP_ADDRESS'] = $Address
    $info.EnvironmentVariables['WWMP_PORT'] = [string]$Port
    $process = [Diagnostics.Process]::Start($info)
    $records += [pscustomobject]@{Role=$role;Pid=$process.Id;Executable=$info.FileName;World=$World;Started=(Get-Date).ToString('o')}
}
$records | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mpDir ('last-' + $Mode + '.json'))
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WWMPWindows {
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr window, string text);
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
'@
$deadline = [DateTime]::UtcNow.AddSeconds(35)
$titled = @{}
do {
    foreach ($record in $records) {
        if ($titled.ContainsKey($record.Pid)) { continue }
        $process = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
        if (!$process -or $process.MainWindowHandle -eq [IntPtr]::Zero) { continue }
        [void][WWMPWindows]::SetWindowText($process.MainWindowHandle, ('WildWastelandMP - ' + $record.Role.ToUpperInvariant()))
        $width = [int]([WWMPWindows]::GetSystemMetrics(0) / 2)
        $x = if ($record.Role -eq 'host') { 0 } else { $width }
        [void][WWMPWindows]::MoveWindow($process.MainWindowHandle,$x,0,$width,[Math]::Min(800,[WWMPWindows]::GetSystemMetrics(1)-50),$true)
        $titled[$record.Pid] = $true
    }
    if ($titled.Count -lt $records.Count) { Start-Sleep -Milliseconds 250 }
} while ($titled.Count -lt $records.Count -and [DateTime]::UtcNow -lt $deadline)
Write-Host 'Host/client started with graphics. Saves: ' (Join-Path $mpDir 'Saves')
$records | Format-Table Role,Pid,World
