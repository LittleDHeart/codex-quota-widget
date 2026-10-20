param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'out\test'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:exe ('/out:' + (Join-Path $OutputDirectory 'MockCodex.exe')) ('/reference:' + (Join-Path $framework 'System.Web.Extensions.dll')) (Join-Path $PSScriptRoot 'tests\MockCodex.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mock compilation failed' }
$previousCli = $env:CODEX_QUOTA_CLI
try {
 $env:CODEX_QUOTA_CLI = Join-Path $OutputDirectory 'MockCodex.exe'
 $taskProcess = Start-Process -FilePath (Join-Path $OutputDirectory 'CodexQuota.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
 if ($taskProcess.ExitCode -ne 0) { throw 'Model tests failed' }
 $taskProcess = Start-Process -FilePath (Join-Path $OutputDirectory 'CodexQuota.exe') -ArgumentList '--probe' -WindowStyle Hidden -PassThru -Wait
 if ($taskProcess.ExitCode -ne 0) { throw 'RPC integration failed' }
 $result = Get-Content -LiteralPath (Join-Path $OutputDirectory 'probe.json') -Raw | ConvertFrom-Json
 if ($result.source -ne 'api' -or $result.buckets[0].Primary.Remaining -ne 63 -or $result.buckets[0].Secondary.Remaining -ne 19) { throw 'RPC values did not match the fake server' }
 $taskProcess = Start-Process -FilePath (Join-Path $OutputDirectory 'CodexQuota.exe') -ArgumentList '--ui-test' -WindowStyle Hidden -PassThru -Wait
 if ($taskProcess.ExitCode -ne 0) { throw 'UI interaction tests failed' }
 $taskProcess = Start-Process -FilePath (Join-Path $OutputDirectory 'CodexQuota.exe') -ArgumentList '--attachment-test' -WindowStyle Hidden -PassThru -Wait
 if ($taskProcess.ExitCode -ne 0) { throw 'Window attachment tests failed' }
 $taskUiResults = Get-Content -LiteralPath (Join-Path $OutputDirectory 'ui-test-results.json') -Raw | ConvertFrom-Json
 $taskAttachmentResults = Get-Content -LiteralPath (Join-Path $OutputDirectory 'attachment-test-results.json') -Raw | ConvertFrom-Json
 Write-Host ('PASS: 7 model tests + stdio integration + ' + $taskUiResults.passed + ' UI tests + ' + $taskAttachmentResults.passed + ' window attachment tests')
} finally {
 $env:CODEX_QUOTA_CLI = $previousCli
}
