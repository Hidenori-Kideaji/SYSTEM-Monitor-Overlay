param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\outputs'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler is missing. No installation was attempted.' }
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$output = Join-Path $OutputDirectory 'SYSTEM Monitor Overlay.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll ("/win32manifest:" + (Join-Path $PSScriptRoot 'App.manifest')) ("/win32icon:" + (Join-Path $PSScriptRoot 'VramMonitor.ico')) ("/out:" + $output) (Join-Path $PSScriptRoot 'VramMonitorFramework.cs') (Join-Path $PSScriptRoot 'VramCapacityProbe.cs') (Join-Path $PSScriptRoot 'GpuTelemetryProvider.cs') (Join-Path $PSScriptRoot 'MonitorWorker.cs') (Join-Path $PSScriptRoot 'ModernTypography.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
