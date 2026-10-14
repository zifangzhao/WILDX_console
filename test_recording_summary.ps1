param([string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\recording-summary-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$exe=Join-Path $OutputDirectory 'NpRecordingSummaryTests.exe'
& $Compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\NpRecordingSummaryTests.cs')
if($LASTEXITCODE){throw 'Recording summary tests did not compile.'}
& $exe $ConsoleExecutable $OutputDirectory
if($LASTEXITCODE){throw 'Recording summary regression failed.'}
