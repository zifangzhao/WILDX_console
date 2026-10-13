param([string]$Compiler,[string]$OutputDirectory="$PSScriptRoot\_artifacts\np2-degraded-chain-tests")
$ErrorActionPreference='Stop'
if(!$Compiler){
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface\USB512_interface'
$exe=Join-Path $OutputDirectory 'Np2DegradedChainTests.exe'
$inputs=@("$PSScriptRoot\Tests\Np2DegradedChainTests.cs","$source\Np2ConfigurationClient.cs","$source\Ble\NpBleConfigurationReceipts.cs",
    "$PSScriptRoot\Neuropixels\USBConsoleV2\NpV2UsbConsole.cs","$source\USB512WinUsb.cs","$source\NeuropixelsPreviewForm.cs","$source\XeNpStreamDecoder.cs")
& $Compiler /nologo /optimize+ /define:NP2_CONSOLE_STANDALONE /target:exe /main:Np2DegradedChainTests /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" @inputs
if($LASTEXITCODE){throw 'Degraded-chain source fixture did not compile.'}
& $exe
if($LASTEXITCODE){throw 'Degraded-chain source fixture failed.'}
