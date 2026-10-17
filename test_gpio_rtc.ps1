param([string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\gpio-rtc-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$exe=Join-Path $OutputDirectory 'NpGpioRtcTests.exe'
& $Compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Management.dll /r:System.Web.Extensions.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\NpGpioRtcTests.cs') (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\NpGpioRtcLogger.cs')
if($LASTEXITCODE){throw 'GPIO RTC tests did not compile.'}
& $exe $OutputDirectory
if($LASTEXITCODE){throw 'GPIO RTC regression failed.'}
$warnings=Join-Path $OutputDirectory 'NpRecordingWarningTests.exe'
& $Compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$warnings" (Join-Path $PSScriptRoot 'Tests\NpRecordingWarningTests.cs')
if($LASTEXITCODE){throw 'Recording warning tests did not compile.'}
& $warnings $ConsoleExecutable $OutputDirectory
if($LASTEXITCODE){throw 'Recording warning regression failed.'}
$continuity=Join-Path $OutputDirectory 'NpGpioRtcContinuityTests.exe'
& $Compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$continuity" (Join-Path $PSScriptRoot 'Tests\NpGpioRtcContinuityTests.cs')
if($LASTEXITCODE){throw 'GPIO continuity tests did not compile.'}
& $continuity $ConsoleExecutable $OutputDirectory
if($LASTEXITCODE){throw 'GPIO continuity regression failed.'}
