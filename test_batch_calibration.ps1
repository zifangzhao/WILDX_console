param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/batch-calibration-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$sdk=Join-Path (Split-Path ([IO.Path]::GetFullPath($ConsoleExecutable))) 'NpV2SdDatExport.dll'
Copy-Item -LiteralPath $sdk -Destination (Join-Path $OutputDirectory 'NpV2SdDatExport.dll')
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$tests=Join-Path $OutputDirectory 'NpCalibrationBatchTests.exe'
& $compiler /nologo /warnaserror /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$sdk" "/out:$tests" "$PSScriptRoot/Tests/NpCalibrationBatchTests.cs"
if($LASTEXITCODE){throw 'Batch calibration checks did not compile'}
& $tests ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'results')))
if($LASTEXITCODE){throw 'Batch calibration checks failed'}
