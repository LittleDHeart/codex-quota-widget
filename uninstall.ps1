[CmdletBinding()]
param([string]$InstallDirectory = $PSScriptRoot, [switch]$RemovePreferences)
$ErrorActionPreference = 'Stop'
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
if ($InstallDirectory.TrimEnd('\') -eq [IO.Path]::GetPathRoot($InstallDirectory).TrimEnd('\')) { throw 'Refusing a drive root.' }
$targetExe = Join-Path $InstallDirectory 'CodexQuota.exe'
if (!(Test-Path -LiteralPath $targetExe)) { throw 'No widget installation in this directory.' }
if (Test-Path -LiteralPath (Join-Path $InstallDirectory 'Widget.cs')) { throw 'This is a source checkout. Run the uninstaller from the installed directory.' }
$running = @(Get-Process -Name CodexQuota -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe })
if ($running.Count -gt 0) {
    $quit = Start-Process -FilePath $targetExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru -Wait
    foreach ($taskProcess in $running) { if (!$taskProcess.WaitForExit(10000)) { throw 'Close the installed widget before uninstalling.' } }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = Get-ItemProperty -Path $runKey -Name CodexQuotaWidget -ErrorAction SilentlyContinue
if ($startup -and $startup.CodexQuotaWidget -eq ('"' + $targetExe + '" --autostart')) { Remove-ItemProperty -Path $runKey -Name CodexQuotaWidget }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Quota Widget.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcutPath).TargetPath -eq $targetExe) { Remove-Item -LiteralPath $shortcutPath }
}
# Delete only application-owned files, never recursively delete a directory.
$names = @('CodexQuota.exe','CodexQuota.ico','README.md','INSTALL.md','LICENSE','error.log','probe.json','attachment-probe.json','uninstall.ps1')
if ($RemovePreferences) { $names += @('preferences.json','preferences.json.tmp') }
foreach ($name in $names) {
    $file = Join-Path $InstallDirectory $name
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
}
if (@(Get-ChildItem -LiteralPath $InstallDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $InstallDirectory }
Write-Host 'Uninstalled. Preferences were preserved unless -RemovePreferences was supplied.'
