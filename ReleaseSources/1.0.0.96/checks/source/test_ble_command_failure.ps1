param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-command-failure-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskForm=Get-Content -LiteralPath (Join-Path $taskSource 'Ble/NpBleForm.cs') -Raw
$taskConnection=Get-Content -LiteralPath (Join-Path $taskSource 'Ble/NpBleConnection.cs') -Raw
$taskFixture=Get-Content -LiteralPath "$PSScriptRoot/Tests/NpBleCommandFailureTests.cs" -Raw
foreach($taskPart in @(
    @('ACTUAL_RUN',$taskForm,'        async Task Run(','        void OfferFirmwareReview('),
    @('ACTUAL_SEND',$taskForm,'        async Task<NpSdStatus> Send(','        async Task<NpSdStatus> ReadStatus('),
    @('ACTUAL_PARSE_STATUS',$taskConnection,'        internal static NpSdStatus ParseStatus(','        internal static async Task<Tuple<int,NpSdStatus>> ReadDevice(')
)){
    $taskStart=$taskPart[1].IndexOf($taskPart[2]);$taskEnd=$taskPart[1].IndexOf($taskPart[3],$taskStart)
    if($taskStart -lt 0 -or $taskEnd -le $taskStart){throw "Production method missing: $($taskPart[0])"}
    $taskFixture=$taskFixture.Replace($taskPart[0],$taskPart[1].Substring($taskStart,$taskEnd-$taskStart))
}
$taskCode=Join-Path $OutputDirectory 'CommandFailure.cs'
[IO.File]::WriteAllText($taskCode,$taskFixture)
$taskExecutable=Join-Path $OutputDirectory 'NpBleCommandFailureTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649,0414 /r:System.Core.dll "/out:$taskExecutable" $taskCode "$taskSource/Ble/NpSdControl.cs" "$taskSource/Ble/NpV2SdControl.cs" "$taskSource/NpLogger/NpSdGeometry.cs"
if($LASTEXITCODE){throw 'BLE command failure checks did not compile'}
& $taskExecutable ([IO.Path]::GetFullPath($ConsoleExecutable)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'BLE command failure checks failed'}
