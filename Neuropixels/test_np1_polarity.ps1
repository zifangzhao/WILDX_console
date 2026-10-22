param(
 [string]$Compiler,
 [string]$OutputDirectory="$PSScriptRoot\_artifacts\np1-polarity"
)
$ErrorActionPreference='Stop'
if(!$Compiler){
 $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
 $installation=& $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
 $Compiler=Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Roslyn C# compiler is required'}
$console=Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'Np1PolarityTests.exe'
$sources=@(
 "$PSScriptRoot\Tests\TestNp1Polarity.cs",
 "$console\XeNpStreamDecoder.cs","$console\NeuropixelsPreviewForm.cs","$console\NpCaptureModes.cs",
 "$console\NpLogger\NpCe64Decoder.cs","$console\NpLogger\NpCe64Export.cs","$console\NpLogger\NpSdGeometry.cs",
 "$PSScriptRoot\SD\NpV2SdDatExport.cs","$PSScriptRoot\USBConsoleV2\NpV2UsbConsole.cs","$console\Np2ConfigurationClient.cs",
 "$console\NpAdcCalibration.cs",
 "$PSScriptRoot\SD\NpNeuroscopeXml.cs","$PSScriptRoot\SD\NpMetadataFile.cs","$console\WildXTheme.cs","$console\NpRecordingSettings.cs","$console\NpProbeIdentity.cs",
 "$console\NpLogger\NpDownloadCatalog.cs","$console\Ble\NpMediaExport.cs","$console\Ble\WILD_VideoDecoder.cs",
 "$console\XeNpWinUsb.cs","$console\NpProbeManifest.cs","$console\USB512WinUsb.cs", "$console\UsbHealth.cs", "$console\UsbHealthNative.cs","$console\XeNpSdInfo.cs",
 "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs","$console\NpLogger\NpV1Management.cs",
 "$console\NpLogger\NpDownloadNative.cs","$console\NpLogger\NpDownloadStream.cs","$console\NpLogger\RawBurst.cs"
)
& $Compiler /nologo /optimize+ /define:NP2_CONSOLE_STANDALONE /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll "/out:$exe" @sources
if($LASTEXITCODE){throw 'NP1 polarity tests did not compile'}
& $exe ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'NP1 polarity tests failed'}
'PASS nominal ADC-count polarity/offset tests; file-only fixtures, no hardware access'
