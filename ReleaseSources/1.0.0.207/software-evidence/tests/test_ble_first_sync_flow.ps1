param([string]$OutputDirectory="$PSScriptRoot/_artifacts/first_sync_ble_20261028/enabled")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$form=Get-Content -LiteralPath "$source/Ble/NpBleForm.cs" -Raw
$fixture=Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleRecordingFlowTests.cs" -Raw
$start=$form.IndexOf('        async Task Apply(');$end=$form.IndexOf('        async Task SyncClock(',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual BLE recording handlers missing'}
$fixture=$fixture.Replace('ACTUAL_RECORDING_METHODS',$form.Substring($start,$end-$start))
$start=$form.IndexOf('        Dictionary<byte,NpConfigRequest> V1Configurations(');$end=$form.IndexOf('        HashSet<int> Np2ApplyRequests(',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual V1 configuration scope missing'}
$fixture=$fixture.Replace('ACTUAL_V1_CONFIGURATIONS',$form.Substring($start,$end-$start))
$button=[regex]::Matches($form,'startRecording\.Enabled=(.*);');if($button.Count -ne 1){throw 'Actual Start enablement missing'}
$fixture=$fixture.Replace('ACTUAL_START_BUTTON_EXPRESSION',$button[0].Groups[1].Value)
$test=Get-Content -LiteralPath "$source/NpSdLoggerTest.cs" -Raw
$start=$test.IndexOf('        internal static uint Blocks(');$end=$test.IndexOf('        internal static void Verify(',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual bounded SD helper missing'}
$fixture=$fixture.Replace('ACTUAL_SD_TEST_BLOCKS',$test.Substring($start,$end-$start))
foreach($hook in @('stopRequested=true;CancelBleFirstSyncStart();','void ResetDevice(){ResetBleFirstSync();')){if(!$form.Contains($hook)){throw "BLE first-sync integration missing: $hook"}}
if($form -notmatch 'var s=await ReadStatus\(\);if\(V2\)await InitializeClock\(s\);\s+await WarmBleFirstSyncCapabilities\(\);\s+if\(!connection.Connected\)'){throw 'Actual Connect warm-up must follow status/clock initialization and precede the final connection check'}
if($fixture.Contains('WarmBleFirstSyncCapabilities(')){throw 'Optional warm-up leaked into actual recording handlers'}
$fixturePath=Join-Path $OutputDirectory 'FirstSyncRecordingFlow.cs';[IO.File]::WriteAllText($fixturePath,$fixture)
$exe=Join-Path $OutputDirectory 'NpBleFirstSyncFlowTests.exe'
# The complete real NPP codec includes an unrelated cleanup wait. Preserve the
# existing deterministic Stopwatch fixture API; its type-shadow warning is expected.
& $compiler /nologo /warnaserror /nowarn:0649,0414,0436 /define:NP_FIRST_SYNC_ENABLE /main:USB512_interface.NpBleForm /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$exe" $fixturePath "$PSScriptRoot/Tests/NpBleFirstSyncFlowTests.cs" "$source/Ble/NpBleForm.FirstSync.cs" "$source/NpFirstSyncSdClient.cs" "$source/NpFirstSyncSd.cs" "$source/NpFirstSyncWire.cs" "$source/NpProbePreparation.cs" "$source/Ble/NpV1BleProtocol.cs" "$source/Ble/NpSdControl.cs" "$source/Ble/NpV2SdControl.cs" "$source/NpLogger/NpSdGeometry.cs" "$source/NpImuGridCarrier.cs" "$source/NpImuGridWire.cs"
if($LASTEXITCODE){throw 'Enabled BLE first-sync flow did not compile'}
& $exe
if($LASTEXITCODE){throw 'Enabled BLE first-sync flow failed'}
$offExe=Join-Path $OutputDirectory 'FirstSyncWarmOff.exe'
& $compiler /nologo /warnaserror "/out:$offExe" "$PSScriptRoot/Tests/NpBleFirstSyncWarmOffTests.cs" "$source/Ble/NpBleForm.FirstSync.cs"
if($LASTEXITCODE){throw 'Default-off warm-up fixture did not compile'}
& $offExe
if($LASTEXITCODE){throw 'Default-off warm-up fixture failed'}
$connection=Get-Content -LiteralPath "$source/Ble/NpBleConnection.cs" -Raw
$matcher=$connection.IndexOf('internal static bool Matches(')
if($matcher -lt 0){throw 'Actual transport matcher missing'}
$start=$connection.IndexOf('            if(command==0xb8 && magic==NpFirstSyncSd.RequestMagic)',$matcher);$end=$connection.IndexOf('#endif',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual guarded NPSD matcher missing'}
$match=(Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleFirstSyncMatchTests.cs" -Raw).Replace('ACTUAL_NPSD_MATCH',"#if NP_FIRST_SYNC_ENABLE`r`n"+$connection.Substring($start,$end-$start)+"#endif")
$matchPath=Join-Path $OutputDirectory 'FirstSyncMatches.cs';[IO.File]::WriteAllText($matchPath,$match)
foreach($enabled in @($false,$true)){
 $matchExe=Join-Path $OutputDirectory "FirstSyncMatches-$enabled.exe"
 $arguments=@('/nologo','/warnaserror','/nowarn:0649',"/out:$matchExe",$matchPath,"$source/NpFirstSyncSd.cs","$source/NpFirstSyncWire.cs")
 if($enabled){$arguments+='/define:NP_FIRST_SYNC_ENABLE'}
 & $compiler @arguments
 if($LASTEXITCODE){throw 'Actual NPSD matching fragment did not compile'}
 & $matchExe
 if($LASTEXITCODE){throw 'Actual NPSD matching fragment failed'}
}
