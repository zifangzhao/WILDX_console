param([string]$OutputDirectory="$PSScriptRoot/_artifacts/hardware_rtc_clock_20261029")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOut=[IO.Path]::GetFullPath($OutputDirectory);$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskExe=Join-Path $taskOut 'HardwareRtcClockTests.exe'
# Compile the actual unchanged legacy request/math only, excluding unrelated
# SD catalog/transport dependencies. No copied algorithm or radio code.
$taskOld=[IO.File]::ReadAllText("$taskUi/Ble/NpV2OnlineClock.cs")
$taskAt=$taskOld.IndexOf('  internal static byte[] Request(');$taskEnd=$taskOld.IndexOf('  internal static NpV2ClockReply Parse(',$taskAt)
if($taskAt -lt 0 -or $taskEnd -le $taskAt){throw 'Actual legacy request method missing'}
$taskRequest=$taskOld.Substring($taskAt,$taskEnd-$taskAt)
$taskAt=$taskOld.IndexOf('  internal static long MidpointUtc(');$taskEnd=$taskOld.IndexOf('  internal static async Task<NpV2ClockResult> Synchronize(',$taskAt)
if($taskAt -lt 0 -or $taskEnd -le $taskAt){throw 'Actual legacy midpoint math missing'}
$taskMath=$taskOld.Substring($taskAt,$taskEnd-$taskAt);$taskMagic=[regex]::Match($taskOld,'internal const uint RequestMagic=.*?;').Value
$taskControl=[IO.File]::ReadAllText("$taskUi/Ble/NpSdControl.cs");$taskAt=$taskControl.IndexOf('  internal static uint Crc(');$taskEnd=$taskControl.IndexOf('  static void Put(',$taskAt)
if($taskAt -lt 0 -or $taskEnd -le $taskAt -or !$taskMagic){throw 'Actual legacy CRC/constants missing'}
$taskExtract=Join-Path $taskOut 'LegacyClockMath.cs';[IO.File]::WriteAllText($taskExtract,'using System;using System.IO; namespace CE32_console { internal static class NpSdControl {'+$taskControl.Substring($taskAt,$taskEnd-$taskAt)+'} internal static class NpV2OnlineClock {'+$taskMagic+$taskRequest+$taskMath+'}}')
$taskSources=@("$taskUi/Ble/NpHardwareRtcClock.cs",$taskExtract,"$taskUi/NpFirstSyncWire.cs","$PSScriptRoot/Tests/NpHardwareRtcClockTests.cs")
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /langversion:7.3 /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$taskExe" @taskSources
if($LASTEXITCODE){throw 'Hardware RTC v2 fixture compile failed'}
& $taskExe | Tee-Object -FilePath "$taskOut/result.txt"
if($LASTEXITCODE){throw 'Hardware RTC v2 fixture failed'}
$taskInputs=@($taskSources)+@("$taskUi/Ble/NpV2OnlineClock.cs","$taskUi/Ble/NpSdControl.cs",$PSCommandPath)|Sort-Object -Unique|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
$taskReceipt=[ordered]@{passed=$true;scope='Actual explicit v2 hardware-RTC codec/calibration; no app build/hardware/firmware edit';language='C#7.3';readback_policy='13 readbacks at200ms pending spacing, 2.4s span; covers2s provider deadline+400ms observation margin, early success/cancellation preserved, no COMMIT replay';inputs=$taskInputs}
[IO.File]::WriteAllText("$taskOut/receipt.json",($taskReceipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
