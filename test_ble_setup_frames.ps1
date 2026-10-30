param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble_setup_frames_20261029")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskConnection=Get-Content -LiteralPath "$taskSource/Ble/NpBleConnection.cs" -Raw
$taskStart=$taskConnection.IndexOf('        internal static bool IsReadOnlySetupRequest(')
$taskEnd=$taskConnection.IndexOf('        internal static bool ReadOnlyClockObservation(',$taskStart)
if($taskStart -lt 0 -or $taskEnd -le $taskStart){throw 'Actual setup admission missing'}
$taskWrite=[regex]::Match($taskConnection,'await WriteFrame\(frame,generation,opportunistic\|\|IsReadOnlySetupRequest\(command,request\)\?CancellationToken.None:cancel\);\s*cancel.ThrowIfCancellationRequested\(\);')
if(!$taskWrite.Success){throw 'Actual finish-frame then cancellation barrier missing'}
$taskFixture=(Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleSetupFrameTests.cs" -Raw).Replace('ACTUAL_READ_ONLY_SETUP',$taskConnection.Substring($taskStart,$taskEnd-$taskStart)).Replace('ACTUAL_WRITE_AND_CANCEL',$taskWrite.Value)
$taskCode=Join-Path $OutputDirectory 'SetupFrames.cs'
[IO.File]::WriteAllText($taskCode,$taskFixture)
$taskExe=Join-Path $OutputDirectory 'SetupFrames.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$taskExe" $taskCode "$taskSource/NpFirstSyncSd.cs" "$taskSource/NpFirstSyncWire.cs" "$taskSource/NpProbePreparation.cs"
if($LASTEXITCODE){throw 'Setup frame fixture did not compile'}
& $taskExe
if($LASTEXITCODE){throw 'Setup frame fixture failed'}
