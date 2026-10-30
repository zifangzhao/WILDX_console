param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2_aux_capture_20261029")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskProjectPath=Join-Path $PSScriptRoot 'Neuropixels/SD/NpV2SdDatExport.csproj'
[xml]$taskProject=Get-Content -LiteralPath $taskProjectPath -Raw
$taskProjectFolder=Split-Path -Parent $taskProjectPath
# Compile the actual SDK-linked source boundary directly as a focused fixture.
# This does not run MSBuild, compile the GUI or increment its version.
$taskSources=@($taskProject.Project.ItemGroup.Compile|ForEach-Object {[IO.Path]::GetFullPath((Join-Path $taskProjectFolder $_.Include))})
$taskSources+=@('USB512_interface/USB512_interface/NpV2AuxPacket.cs','USB512_interface/USB512_interface/NpV2AuxCapture.cs','USB512_interface/USB512_interface/NpV2AuxLabSession.cs','Tests/NpV2AuxCaptureTests.cs','Neuropixels/Tests/test_np_v2_usb_console.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
# Exercise the exact shared USB discovery/one-shot SELECT methods without
# pulling unrelated BLE UI dependencies into this transport-only fixture.
$taskControlPath=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface/NpImuGridControl.cs'
$taskControl=[IO.File]::ReadAllText($taskControlPath)
$taskBle=$taskControl.IndexOf('        internal static byte[] BleRequest(',[StringComparison]::Ordinal)
if($taskBle-lt0){throw 'Shared USB control fixture boundary changed'}
[IO.File]::WriteAllText("$taskOutput/ActualGridUsbControl.cs",$taskControl.Substring(0,$taskBle)+'}}',[Text.UTF8Encoding]::new($false))
$taskSources+="$taskOutput/ActualGridUsbControl.cs"
& $taskCompiler /nologo /langversion:7.3 /nowarn:0649,0414 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE /main:NpV2AuxCaptureTests "/out:$taskOutput/Capture.exe" $taskSources
if($LASTEXITCODE){throw 'Actual AUX worker/SDK boundary fixture compile failed'}
& "$taskOutput/Capture.exe" $PSScriptRoot | Tee-Object -FilePath "$taskOutput/capture-result.txt"
if($LASTEXITCODE){throw 'Actual AUX worker/ownership fixture failed'}
$taskReaderPath=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface/USB512WinUsb.cs'
$taskReaderSource=[IO.File]::ReadAllText($taskReaderPath)
$taskStart=$taskReaderSource.IndexOf('    internal sealed class USB512WinUsbQueuedReader : IDisposable',[StringComparison]::Ordinal)
$taskEnd=$taskReaderSource.IndexOf('    internal static class USB512WinUsbNative',$taskStart,[StringComparison]::Ordinal)
if($taskStart-lt0-or$taskEnd-le$taskStart){throw 'Production queued-reader boundary changed'}
$taskActualClass=$taskReaderSource.Substring($taskStart,$taskEnd-$taskStart)
$taskTestSource='using System;using System.IO;using System.ComponentModel;using System.Runtime.InteropServices;using System.Threading;using System.Diagnostics;namespace USB512_interface {'+$taskActualClass+'}'
[IO.File]::WriteAllText("$taskOutput/ActualQueuedReader.cs",$taskTestSource,[Text.UTF8Encoding]::new($false))
$taskNativeTest=Join-Path $PSScriptRoot 'Tests/NpV2AuxReaderNativeTests.cs'
& $taskCompiler /nologo /langversion:7.3 /warnaserror /r:System.Core.dll /main:NpV2AuxReaderNativeTests "/out:$taskOutput/NativeCalls.exe" "$taskOutput/ActualQueuedReader.cs" $taskNativeTest
if($LASTEXITCODE){throw 'Exact production queued-reader/native-call fixture compile failed'}
& "$taskOutput/NativeCalls.exe" | Tee-Object -FilePath "$taskOutput/native-result.txt"
if($LASTEXITCODE){throw 'Exact production queued-reader/native-call fixture failed'}
$taskPins=@($taskSources)+@($taskProjectPath,$taskControlPath,$taskNativeTest,$PSCommandPath)|Sort-Object -Unique|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
$taskReceipt=[ordered]@{passed=$true;scope='Actual AUX worker + real SDK source closure + actual queued-reader method body with native calls stubbed; no WinUSB/device access/app build/HIL';language='C#7.3';checks='worker dispatch/optional failures; blocked native cleanup; positive/incomplete retirement; interface HOLD/reopen guard; EP82-only abort; immutable copy-before-rearm';inputs=$taskPins;queued_reader_extracted_sha256=(Get-FileHash -LiteralPath "$taskOutput/ActualQueuedReader.cs" -Algorithm SHA256).Hash}
[IO.File]::WriteAllText("$taskOutput/receipt.json",($taskReceipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
