param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$FixtureDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh boundary output directory required.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$sdk=Join-Path $SdkDirectory 'NpV2SdDatExport.dll'
Copy-Item -LiteralPath $sdk -Destination $OutputDirectory
$csc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /optimize+ /langversion:7.3 /target:exe "/r:$sdk" "/out:$OutputDirectory/SdkBoundary.exe" "$PSScriptRoot/Tests/NpV2SdGrid16AssemblyTests.cs"
if($LASTEXITCODE){throw 'SDK boundary fixture compilation failed.'}
& "$OutputDirectory/SdkBoundary.exe" $FixtureDirectory "$OutputDirectory/exports"
if($LASTEXITCODE){throw 'SDK boundary fixture failed.'}
if((Get-FileHash -LiteralPath $sdk).Hash -ne (Get-FileHash -LiteralPath "$OutputDirectory/NpV2SdDatExport.dll").Hash){throw 'SDK copy identity mismatch.'}
