param([switch]$Benchmark)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot ('..\tests\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $testDirectory
$testExe = Join-Path $testDirectory 'MonitorRegressionTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /define:TEST_WORKER /main:RegressionTests /r:System.Windows.Forms.dll /r:System.Drawing.dll ("/win32manifest:" + (Join-Path $PSScriptRoot 'App.manifest')) ("/out:" + $testExe) (Join-Path $PSScriptRoot 'VramMonitorFramework.cs') (Join-Path $PSScriptRoot 'VramCapacityProbe.cs') (Join-Path $PSScriptRoot 'GpuTelemetryProvider.cs') (Join-Path $PSScriptRoot 'MonitorWorker.cs') (Join-Path $PSScriptRoot 'ModernTypography.cs') (Join-Path $PSScriptRoot 'RegressionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
if ($Benchmark) {
    & $testExe --benchmark
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
}
