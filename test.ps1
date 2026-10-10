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
$ble=Join-Path $output 'BleFeaturesTests.exe'
& $compiler /nologo /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$ble" (Join-Path $PSScriptRoot 'Tests\BleFeaturesTests.cs')
if($LASTEXITCODE){throw 'BLE feature tests did not compile.'}
& $ble $app $output
if($LASTEXITCODE){throw 'BLE feature regression failed.'}
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
