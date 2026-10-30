param(
    [string]$Compiler,
    [string]$OutputDirectory = "$PSScriptRoot\_artifacts\sd-dat-tests",
    [string]$ConsoleExecutable,
    [string]$DownloadStreamSource,
    [switch]$MetadataV12,
    [switch]$RuntimeProfiles,
    [switch]$Append14,
    [switch]$Imu,
    [switch]$Grid16,
    [switch]$EnableFirstSync,
    [string]$NativeAppendFixtureDirectory="$PSScriptRoot/Tests/Fixtures/Append14",
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
if(-not $DownloadStreamSource){$DownloadStreamSource="$console\NpLogger\NpDownloadStream.cs"}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'NpV2SdDatExportTests.exe'
$sources = @(
    "$PSScriptRoot\ClosedLoop\NpClosedLoopEvent.cs",
    "$PSScriptRoot\ClosedLoop\NpClosedLoopControl.cs",
    "$PSScriptRoot\ClosedLoop\NpClosedLoopCarrier.cs",
    "$PSScriptRoot\ClosedLoop\NpClosedLoopLog.cs",
    "$console\NpAcquisitionAnchor.cs",
    "$console\NpRecordingUtc.cs",
    "$console\NpFirstSyncWire.cs", "$console\NpFirstSyncSd.cs", "$console\NpFirstSyncUtc.cs", "$console\NpFirstSyncSdUtc.cs", "$console\NpDerivedImuGridUtc.cs",
    "$console\NpFineClockWire.cs", "$console\NpFineSdArchive.cs", "$console\NpRtcStartWire.cs", "$console\NpRtcRecordingUtc.cs", "$console\NpRtcSdArchive.cs", "$console\NpRtcUsbArchive.cs",
    "$console\NpFineClockFit.cs", "$console\NpFineClockRecording.cs", "$console\NpFineClockEvidenceImport.cs", "$console\NpSensorFineWire.cs", "$console\NpSensorFine64Wire.cs", "$console\NpSensorFineTime.cs", "$console\NpSdFineAnalog.cs",
    "$console\NpFirstSyncUsb.cs", "$console\NpFirstSyncUsbEvidence.cs", "$console\NpFirstSyncUsbUtc.cs",
    "$console\NpUsbTimingWire.cs",
    "$console\NpUsbTimingEvidence.cs",
    "$console\NpHostClock.cs",
    "$console\NpTimeCorrection.cs",
    "$console\NpProgressEstimate.cs", "$console\NpV2ReadHistory.cs",
    "$PSScriptRoot\SD\NpV2SdDatExport.cs", "$PSScriptRoot\SD\NpV2SdGrid.cs",
    "$console\NpV2GridSector.cs", "$console\NpImuGridWire.cs", "$console\NpImuGridPhase.cs", "$console\NpImuGridCarrier.cs", "$console\NpImuGridWriter.cs",
    "$PSScriptRoot\SD\NpSdImuExport.cs",
    "$console\NpAdcCalibration.cs",
    "$PSScriptRoot\SD\NpNeuroscopeXml.cs",
    "$PSScriptRoot\SD\NpMetadataFile.cs",
    "$PSScriptRoot\USBConsoleV2\NpV2UsbConsole.cs", "$console\Np2ConfigurationClient.cs",
    "$console\USB512WinUsb.cs", "$console\UsbHealth.cs", "$console\UsbHealthNative.cs", "$console\NeuropixelsPreviewForm.cs", "$console\XeNpStreamDecoder.cs",
    "$PSScriptRoot\Tests\TestNpV2SdDatExport.cs", "$PSScriptRoot\Tests\TestNpV2SdAppend.cs", "$PSScriptRoot\Tests\test_np_v2_usb_console.cs",
    "$PSScriptRoot\Tests\TestNpV2SdClosedLoop.cs",
    "$PSScriptRoot\Tests\TestNpV2SdImu.cs", "$PSScriptRoot\Tests\TestNpV2SdGrid16.cs", "$PSScriptRoot\Tests\TestNpV2SdGridAppend.cs",
    "$PSScriptRoot\..\Tests\NpFirstSyncWireTests.cs",
    "$console\NpLogger\NpCe64Export.cs", "$console\NpLogger\NpSdDatMerge.cs", "$console\NpLogger\NpCe64Decoder.cs", "$console\NpLogger\NpSdGeometry.cs",
    "$console\NpRecordingSettings.cs","$console\NpProbeIdentity.cs", "$console\NpProbeManifest.cs", "$PSScriptRoot\V1Capture\Configuration\NpConfigClient.cs",
    "$console\NpLogger\NpDownloadCatalog.cs", "$console\NpLogger\NpV1StoreV2.cs", $DownloadStreamSource, "$console\NpLogger\NpDownloadNative.cs", "$console\NpLogger\RawBurst.cs",
    "$console\NpLogger\NpDownloadBurst.cs", "$console\NpLogger\NpDownloadStream.Progress.cs",
    "$console\Ble\NpMediaExport.cs", "$console\Ble\WILD_VideoDecoder.cs"
    "$console\WildXTheme.cs"
)
$defines=if($EnableFirstSync){'/define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE'}else{'/define:NP2_CONSOLE_STANDALONE'}
& $Compiler /nologo /optimize+ $defines /target:exe /main:NpV2SdDatExportTests /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Xml.Linq.dll /langversion:7.3 "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'SD DAT exporter tests did not compile.' }
if($CompileOnly){return}
$arguments = @($OutputDirectory)
if($Grid16){
    if($Imu -or $Append14 -or $RuntimeProfiles -or $MetadataV12 -or $ConsoleExecutable -or $BaselineExporterPath){throw 'SD16 is a focused source fixture; do not combine selectors.'}
    $arguments+=@('--grid16-only',[IO.Path]::GetFullPath("$PSScriptRoot/../Tests/Fixtures/SdGrid16"))
}
if($Imu){
    if($Append14 -or $RuntimeProfiles -or $MetadataV12 -or $ConsoleExecutable -or $BaselineExporterPath){throw 'IMU is a focused source fixture; do not combine selectors.'}
    $arguments+='--imu-only'
}
if($Append14){
    if($RuntimeProfiles -or $MetadataV12 -or $ConsoleExecutable -or $BaselineExporterPath){throw 'Append14 is a focused source fixture; do not combine selectors.'}
    $arguments+=@('--append14-only',[IO.Path]::GetFullPath($NativeAppendFixtureDirectory))
}
if($RuntimeProfiles){
    if($MetadataV12 -or $ConsoleExecutable){throw 'Runtime profile fixture is standalone; do not combine selectors.'}
    $arguments+='--runtime-only'
}
if ($ConsoleExecutable) { $arguments += [IO.Path]::GetFullPath($ConsoleExecutable) }
if($MetadataV12){
    $arguments+='--v12-only'
    if($BaselineExporterPath){$arguments += [IO.Path]::GetFullPath($BaselineExporterPath)}
}
& $exe @arguments
if ($LASTEXITCODE) { throw 'SD DAT exporter tests failed.' }
'PASS project-owned NP2 SD-to-DAT fixtures; no hardware was accessed.'
