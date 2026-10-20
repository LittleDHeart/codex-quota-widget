$ErrorActionPreference = 'Stop'
$source = Split-Path $PSScriptRoot -Parent
$target = Join-Path $source ('out\install-test-' + [Guid]::NewGuid().ToString('N'))
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function StartupValue { $value = Get-ItemProperty -Path $runKey -Name CodexQuotaWidget -ErrorAction SilentlyContinue; if ($value) { return $value.CodexQuotaWidget }; return $null }
$startupBefore = StartupValue
$processesBefore = @(Get-Process CodexQuota -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
& (Join-Path $source 'install.ps1') -InstallDirectory $target -NoLaunch -NoShortcut
if (!(Test-Path -LiteralPath (Join-Path $target 'CodexQuota.exe'))) { throw 'Executable not installed.' }
$prefs = Join-Path $target 'preferences.json'
$sentinel = '{"Theme":"dark","Topmost":false,"Left":123,"Top":456}'
[IO.File]::WriteAllText($prefs, $sentinel)
[IO.File]::WriteAllText((Join-Path $target 'unrelated.keep'), 'Do not remove me')
& (Join-Path $source 'install.ps1') -InstallDirectory $target -NoLaunch -NoShortcut
if ([IO.File]::ReadAllText($prefs) -ne $sentinel) { throw 'Upgrade changed preferences.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target 'uninstall.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Standalone uninstall failed.' }
if (!(Test-Path -LiteralPath $prefs)) { throw 'Uninstall did not preserve preferences.' }
if (Test-Path -LiteralPath (Join-Path $target 'CodexQuota.exe')) { throw 'Uninstall left the executable.' }
& (Join-Path $source 'install.ps1') -InstallDirectory $target -NoLaunch -NoShortcut
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target 'uninstall.ps1') -RemovePreferences
if ($LASTEXITCODE -ne 0) { throw 'Standalone uninstall with preference removal failed.' }
if (Test-Path -LiteralPath $prefs) { throw 'RemovePreferences did not remove preferences.' }
if (!(Test-Path -LiteralPath (Join-Path $target 'unrelated.keep'))) { throw 'Uninstall removed an unrelated file.' }
if ((StartupValue) -ne $startupBefore) { throw 'Staging installation changed the startup entry.' }
$processesAfter = @(Get-Process CodexQuota -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
if (($processesBefore -join ',') -ne ($processesAfter -join ',')) { throw 'Staging installation affected running widgets.' }
Write-Host 'PASS: install, upgrade, settings preservation, selective uninstall, no autostart/process side effects'
