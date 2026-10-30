#Requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][string]$Folder,
    [string]$ConsoleExecutable,
    [ValidateRange(0,10000000)][double]$LatencyMicroseconds=0,
    [switch]$Recurse,
    [switch]$ReplaceDerived,
    [switch]$DeviceRtcOnly
)
$ErrorActionPreference='Stop'
if(-not $ConsoleExecutable){$ConsoleExecutable=Join-Path ([IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)) 'WILDX_console.exe'}
if($PSVersionTable.PSEdition -eq 'Core') {
    $childArgs=@('-NoProfile','-File',$PSCommandPath,'-Folder',$Folder,'-ConsoleExecutable',$ConsoleExecutable,'-LatencyMicroseconds',$LatencyMicroseconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    if($Recurse){$childArgs+='-Recurse'}
    if($ReplaceDerived){$childArgs+='-ReplaceDerived'}
    if($DeviceRtcOnly){$childArgs+='-DeviceRtcOnly'}
    & powershell.exe @childArgs
    if($LASTEXITCODE){throw 'Timestamp correction failed'}
    return
}
# Load only the file-processing engine. The GUI entry point and USB APIs do not run.
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ConsoleExecutable))
if($DeviceRtcOnly){
    # Refresh existing source-bound device RTC evidence only. This never calls
    # the PC/IMU/GPIO correction engines or reconstructs missing source context.
    $rtcType=$assembly.GetType('USB512_interface.NpRecordingUtc',$false)
    $rtcMethod=if($rtcType){$rtcType.GetMethod('RefreshFromFirstSampleReport',[Reflection.BindingFlags]'Static,NonPublic')}else{$null}
    $selected=[IO.Path]::GetFullPath($Folder.Trim().Trim('"'))
    if(-not [IO.Directory]::Exists($selected)){throw 'Select an existing recording folder'}
    $rtcCapability=if($rtcType){$rtcType.GetProperty('DeviceRtcRefreshSupported',[Reflection.BindingFlags]'Static,NonPublic')}else{$null}
    if($null -eq $rtcMethod -or $null -eq $rtcCapability -or $rtcCapability.GetValue($null,$null) -ne $true){
        [pscustomobject]@{Status='unavailable';LocalWidthUs=$null;Reason='This WILDX build does not support strict device RTC refresh; existing files were preserved.';Folder=$selected}
        return
    }
    $folders=New-Object 'System.Collections.Generic.List[string]'
    $pending=New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($selected)
    while($pending.Count -gt 0){
        $path=$pending.Pop()
        if(([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue}
        if([IO.File]::Exists((Join-Path $path 'recording_start_utc.json')) -or [IO.File]::Exists((Join-Path $path 'first_sample_timestamps.json'))){$folders.Add($path)}
        if($Recurse){foreach($child in [IO.Directory]::GetDirectories($path)){$pending.Push($child)}}
    }
    if($folders.Count -eq 0 -and ([IO.File]::GetAttributes($selected) -band [IO.FileAttributes]::ReparsePoint) -eq 0){$folders.Add($selected)}
    foreach($path in $folders){
        $canonical=Join-Path $path 'recording_start_utc.json'
        $sample=Join-Path $path 'first_sample_timestamps.json'
        $status='unavailable';$width=$null;$reason='Existing source-bound device RTC canonical and first-sample reports are required.'
        try{
            if([IO.File]::Exists($canonical) -and [IO.File]::Exists($sample)){
                $linked=$false
                foreach($file in [IO.Directory]::GetFiles($path)){if(([IO.File]::GetAttributes($file) -band [IO.FileAttributes]::ReparsePoint) -ne 0){$linked=$true;break}}
                if($linked){throw 'Linked recording files are skipped; device RTC reports were preserved.'}
                # A selector, not timing proof: the actual engine revalidates
                # every retained source/RTC seal and binding before qualifying.
                $before=[IO.File]::ReadAllText($canonical)|ConvertFrom-Json
                $declared=$before.clock_reference -eq 'device_hardware_rtc' -and
                    (($before.sd_device_rtc_mapping_selected -eq $true -and ($before.sd_bound_first_sync -eq $true -or $before.sd_first_sync_refresh_required -eq $true)) -or
                     ($before.usb_device_rtc_mapping_selected -eq $true -and $before.usb_first_sync_refresh_required -eq $true))
                if($declared){
                    $layout=[IO.File]::ReadAllText($sample)|ConvertFrom-Json
                    if($null -eq $layout.PSObject.Properties['rows'] -or $null -eq $layout.rows -or $layout.rows -isnot [Array]){
                        throw 'First-sample rows must be a non-null JSON array; existing reports were preserved.'
                    }
                    foreach($row in $layout.rows){if($null -eq $row -or ($row -isnot [pscustomobject] -and $row -isnot [Collections.IDictionary])){
                        throw 'First-sample rows must contain objects; existing reports were preserved.'
                    }}
                    $rtcMethod.Invoke($null,[object[]]@($path))|Out-Null
                    $result=[IO.File]::ReadAllText($canonical)|ConvertFrom-Json
                    if($result.clock_reference -eq 'device_hardware_rtc' -and $result.utc_valid -eq $true -and $result.rtc_local_mapping_qualified -eq $true){
                        $status='qualified';$width=$result.local_interval_width_us;$reason=$result.rtc_map_reason
                    }else{
                        $reason=$result.rtc_evidence_error
                        if(-not $reason){$reason=$result.unavailable_reason}
                        if(-not $reason){$reason='Device RTC evidence did not qualify; no PC estimate was substituted.'}
                    }
                }
            }
        }catch{
            $errorDetail=$_.Exception;if($errorDetail.InnerException){$errorDetail=$errorDetail.InnerException};$reason=$errorDetail.Message
        }
        [pscustomobject]@{Status=$status;LocalWidthUs=$width;Reason=$reason;Folder=$path}
    }
    return
}
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
    if([IO.File]::Exists((Join-Path $path 'time_observations.jsonl')) -or [IO.File]::Exists((Join-Path $path 'first_sample_timestamps.json')) -or [IO.File]::Exists((Join-Path $path 'gpio_rtc_sync.csv')) -or [IO.File]::Exists((Join-Path $path 'gpio_rtc_link.json')) -or [IO.File]::Exists((Join-Path $path 'imu_native_samples.jsonl')) -or [IO.File]::Exists((Join-Path $path 'imu_snapshots.bin'))){$folders.Add($path)}
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
        if($gpioMethod -and ([IO.File]::Exists((Join-Path $path 'gpio_rtc_sync.csv')) -or [IO.File]::Exists((Join-Path $path 'gpio_rtc_link.json')))){
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
