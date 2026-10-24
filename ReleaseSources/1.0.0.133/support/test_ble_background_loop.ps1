param([string]$OutputDirectory="$PSScriptRoot/_artifacts/known_issues_20261023/background-tests",[string]$BaselineForm)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskSource=Get-Content "$PSScriptRoot/USB512_interface/USB512_interface/Ble/NpBleForm.Imu.cs" -Raw
function Slice([string]$text,[string]$start,[string]$end){$a=$text.IndexOf($start);$b=$text.IndexOf($end,$a);if($a-lt0-or$b-le$a){throw 'Production markers changed'};$text.Substring($a,$b-$a)}
$taskLoop=Slice $taskSource '        async Task PollBleBackground()' '        void RefreshBleImuAge('
$taskAge=Slice $taskSource '        void RefreshBleImuAge(' '        bool BleImuShouldYield('
$taskOld=$null
if($BaselineForm){
    $taskBaseline=Get-Content $BaselineForm -Raw
    $taskOld=Slice $taskBaseline '            bleImuTimer.Tick+=async delegate {' '            bleImuTimer.Start();'
    $taskOld=$taskOld.Replace('            bleImuTimer.Tick+=async delegate {','        async Task PollBleBackground() {').TrimEnd()
    if(-not $taskOld.EndsWith('};')){throw 'Baseline callback end changed'}
    $taskOld=$taskOld.Substring(0,$taskOld.Length-1)
}
$taskTemplate=Get-Content "$PSScriptRoot/Tests/NpBleBackgroundLoopTests.cs" -Raw
foreach($taskMode in @('baseline','fixed')){
    if($taskMode -eq 'baseline' -and -not $BaselineForm){continue}
    $taskBody=if($taskMode -eq 'baseline'){$taskOld}else{$taskLoop}
    $taskCode=Join-Path $OutputDirectory "$taskMode.cs"
    [IO.File]::WriteAllText($taskCode,$taskTemplate.Replace('SOURCE_LOOP',$taskBody).Replace('SOURCE_AGE',$taskAge))
    $taskTest=Join-Path $OutputDirectory "$taskMode.exe"
    & 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /nowarn:0649,0414 "/out:$taskTest" $taskCode
    if($LASTEXITCODE){throw 'Background loop check compile failed'}
    & $taskTest $taskMode
    if($LASTEXITCODE){throw 'Background loop check failed'}
}
