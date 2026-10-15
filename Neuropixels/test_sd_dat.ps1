param(
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\sd-dat-tests",
    [string]$ConsoleExecutable,
    [switch]$MetadataV12,
    [switch]$RuntimeProfiles,
    [string]$BaselineExporterPath,
    [switch]$CompileOnly
)
$ErrorActionPreference = 'Stop'
if (-not $Compiler) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Pass -Compiler with the full path to Roslyn csc.exe.' }
    $installation = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    $Compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
}
if (-not (Test-Path -LiteralPath $Compiler)) { throw "C# compiler missing: $Compiler" }
$console = Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'NpV2SdDatExportTests.exe'
$sources = @(
    "$PSScriptRoot\SD\NpV2SdDatExport.cs",
    "$console\NpAdcCalibration.cs",
    "$PSScriptRoot\SD\NpNeuroscopeXml.cs",
    "$PSScriptRoot\SD\NpMetadataFile.cs",
    "$PSScriptRoot\USBConsoleV2\NpV2UsbConsole.cs", "$console\Np2ConfigurationClient.cs",
    "$console\USB512WinUsb.cs", "$console\UsbHealth.cs", "$console\UsbHealthNative.cs", "$console\NeuropixelsPreviewForm.cs", "$console\XeNpStreamDecoder.cs",
    "$PSScriptRoot\Tests\TestNpV2SdDatExport.cs", "$PSScriptRoot\Tests\test_np_v2_usb_console.cs",
    "$console\NpLogger\NpCe64Export.cs", "$console\NpLogger\NpCe64Decoder.cs", "$console\NpLogger\NpSdGeometry.cs",
    "$console\NpRecordingSettings.cs", "$console\NpProbeManifest.cs", "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs",
    "$console\NpLogger\NpDownloadCatalog.cs", "$console\NpLogger\NpDownloadStream.cs", "$console\NpLogger\NpDownloadNative.cs", "$console\NpLogger\RawBurst.cs",
    "$console\Ble\NpMediaExport.cs", "$console\Ble\WILD_VideoDecoder.cs"
    "$console\WildXTheme.cs"
)
& $Compiler /nologo /optimize+ /define:NP2_CONSOLE_STANDALONE /target:exe /main:NpV2SdDatExportTests /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'SD DAT exporter tests did not compile.' }
if($CompileOnly){return}
$arguments = @($OutputDirectory)
if($RuntimeProfiles){
    if($MetadataV12 -or $ConsoleExecutable){throw 'Runtime profile fixture is standalone; do not combine selectors.'}
    $arguments+='--runtime-only'
}
if ($ConsoleExecutable) { $arguments += [IO.Path]::GetFullPath($ConsoleExecutable) }
if($MetadataV12){
    if(-not $ConsoleExecutable){throw 'The focused v11/v12 check requires -ConsoleExecutable.'}
    $arguments+='--v12-only'
    if($BaselineExporterPath){$arguments += [IO.Path]::GetFullPath($BaselineExporterPath)}
}
& $exe @arguments
if ($LASTEXITCODE) { throw 'SD DAT exporter tests failed.' }
'PASS project-owned NP2 SD-to-DAT fixtures; no hardware was accessed.'
