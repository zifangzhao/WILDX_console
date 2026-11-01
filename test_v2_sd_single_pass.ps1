param(
 [Parameter(Mandatory=$true)][string]$CandidateSdk,
 [string]$BaselineSdk='C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_host/enabled221-sweep/NpV2SdDatExport.dll',
 [string]$FixtureExecutable,
 [string]$FixtureDirectory,
 [string]$YoshiMetadataDirectory='C:/Users/antfe/Documents/Codex/2026-10-13/s/work/download-triage-55718538',
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if(!$FixtureExecutable){$FixtureExecutable=Join-Path $PSScriptRoot '_artifacts/deterministic_imu_next/sd16-append-host/focused-final/NpV2SdDatExportTests.exe'}
if(!$FixtureDirectory){$FixtureDirectory=Join-Path $PSScriptRoot '_artifacts/deterministic_imu_next/sd16-append-host/focused-final/export_tests_c5fbc575e3ea4ae7a1a93310a5b6bed2'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh test output required.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'NpV2SdSinglePassTests.exe'
& $compiler /nologo /optimize+ /langversion:7.3 /target:exe /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/out:$exe" "$PSScriptRoot/Tests/NpV2SdSinglePassTests.cs"
if($LASTEXITCODE){throw 'Tests-only regression executable did not compile.'}
& $exe ([IO.Path]::GetFullPath($CandidateSdk)) ([IO.Path]::GetFullPath($BaselineSdk)) ([IO.Path]::GetFullPath($FixtureExecutable)) ([IO.Path]::GetFullPath($FixtureDirectory)) (Join-Path $OutputDirectory 'exports') $YoshiMetadataDirectory
if($LASTEXITCODE){throw 'One-pass SDK regression failed; retain its partial outputs and logs.'}
