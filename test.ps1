$ErrorActionPreference='Stop'
$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
$output=Join-Path $PSScriptRoot '_artifacts\standalone-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe=Join-Path $output 'StandaloneTests.exe'
$app=Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'
& $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" (Join-Path $PSScriptRoot 'Tests\StandaloneTests.cs')
if($LASTEXITCODE){throw 'Standalone tests did not compile.'}
& $exe $app $output
if($LASTEXITCODE){throw 'Standalone regression failed.'}
& (Join-Path $PSScriptRoot 'test_universal_workflow.ps1') -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'universal-workflow')
& (Join-Path $PSScriptRoot 'test_recording_bundle.ps1') -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'recording-bundle')
& (Join-Path $PSScriptRoot 'test_media_preview.ps1') -Compiler $compiler -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'media-preview')
$ble=Join-Path $output 'BleFeaturesTests.exe'
& $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$ble" (Join-Path $PSScriptRoot 'Tests\BleFeaturesTests.cs')
if($LASTEXITCODE){throw 'BLE feature tests did not compile.'}
& $ble $app $output
if($LASTEXITCODE){throw 'BLE feature regression failed.'}
$clock=Join-Path $output 'NpBleClockTests.exe'
$clockSources=@(
    (Join-Path $PSScriptRoot 'Tests\NpBleClockTests.cs'),
    (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\Ble\NpBleClock.cs'),
    (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\Ble\NpV2OnlineClock.cs'),
    (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\Ble\NpSdControl.cs'),
    (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\NpLogger\NpSdGeometry.cs')
)
& $compiler /nologo /warnaserror /optimize+ "/out:$clock" @clockSources
if($LASTEXITCODE){throw 'BLE clock tests did not compile.'}
& $clock
if($LASTEXITCODE){throw 'BLE clock regression failed.'}
$warm=Join-Path $output 'NpBleWarmLinkTests.exe'
& $compiler /nologo /warnaserror /optimize+ "/out:$warm" (Join-Path $PSScriptRoot 'Tests\NpBleWarmLinkTests.cs')
if($LASTEXITCODE){throw 'BLE warm-link tests did not compile.'}
& $warm (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\Ble\NpBleConnection.cs') $compiler
if($LASTEXITCODE){throw 'BLE warm-link regression failed.'}
$frameWrite=Join-Path $output 'NpBleFrameWriteTests.exe'
& $compiler /nologo /warnaserror /optimize+ "/out:$frameWrite" (Join-Path $PSScriptRoot 'Tests\NpBleFrameWriteTests.cs')
if($LASTEXITCODE){throw 'BLE frame-write tests did not compile.'}
& $frameWrite (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\Ble\NpBleConnection.cs') $compiler
if($LASTEXITCODE){throw 'BLE frame-write regression failed.'}
$bleConfig=Join-Path $output 'Np2BleConfigurationTests.exe'
& $compiler /nologo /optimize+ /r:System.Core.dll "/out:$bleConfig" (Join-Path $PSScriptRoot 'Tests\Np2BleConfigurationTests.cs')
if($LASTEXITCODE){throw 'NP2 BLE configuration tests did not compile.'}
& $bleConfig $app
if($LASTEXITCODE){throw 'NP2 BLE configuration regression failed.'}
& (Join-Path $PSScriptRoot 'test_np2_degraded_chains.ps1') -Compiler $compiler -OutputDirectory (Join-Path $output 'np2-degraded-chains')
$updater=Join-Path (Split-Path -Parent $app) 'FirmwareUpdater\XeNpFirmwareUpdater.exe'
& $updater --selector-self-test
if($LASTEXITCODE){throw 'Updater selector regression failed.'}
& (Join-Path $PSScriptRoot 'Neuropixels\test_np_inplace_update.ps1') -Compiler $compiler -OutputDirectory (Join-Path $output 'np1-inplace-update')
& (Join-Path $PSScriptRoot 'Neuropixels\test_np_management_forms.ps1') -Compiler $compiler -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'np1-management-forms')
$np1=Join-Path $output 'Np1StartupTests.exe'
$source=Join-Path $PSScriptRoot 'USB512_interface\USB512_interface'
& $compiler /nologo /optimize+ "/out:$np1" (Join-Path $PSScriptRoot 'Neuropixels\Tests\TestNp1Startup.cs') (Join-Path $source 'XeNpStreamDecoder.cs') (Join-Path $source 'NpLogger\NpCe64Decoder.cs')
if($LASTEXITCODE){throw 'NP1 startup regression did not compile.'}
& $np1
if($LASTEXITCODE){throw 'NP1 startup decoder regression failed.'}
& (Join-Path $PSScriptRoot 'Neuropixels\test_sd_dat.ps1') -Compiler $compiler -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'sd-dat')
if($LASTEXITCODE){throw 'NP2 decoder/export regression failed.'}
& (Join-Path $PSScriptRoot 'Neuropixels\test_np1_polarity.ps1') -Compiler $compiler -OutputDirectory (Join-Path $output 'np1-polarity')
& (Join-Path $PSScriptRoot 'test_adc_calibration.ps1') -Compiler $compiler -ConsoleExecutable $app -OutputDirectory (Join-Path $output 'adc-calibration')
