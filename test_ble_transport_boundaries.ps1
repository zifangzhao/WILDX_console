param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble_transport_boundaries")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOut=[IO.Path]::GetFullPath($OutputDirectory)
$taskSource=Get-Content -LiteralPath "$PSScriptRoot/USB512_interface/USB512_interface/Ble/NpBleConnection.cs" -Raw
function Extract-Method([string]$marker){
    $start=$taskSource.IndexOf($marker,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing actual method $marker"}
    $open=$taskSource.IndexOf('{',$start);$depth=0
    for($i=$open;$i -lt $taskSource.Length;$i++){if($taskSource[$i] -eq '{'){$depth++};if($taskSource[$i] -eq '}'){$depth--;if(!$depth){return $taskSource.Substring($start,$i-$start+1)}}}
    throw "Unterminated actual method $marker"
}
$taskTest=Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleTransportBoundaryTests.cs" -Raw
$taskTest=$taskTest.Replace('ACTUAL_READ_ONLY',(Extract-Method 'internal static bool IsReadOnlyClockRequest(')).Replace('ACTUAL_POLICY',(Extract-Method 'internal static int WriteChunkBytes(')).Replace('ACTUAL_WRITE',(Extract-Method 'async Task WriteFrame(byte[] frame,int generation,CancellationToken cancel)')).Replace('ACTUAL_RECEIVE',(Extract-Method 'void ValueChanged('))
$taskFixture=Join-Path $taskOut 'ActualBleBoundaryTests.cs';[IO.File]::WriteAllText($taskFixture,$taskTest)
$taskUi="$PSScriptRoot/USB512_interface/USB512_interface"
$taskExe=Join-Path $taskOut 'BleBoundaryTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Web.Extensions.dll "/out:$taskExe" $taskFixture "$taskUi/Ble/NpBleClockEvidence.cs" "$taskUi/Ble/NpBleWire.cs" "$taskUi/Ble/NpV2OnlineClock.cs" "$taskUi/NpHostClock.cs" "$taskUi/NpFirstSyncWire.cs" "$taskUi/NpFirstSyncSd.cs" "$taskUi/NpFineClockWire.cs"
if($LASTEXITCODE){throw 'BLE boundary source fixtures failed to compile'}
& $taskExe (Join-Path $taskOut ([Guid]::NewGuid().ToString('N').Substring(0,12)))
if($LASTEXITCODE){throw 'BLE boundary source fixtures failed'}
