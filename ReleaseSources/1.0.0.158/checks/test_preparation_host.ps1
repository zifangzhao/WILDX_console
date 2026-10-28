param([string]$OutputDirectory="$PSScriptRoot/_artifacts/preparation_20261028/host")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$fixture=Get-Content -LiteralPath "$PSScriptRoot/Tests/NpPreparationHostTests.cs" -Raw
$v1=Get-Content -LiteralPath "$source/XeNpWinUsb.cs" -Raw
$start=$v1.IndexOf('        internal NpModeCapabilities SelectCaptureMode(');$end=$v1.IndexOf('        internal XeNpStatus Start(',$start)
if($start -lt 0 -or $end -le $start){throw 'V1 production selection method missing'}
$fixture=$fixture.Replace('ACTUAL_V1_SELECT',$v1.Substring($start,$end-$start))
$v2=Get-Content -LiteralPath "$PSScriptRoot/Neuropixels/USBConsoleV2/NpV2UsbConsole.cs" -Raw
$start=$v2.IndexOf('        internal void Start(uint blocks)');$end=$v2.IndexOf('        internal void ArmCapture()',$start)
if($start -lt 0 -or $end -le $start){throw 'V2 production Start method missing'}
$fixture=$fixture.Replace('ACTUAL_V2_START',$v2.Substring($start,$end-$start))
$dialog=Get-Content -LiteralPath "$source/NpModeDialog.cs" -Raw
$start=$dialog.IndexOf('  private async Task NpShowModeSettings()');$end=$dialog.LastIndexOf("`n }")
if($start -lt 0 -or $end -le $start){throw 'USB settings lease method missing'}
$fixture=$fixture.Replace('ACTUAL_SETTINGS_DIALOG',$dialog.Substring($start,$end-$start))
foreach($guard in @(@('mainForm.SystemInfo.cs','!npSettingsApplying'),@('mainForm.SystemInfo.cs','if (npSettingsApplying || firmwareManagementActive'),@('mainForm.cs','if (running || npSettingsApplying || systemInfoActive!=0 || Interlocked.CompareExchange'),@('mainForm.cs','private async void btnStop_Click'),@('Ble/NpBleForm.cs','await RetireBleProbePreparationForSettings();'),@('Ble/NpBleForm.cs','if(UnknownBlePreparation && probePreparationAttempt.StopIssued)'))){
 if(!(Get-Content -LiteralPath (Join-Path $source $guard[0]) -Raw).Contains($guard[1])){throw "Preparation integration missing: $($guard[0]) / $($guard[1])"}
}
$fixturePath=Join-Path $OutputDirectory 'PreparationHost.cs';[IO.File]::WriteAllText($fixturePath,$fixture)
$csc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'PreparationHostTests.exe'
& $csc /nologo /warnaserror /define:NP_PREPARATION_ENABLE /nowarn:0649,0414 /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$exe" $fixturePath "$source/NpProbePreparation.cs" "$source/mainForm.Preparation.cs" "$source/Ble/NpBleForm.Preparation.cs"
if($LASTEXITCODE){throw 'Preparation host checks did not compile'}
& $exe
if($LASTEXITCODE){throw 'Preparation host checks failed'}
$coldExe=Join-Path $OutputDirectory 'PreparationColdCompatibilityTests.exe'
& $csc /nologo /warnaserror /nowarn:0649,0414 /r:System.Core.dll /r:System.Windows.Forms.dll "/out:$coldExe" $fixturePath "$source/NpProbePreparation.cs" "$source/mainForm.Preparation.cs" "$source/Ble/NpBleForm.Preparation.cs"
if($LASTEXITCODE){throw 'Default cold compatibility checks did not compile'}
& $coldExe
if($LASTEXITCODE){throw 'Default cold compatibility checks failed'}
