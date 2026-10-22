param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-recording-settings-tests",[switch]$FlowOnly)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
if(!$FlowOnly){
$exe=Join-Path $OutputDirectory 'NpBleRecordingSettingsTests.exe'
& $compiler /nologo /warnaserror /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll "/out:$exe" "$PSScriptRoot/Tests/NpBleRecordingSettingsTests.cs"
if($LASTEXITCODE){throw 'BLE settings checks did not compile'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'BLE settings checks failed'}
}
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$form=Get-Content -LiteralPath (Join-Path $source 'Ble/NpBleForm.cs') -Raw
$start=$form.IndexOf('        async Task Apply(');$end=$form.IndexOf('        async Task SyncClock(',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual recording handlers missing'}
$fixture=(Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleRecordingFlowTests.cs" -Raw).Replace('ACTUAL_RECORDING_METHODS',$form.Substring($start,$end-$start))
$button=[regex]::Matches($form,'startRecording\.Enabled=(.*);')
if($button.Count -ne 1){throw 'Actual Start button expression missing or ambiguous'}
$fixture=$fixture.Replace('ACTUAL_START_BUTTON_EXPRESSION',$button[0].Groups[1].Value)
$configStart=$form.IndexOf('        Dictionary<byte,NpConfigRequest> V1Configurations(');$configEnd=$form.IndexOf('        HashSet<int> Np2ApplyRequests(',$configStart)
if($configStart -lt 0 -or $configEnd -le $configStart){throw 'Actual V1 configuration scope missing'}
$fixture=$fixture.Replace('ACTUAL_V1_CONFIGURATIONS',$form.Substring($configStart,$configEnd-$configStart))
foreach($hook in @('if(s.Operation==0)ObservePendingStop(s);','(poll.Checked || pendingStop!=null)','void ResetDevice(){ForgetPendingStop();','stopRequested || pendingStop!=null')){
    if(!$form.Contains($hook)){throw "Pending Stop production integration missing: $hook"}
}
$fixturePath=Join-Path $OutputDirectory 'RecordingFlow.cs';[IO.File]::WriteAllText($fixturePath,$fixture)
$flow=Join-Path $OutputDirectory 'NpBleRecordingFlowTests.exe'
& $compiler /nologo /warnaserror /nowarn:0649,0414 /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$flow" $fixturePath "$source/Ble/NpV1BleProtocol.cs" "$source/Ble/NpSdControl.cs" "$source/Ble/NpV2SdControl.cs" "$source/NpLogger/NpSdGeometry.cs"
if($LASTEXITCODE){throw 'BLE recording flow checks did not compile'}
& $flow
if($LASTEXITCODE){throw 'BLE recording flow checks failed'}
