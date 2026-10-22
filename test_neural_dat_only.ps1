param(
    [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
    [Parameter(Mandatory=$true)][string]$BaselineExecutable,
    [string]$OutputDirectory="$PSScriptRoot/_artifacts/neural-dat-only-tests"
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$tests=Join-Path $OutputDirectory 'NpNeuralDatOnlyTests.exe'
& $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /main:NpNeuralDatOnlyTests "/out:$tests" "$PSScriptRoot/Tests/NpNeuralDatOnlyTests.cs" "$PSScriptRoot/Tests/NpV1PowerCutRecoveryTests.cs"
if($LASTEXITCODE){throw 'DAT-only checks did not compile'}
& $tests ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($BaselineExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'comparison')))
if($LASTEXITCODE){throw 'DAT-only checks failed'}
$recovery=Join-Path $OutputDirectory 'NpV1PowerCutRecoveryTests.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll "/out:$recovery" "$PSScriptRoot/Tests/NpV1PowerCutRecoveryTests.cs"
if($LASTEXITCODE){throw 'Prefix recovery checks did not compile'}
& $recovery ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'recovery')))
if($LASTEXITCODE){throw 'Prefix recovery checks failed'}
