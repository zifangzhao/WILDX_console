param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/firmware-sparse-image")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Firmware/releases.json') -Raw | ConvertFrom-Json
$v1=$catalog.releases | Where-Object family -eq 1
$v2=$catalog.releases | Where-Object family -eq 2
$full1=$v1.files | Where-Object path -match 'fused_'
$full2=$v2.full
if(!$full2){$full2=$v2.files | Where-Object path -match 'fused_'}
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $compiler /nologo /warnaserror /r:System.Core.dll "/out:$OutputDirectory/NpFirmwareSparseImageTests.exe" "$PSScriptRoot/Tests/NpFirmwareSparseImageTests.cs" "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpFirmwareImage.cs"
if($LASTEXITCODE){throw 'Sparse firmware parser tests did not compile.'}
& "$OutputDirectory/NpFirmwareSparseImageTests.exe" ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path "$PSScriptRoot/Firmware" $full2.path) $full2.sha256 (Join-Path "$PSScriptRoot/Firmware" $full1.path) (Join-Path "$PSScriptRoot/Firmware" $v1.app.path) ([IO.Path]::GetFullPath("$OutputDirectory/results"))
if($LASTEXITCODE){throw 'Sparse firmware parser tests failed.'}
