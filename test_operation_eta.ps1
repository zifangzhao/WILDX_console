param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/operation-eta-tests",[string]$Ffmpeg)
$ErrorActionPreference='Stop';New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$sdk=Join-Path (Split-Path ([IO.Path]::GetFullPath($ConsoleExecutable))) 'NpV2SdDatExport.dll';Copy-Item -LiteralPath $sdk -Destination "$OutputDirectory/NpV2SdDatExport.dll" -Force
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe';$test="$OutputDirectory/NpOperationEtaTests.exe"
& $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$sdk" "/out:$test" "$PSScriptRoot/Tests/NpOperationEtaTests.cs"
if($LASTEXITCODE){throw 'Operation ETA checks did not compile'}
$arguments=@([IO.Path]::GetFullPath($ConsoleExecutable),[IO.Path]::GetFullPath("$OutputDirectory/results"));if($Ffmpeg){$arguments+=[IO.Path]::GetFullPath($Ffmpeg)}
& $test @arguments
if($LASTEXITCODE){throw 'Operation ETA checks failed'}
