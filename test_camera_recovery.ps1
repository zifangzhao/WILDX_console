param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[Parameter(Mandatory=$true)][string]$Ffmpeg,[string]$OutputDirectory="$PSScriptRoot/_artifacts/camera-recovery-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskExe=Join-Path $OutputDirectory 'NpCameraRecoveryTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /r:System.Web.Extensions.dll "/out:$taskExe" "$PSScriptRoot/Tests/NpCameraRecoveryTests.cs"
if($LASTEXITCODE){throw 'Camera recovery fixture compilation failed'}
& $taskExe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'converted'))) ([IO.Path]::GetFullPath($Ffmpeg))
if($LASTEXITCODE){throw 'Camera recovery checks failed'}
