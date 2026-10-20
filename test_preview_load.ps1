param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/preview-load-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$exe=Join-Path $OutputDirectory 'NpPreviewLoadTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll "/out:$exe" "$PSScriptRoot/Tests/NpPreviewLoadTests.cs"
if($LASTEXITCODE){throw 'Preview load fixture compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable))
if($LASTEXITCODE){throw 'Preview load checks failed'}
