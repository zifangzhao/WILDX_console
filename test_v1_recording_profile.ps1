param([string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),[string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\v1-recording-profile-tests'),[string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$exe=Join-Path $OutputDirectory 'NpV1RecordingProfileTests.exe'
& $compiler /nologo /target:exe /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\NpV1RecordingProfileTests.cs')
if($LASTEXITCODE){throw 'NP1 recording-profile fixture compilation failed.'}
& $exe $ConsoleExecutable
if($LASTEXITCODE){throw 'NP1 recording-profile regression failed.'}
