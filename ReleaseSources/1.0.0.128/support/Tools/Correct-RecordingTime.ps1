#Requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][string]$Folder,
    [string]$ConsoleExecutable,
    [ValidateRange(0,10000000)][double]$LatencyMicroseconds=0,
    [switch]$Recurse,
    [switch]$ReplaceDerived
)
$ErrorActionPreference='Stop'
if(-not $ConsoleExecutable){$ConsoleExecutable=Join-Path ([IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)) 'WILDX_console.exe'}
if($PSVersionTable.PSEdition -eq 'Core') {
    $childArgs=@('-NoProfile','-File',$PSCommandPath,'-Folder',$Folder,'-ConsoleExecutable',$ConsoleExecutable,'-LatencyMicroseconds',$LatencyMicroseconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    if($Recurse){$childArgs+='-Recurse'}
    if($ReplaceDerived){$childArgs+='-ReplaceDerived'}
    & powershell.exe @childArgs
    if($LASTEXITCODE){throw 'Timestamp correction failed'}
    return
}
# Load only the file-processing engine. The GUI entry point and USB APIs do not run.
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ConsoleExecutable))
$type=$assembly.GetType('USB512_interface.NpTimeCorrection',$true)
$method=$type.GetMethod('Correct',[Reflection.BindingFlags]'Static,NonPublic')
$gpioType=$assembly.GetType('USB512_interface.NpGpioTimeCorrection',$false)
$gpioMethod=if($gpioType){$gpioType.GetMethod('Correct',[Reflection.BindingFlags]'Static,NonPublic')}else{$null}
$imuType=$assembly.GetType('USB512_interface.NpImuTimeCorrection',$false)
$imuMethod=if($imuType){$imuType.GetMethod('Correct',[Reflection.BindingFlags]'Static,NonPublic')}else{$null}
if($null -eq $method){throw 'This WILDX build does not include timestamp correction'}
$selected=[IO.Path]::GetFullPath($Folder.Trim().Trim('"'))
if(-not [IO.Directory]::Exists($selected)){throw 'Select an existing recording folder'}
$folders=New-Object 'System.Collections.Generic.List[string]'
$pending=New-Object 'System.Collections.Generic.Stack[string]'
$pending.Push($selected)
while($pending.Count -gt 0){
    $path=$pending.Pop()
    if(([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue}
    if([IO.File]::Exists((Join-Path $path 'time_observations.jsonl')) -or [IO.File]::Exists((Join-Path $path 'first_sample_timestamps.json')) -or [IO.File]::Exists((Join-Path $path 'gpio_rtc_sync.csv')) -or [IO.File]::Exists((Join-Path $path 'imu_native_samples.jsonl')) -or [IO.File]::Exists((Join-Path $path 'imu_snapshots.bin'))){$folders.Add($path)}
    if($Recurse){foreach($child in [IO.Directory]::GetDirectories($path)){$pending.Push($child)}}
}
if($folders.Count -eq 0){$folders.Add($selected)}
$failures=0
foreach($path in $folders){
    try{
        if($imuMethod -and ([IO.File]::Exists((Join-Path $path 'imu_native_samples.jsonl')) -or [IO.File]::Exists((Join-Path $path 'imu_snapshots.bin')))){
            $imuResult=$imuMethod.Invoke($null,[object[]]@($path,[bool]$ReplaceDerived)) | ConvertFrom-Json
            [pscustomobject]@{Folder=$path;Signal='IMU';Status=$imuResult.status;Report=(Join-Path $path 'imu_timestamp_calibration.json');Reason=$imuResult.reason}
        }
        if($gpioMethod -and [IO.File]::Exists((Join-Path $path 'gpio_rtc_sync.csv'))){
            $gpioResult=$gpioMethod.Invoke($null,[object[]]@($path,[bool]$ReplaceDerived)) | ConvertFrom-Json
            [pscustomobject]@{Folder=$path;Signal='GPIO';Status=$gpioResult.status;Report=(Join-Path $path 'gpio_timestamp_calibration.json');Reason=$gpioResult.reason}
        }
        if(-not [IO.File]::Exists((Join-Path $path 'time_observations.jsonl')) -and -not [IO.File]::Exists((Join-Path $path 'first_sample_timestamps.json'))){continue}
        $json=$method.Invoke($null,[object[]]@($path,[double]$LatencyMicroseconds,[bool]$ReplaceDerived))
        $result=$json | ConvertFrom-Json
        [pscustomobject]@{Folder=$path;Status=$result.status;Report=(Join-Path $path 'timestamp_calibration.json');Reason=$result.reason}
    }catch{
        $failures++
        $reason=$_.Exception
        if($reason.InnerException){$reason=$reason.InnerException}
        Write-Warning "$path : $($reason.Message)"
    }
}
if($failures){throw "$failures recording folder(s) could not be corrected; existing sample data and original timestamps were preserved"}
