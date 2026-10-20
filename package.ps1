param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
$stage = Join-Path $PSScriptRoot ('out\package-' + $Version)
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage
$files = @('CodexQuota.exe')
foreach ($name in @('CodexQuota.ico','README.md','INSTALL.md','LICENSE','install.ps1','uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name) -Force
    $files += $name
}
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zip = Join-Path $dist ('codex-quota-widget-' + $Version + '-windows-x64.zip')
# Explicit allowlist excludes local settings and diagnostics on repeat builds.
Compress-Archive -LiteralPath @($files | ForEach-Object { Join-Path $stage $_ }) -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), [Text.Encoding]::ASCII)
Write-Host ('Packaged: ' + $zip)
