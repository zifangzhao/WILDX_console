param([string]$OutputDirectory="$PSScriptRoot/_artifacts/imu_grid_integration_20261029",[string]$SavedRun)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskNp=Join-Path $PSScriptRoot 'Neuropixels'
$taskSources=@(
    "$taskNp/ClosedLoop/NpClosedLoopEvent.cs","$taskNp/ClosedLoop/NpClosedLoopControl.cs","$taskNp/ClosedLoop/NpClosedLoopCarrier.cs","$taskNp/ClosedLoop/NpClosedLoopLog.cs",
    "$taskUi/NpAcquisitionAnchor.cs","$taskUi/NpRecordingUtc.cs","$taskUi/NpHostClock.cs","$taskUi/NpTimeCorrection.cs","$taskUi/NpProgressEstimate.cs",
    "$taskUi/NpFirstSyncWire.cs","$taskUi/NpFirstSyncSd.cs","$taskUi/NpFirstSyncUtc.cs","$taskUi/NpFirstSyncSdUtc.cs", "$taskUi/NpDerivedImuGridUtc.cs",
    "$taskUi/NpFirstSyncUsb.cs","$taskUi/NpFirstSyncUsbEvidence.cs","$taskUi/NpFirstSyncUsbUtc.cs",
    "$taskUi/NpFineClockWire.cs","$taskUi/NpFineSdArchive.cs", "$taskUi/NpRtcStartWire.cs", "$taskUi/NpRtcRecordingUtc.cs", "$taskUi/NpRtcSdArchive.cs", "$taskUi/NpRtcUsbArchive.cs","$taskUi/NpUsbTimingWire.cs","$taskUi/NpUsbTimingEvidence.cs",
    "$taskUi/NpFineClockFit.cs","$taskUi/NpFineClockRecording.cs","$taskUi/NpFineClockEvidenceImport.cs","$taskUi/NpSensorFineWire.cs","$taskUi/NpSensorFine64Wire.cs","$taskUi/NpSensorFineTime.cs","$taskUi/NpSdFineAnalog.cs",
    "$taskNp/SD/NpV2SdDatExport.cs","$taskNp/SD/NpSdImuExport.cs","$taskNp/SD/NpNeuroscopeXml.cs","$taskNp/SD/NpMetadataFile.cs",
    "$taskNp/USBConsoleV2/NpV2UsbConsole.cs","$taskUi/NpV2ReadHistory.cs","$taskUi/Np2ConfigurationClient.cs","$taskUi/NpAdcCalibration.cs",
    "$taskUi/USB512WinUsb.cs","$taskUi/UsbHealth.cs","$taskUi/UsbHealthNative.cs","$taskUi/NeuropixelsPreviewForm.cs","$taskUi/XeNpStreamDecoder.cs",
    "$taskUi/NpLogger/NpCe64Export.cs","$taskUi/NpLogger/NpCe64Decoder.cs","$taskUi/NpLogger/NpSdGeometry.cs",
    "$taskUi/NpRecordingSettings.cs","$taskUi/NpProbeIdentity.cs","$taskUi/NpProbeManifest.cs","$taskNp/V1Capture/Configuration/NpConfigClient.cs",
    "$taskUi/NpLogger/NpDownloadCatalog.cs","$taskUi/NpLogger/NpV1StoreV2.cs","$taskUi/NpLogger/NpDownloadStream.cs","$taskUi/NpLogger/NpDownloadNative.cs",
    "$taskUi/NpLogger/RawBurst.cs","$taskUi/NpLogger/NpDownloadBurst.cs","$taskUi/NpLogger/NpDownloadStream.Progress.cs",
    "$taskUi/Ble/NpMediaExport.cs","$taskUi/Ble/WILD_VideoDecoder.cs","$taskUi/WildXTheme.cs",
    "$PSScriptRoot/Tests/NpV1PowerCutRecoveryTests.cs","$PSScriptRoot/Tests/NpV1SdOptionalSensorTests.cs"
)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskSources+=@('NpImuGridWire','NpImuGridPhase','NpImuGridCarrier','NpImuGridWriter','NpImuGridControl','NpImuGridUsb')|ForEach-Object {Join-Path $taskUi ($_+'.cs')}
$taskSources+=@("$taskUi/Ble/NpSdControl.cs","$taskUi/Ble/NpV1BleProtocol.cs","$PSScriptRoot/Tests/NpFirstSyncWireTests.cs","$PSScriptRoot/Tests/NpImuGridIntegrationTests.cs")
$taskExecutable=Join-Path $taskOutput 'ImuGridIntegration.exe'
& $taskCompiler /nologo /optimize+ /nowarn:0649,0414 /target:exe /main:NpImuGridIntegrationTests /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll /define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE,NP_IMU_GRID_ENABLE "/out:$taskExecutable" @taskSources
if($LASTEXITCODE){throw 'IMU grid actual caller fixture compile failed'}
$taskArguments=@($taskOutput,$PSScriptRoot);if($SavedRun){$taskArguments+=$SavedRun}
& $taskExecutable @taskArguments
if($LASTEXITCODE){throw 'IMU grid actual caller fixture failed'}
