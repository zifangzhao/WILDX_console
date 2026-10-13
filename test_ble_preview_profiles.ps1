param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\ble-preview-profile-tests",[string]$BaselineExecutable)
$ErrorActionPreference='Stop'
if(!$Compiler){
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface\USB512_interface'
$exe=Join-Path $OutputDirectory 'NpBlePreviewProfilesTests.exe'
$inputs=@("$PSScriptRoot\Tests\NpBlePreviewProfilesTests.cs","$PSScriptRoot\Tests\BleFeaturesTests.cs",
    "$source\Ble\NpV2Preview.cs","$source\Ble\NpV2PreviewWire.cs","$source\Ble\NpV2SdControl.cs","$source\Ble\NpSdControl.cs","$source\NpLogger\NpSdGeometry.cs")
& $Compiler /nologo /optimize+ /target:exe /main:NpBlePreviewProfilesTests /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @inputs
if($LASTEXITCODE){throw 'BLE preview profile fixture did not compile.'}
if($BaselineExecutable){& $exe ([IO.Path]::GetFullPath($BaselineExecutable))}else{& $exe}
if($LASTEXITCODE){throw 'BLE preview profile fixture failed.'}
