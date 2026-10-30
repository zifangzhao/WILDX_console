param([string]$OutputDirectory="$PSScriptRoot/_artifacts/usb_optional_isolation_20261029")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
function ActualSource([string]$source,[string]$marker) {
    $taskStart=$source.IndexOf($marker,[StringComparison]::Ordinal)
    if($taskStart -lt 0){throw "Actual source missing: $marker"}
    $taskOpen=$source.IndexOf('{',$taskStart);$taskDepth=0
    for($taskAt=$taskOpen;$taskAt -lt $source.Length;$taskAt++){
        if($source[$taskAt] -eq '{'){$taskDepth++}
        if($source[$taskAt] -eq '}'){$taskDepth--;if($taskDepth -eq 0){return $source.Substring($taskStart,$taskAt-$taskStart+1)}}
    }
    throw "Unterminated actual source: $marker"
}
$taskV2=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Neuropixels/USBConsoleV2/NpV2UsbConsole.cs'))
$taskFixture=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Neuropixels/Tests/test_np_v2_usb_console.cs'))
$taskGenerated="using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Globalization;using USB512_interface;namespace USB512_interface {`n"
foreach($taskName in @('NpV2ServiceStatus','NpV2CaptureStatus','NpV2SampleDecoder','NpV2WireVerifier')){$taskGenerated+=(ActualSource $taskV2 ("internal sealed class "+$taskName))+"`n"}
$taskGenerated+=(ActualSource ([IO.File]::ReadAllText("$taskUi/XeNpWinUsb.cs")) 'internal sealed class XeNpPacketParser')+"`n"
$taskGenerated+=(ActualSource ([IO.File]::ReadAllText("$taskUi/NpCaptureModes.cs")) 'internal sealed class NpCameraRecording')+"`n"
# Native endpoint boundary is unavailable by construction. Real parsers,
# verifiers, decoders, sensor consumers and file writers are compiled unchanged.
$taskGenerated+='internal sealed class NpV2UsbDevice {internal const int BlockBytes=46592;internal byte[] GetSensorSnapshot(){throw new InvalidOperationException("Hardware forbidden in source-only fixture.");}}'
$taskGenerated+="`ninternal static class NpOptionalV2Fixture {const int Header=512,Payload=46080,Block=46592;static readonly byte[] Sync={0xD3,0x6A,0x59,0xC5,0x3E,0xA7,0x1D,0xE4,0xB2,0x4F,0x78,0x96};internal static byte[] Create(ulong seq,ulong tick){return MakeV3Block(seq,tick,3,3,20000,true,uint.MaxValue);}`n"
foreach($taskMethod in @('static void Put32(','static void Put64(','static byte Expected(','static int PopCount3(','static uint WireMask(','static int PhaseFor(','static void PutBits(','static byte[] MakeV3Block(')){$taskGenerated+=(ActualSource $taskFixture $taskMethod)+"`n"}
$taskGenerated+="}}`n"
$taskGenerated+="namespace CE32_console {internal static class NpBleConnection {"+(ActualSource ([IO.File]::ReadAllText("$taskUi/Ble/NpBleConnection.cs")) 'internal static bool IsReadOnlyClockRequest(')+"}internal static class NpSdControl {"+(ActualSource ([IO.File]::ReadAllText("$taskUi/Ble/NpSdControl.cs")) 'internal static uint Crc(')+"}internal static class NpSdGeometry {"+(ActualSource ([IO.File]::ReadAllText("$taskUi/NpLogger/NpSdGeometry.cs")) 'internal static uint U(')+"}}`n"
$taskActual=Join-Path $taskOutput 'ActualParsers.cs'
[IO.File]::WriteAllText($taskActual,$taskGenerated)
$taskSources=@(
    "$taskUi/NpImuGridWire.cs","$taskUi/NpImuGridPhase.cs","$taskUi/NpImuGridWriter.cs","$taskUi/NpUsbFineSensors.cs",
    "$taskUi/NpFineClockWire.cs","$taskUi/NpFineSdArchive.cs", "$taskUi/NpRtcStartWire.cs", "$taskUi/NpRtcRecordingUtc.cs", "$taskUi/NpRtcSdArchive.cs", "$taskUi/NpRtcUsbArchive.cs","$taskUi/NpFineClockFit.cs","$taskUi/NpFineClockRecording.cs","$taskUi/NpFineClockEvidenceImport.cs","$taskUi/NpSensorFineWire.cs","$taskUi/NpSensorFine64Wire.cs","$taskUi/NpSensorFineTime.cs","$taskUi/NpSdFineAnalog.cs",
    "$PSScriptRoot/Neuropixels/SD/NpMetadataFile.cs","$PSScriptRoot/Neuropixels/SD/NpSdImuExport.cs",
    "$taskUi/NpUsbSensors.cs","$taskUi/NpUsbSensorMonitor.cs","$taskUi/XeNpStreamDecoder.cs","$taskUi/NpImuTimeCorrection.cs","$taskUi/NpHostClock.cs","$taskUi/NpAcquisitionAnchor.cs","$taskUi/NpRecordingUtc.cs",
    "$taskUi/NpFirstSyncWire.cs","$taskUi/NpFirstSyncUsb.cs","$taskUi/NpFirstSyncUsbEvidence.cs","$taskUi/NpFirstSyncUsbUtc.cs","$taskUi/NpFirstSyncSd.cs","$taskUi/NpFirstSyncSdUtc.cs", "$taskUi/NpDerivedImuGridUtc.cs","$taskUi/NpFirstSyncUtc.cs",
    "$taskUi/NpUsbTimingWire.cs","$taskUi/NpUsbTimingEvidence.cs","$taskUi/NpClockFit.cs","$taskUi/NpClockJournal.cs","$taskUi/Ble/NpBleClockEvidence.cs","$taskUi/Ble/NpV2OnlineClock.cs",
    "$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopEvent.cs","$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopControl.cs","$PSScriptRoot/Neuropixels/ClosedLoop/NpClosedLoopCarrier.cs",
    $taskActual,"$PSScriptRoot/Tests/NpUsbOptionalIsolationTests.cs"
)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo /langversion:7.3 /optimize+ /nowarn:0649,0414 /define:NP_FIRST_SYNC_ENABLE /r:System.Web.Extensions.dll "/out:$taskOutput/UsbOptionalIsolation.exe" @taskSources
if($LASTEXITCODE){throw 'Optional USB IMU isolation checks did not compile'}
& "$taskOutput/UsbOptionalIsolation.exe" $taskOutput
if($LASTEXITCODE){throw 'Optional USB IMU isolation checks failed'}
