param([Parameter(Mandatory=$true)][string]$AppHex,[Parameter(Mandatory=$true)][string]$FusedHex,[Parameter(Mandatory=$true)][string]$ExpectedFlash,[string]$OutputDirectory="$PSScriptRoot\_artifacts\firmware-flash-tests")
$ErrorActionPreference='Stop'
& "$PSScriptRoot\test_np_inplace_update.ps1" -OutputDirectory (Join-Path $OutputDirectory 'resident')
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$console=[IO.Path]::GetFullPath("$PSScriptRoot\..\USB512_interface\USB512_interface")
$exe=Join-Path $OutputDirectory 'TestNpFirmwareFlash.exe'
& $compiler /nologo /warnaserror /optimize+ /main:TestNpFirmwareFlash "/out:$exe" (Join-Path $OutputDirectory 'resident/NpV1Management.mock.cs') "$PSScriptRoot\Tests\TestNpInPlaceUpdate.cs" "$console\NpLogger\NpFirmwareImage.cs" "$console\NpLogger\NpFirmwareFlash.cs" "$PSScriptRoot\Tests\TestNpFirmwareFlash.cs"
if($LASTEXITCODE){throw 'Firmware flow test compilation failed'}
$fake=Join-Path $OutputDirectory 'STM32_Programmer_CLI.exe'
& $compiler /nologo /warnaserror /optimize+ "/out:$fake" "$PSScriptRoot\Tests\FakeCubeCli.cs"
if($LASTEXITCODE){throw 'Fake CLI compilation failed'}
# Work on private copies; negative tests never change release firmware inputs.
$app=Join-Path $OutputDirectory 'app.hex';$fused=Join-Path $OutputDirectory 'full.hex'
Copy-Item -LiteralPath $AppHex -Destination $app;Copy-Item -LiteralPath $FusedHex -Destination $fused
& $exe $app $fused ([IO.Path]::GetFullPath($ExpectedFlash)) ([IO.Path]::GetFullPath($OutputDirectory)) ([IO.Path]::GetFullPath($fake))
if($LASTEXITCODE){throw 'Firmware flow checks failed'}
