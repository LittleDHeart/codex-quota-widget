param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$arguments = @('/nologo','/codepage:65001','/target:winexe','/optimize+','/platform:anycpu',('/out:' + (Join-Path $OutputDirectory 'CodexQuota.exe')),('/resource:' + (Join-Path $PSScriptRoot 'Ring.xaml') + ',Widget.xaml'),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')))
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { throw 'Windows .NET Framework compiler not found. Requires Windows x64 with .NET Framework 4.7.2 or newer.' }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CodexQuota.ico')) { $arguments += '/win32icon:' + (Join-Path $PSScriptRoot 'CodexQuota.ico') }
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $framework $ref) }
$arguments += Join-Path $PSScriptRoot 'Widget.cs'
$arguments += Join-Path $PSScriptRoot 'RingWidget.cs'
$arguments += Join-Path $PSScriptRoot 'WindowAttachment.cs'
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Host ('Built ' + (Join-Path $OutputDirectory 'CodexQuota.exe'))
