param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/preview-gain-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$exe=Join-Path $OutputDirectory 'NpPreviewGainTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot/Tests/NpPreviewGainTests.cs"
if($LASTEXITCODE){throw 'Preview gain checks did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Preview gain checks failed'}
