[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'CodexQuotaWidget'),
    [switch]$StartWithWindows,
    [switch]$NoLaunch,
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
if ($InstallDirectory.TrimEnd('\') -eq [IO.Path]::GetPathRoot($InstallDirectory).TrimEnd('\')) { throw 'Choose an application directory, not a drive root.' }
if ($InstallDirectory.TrimEnd('\') -eq $PSScriptRoot.TrimEnd('\')) { throw 'Install to a directory separate from the source or extracted release.' }
$buildDirectory = Join-Path $PSScriptRoot 'out\install'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Widget.cs')) {
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $buildDirectory
    $sourceExe = Join-Path $buildDirectory 'CodexQuota.exe'
} else {
    $sourceExe = Join-Path $PSScriptRoot 'CodexQuota.exe'
    if (!(Test-Path -LiteralPath $sourceExe)) { throw 'Missing source code and CodexQuota.exe. Download the full repository or release ZIP.' }
}
$targetExe = Join-Path $InstallDirectory 'CodexQuota.exe'
# Stop only a running copy installed in the selected destination.
$running = @(Get-Process -Name CodexQuota -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe })
if ($running.Count -gt 0) {
    $quit = Start-Process -FilePath $targetExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru -Wait
    foreach ($taskProcess in $running) { if (!$taskProcess.WaitForExit(10000)) { throw 'Close the installed widget before upgrading.' } }
}
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
foreach ($name in @('CodexQuota.ico','README.md','INSTALL.md','LICENSE','uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $InstallDirectory $name) -Force
}
if (!$NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Quota Widget.lnk'))
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.IconLocation = Join-Path $InstallDirectory 'CodexQuota.ico'
    $shortcut.Save()
}
if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (!(Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey -Force | Out-Null }
    New-ItemProperty -Path $runKey -Name CodexQuotaWidget -Value ('"' + $targetExe + '" --autostart') -PropertyType String -Force | Out-Null
}
Write-Host ('Installed: ' + $targetExe)
if (!$NoLaunch) { Start-Process -FilePath $targetExe -WorkingDirectory $InstallDirectory -WindowStyle Hidden }
