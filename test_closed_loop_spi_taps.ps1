param([Parameter(Mandatory=$true)][string]$Sdk,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$NativeMetadata)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh test output required'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Copy-Item -LiteralPath $Sdk -Destination (Join-Path $OutputDirectory 'NpV2SdDatExport.dll')
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $compiler /nologo /warnaserror ("/r:"+$Sdk) ("/out:"+(Join-Path $OutputDirectory 'SpiTapTests.exe')) "$PSScriptRoot/Tests/NpClosedLoopSpiTapTests.cs"
if($LASTEXITCODE){throw 'SPI tap check compilation failed'}
$testArgs=@("$PSScriptRoot/Tests/Fixtures/closed-loop/metadata.bin")
if($NativeMetadata){$testArgs+=$NativeMetadata}
& (Join-Path $OutputDirectory 'SpiTapTests.exe') @testArgs
if($LASTEXITCODE){throw 'SPI tap check failed'}
