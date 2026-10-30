param([int]$ExpectedRevision=200,[string]$Attempt='',
    [string]$CheckedSdk='', [string]$CheckedSdkSha256='', [string]$CheckedSdkReceipt='')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskAppProject=Join-Path $taskRoot 'USB512_interface/USB512_interface/WILDX_console.csproj'
$taskSdkProject=Join-Path $taskRoot 'Neuropixels/SD/NpV2SdDatExport.csproj'
$taskVersion=Join-Path $taskRoot 'USB512_interface/USB512_interface/Properties/AssemblyInfo.cs'
$taskVersionBefore=[IO.File]::ReadAllText($taskVersion)
if($taskVersionBefore -notmatch ('AssemblyVersion\("1\.0\.0\.'+($ExpectedRevision-1)+'"\)')){throw 'Unexpected current compilation version; do not overwrite another build'}
if($Attempt -and $Attempt -notmatch '^-[a-z0-9]+$'){throw 'Attempt suffix must be a short dash-prefixed identifier'}
if($CheckedSdk){
    if($CheckedSdkSha256 -notmatch '^[0-9a-f]{64}$' -or !(Test-Path -LiteralPath $CheckedSdkReceipt -PathType Leaf)){
        throw 'Checked SDK reuse requires its exact SHA256 and recorded qualification receipt'
    }
    if((Get-FileHash -LiteralPath $CheckedSdk -Algorithm SHA256).Hash -ne $CheckedSdkSha256){throw 'Checked SDK bytes changed'}
}elseif($CheckedSdkSha256 -or $CheckedSdkReceipt){throw 'Incomplete checked SDK reuse arguments'}
$taskOut=Join-Path $PSScriptRoot "enabled$ExpectedRevision$Attempt"
$taskSnapshot=Join-Path $PSScriptRoot "source$ExpectedRevision$Attempt"
$taskReceipt=Join-Path $PSScriptRoot "host-build-receipt-$ExpectedRevision$Attempt.json"
if((Test-Path -LiteralPath $taskOut) -or (Test-Path -LiteralPath $taskSnapshot) -or (Test-Path -LiteralPath $taskReceipt)){throw 'Fresh lab build paths required'}
New-Item -ItemType Directory -Path $taskOut,$taskSnapshot | Out-Null
if($CheckedSdk){Copy-Item -LiteralPath $CheckedSdk -Destination (Join-Path $taskOut 'NpV2SdDatExport.dll')}
$taskPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($taskProject in @($taskAppProject,$taskSdkProject)){
    [void]$taskPaths.Add($taskProject)
    [xml]$taskXml=Get-Content -LiteralPath $taskProject -Raw
    foreach($taskNode in $taskXml.SelectNodes("//*[local-name()='Compile']")){
        $taskInput=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($taskProject)) $taskNode.Include))
        if($taskInput -ne $taskVersion){[void]$taskPaths.Add($taskInput)}
    }
}
foreach($taskName in @('np_first_sync_wire.h','np_first_sync_usb.h','np_first_sync_sd.h','np_fine_clock_wire.h','np_sensor_fine_wire.h','np_sensor_fine64_wire.h','np_rtc_start_wire.h','np_imu_grid_wire.h','np_imu_grid_phase.h')){[void]$taskPaths.Add((Join-Path $taskRoot "Neuropixels/Timing/$taskName"))}
[void]$taskPaths.Add($env:CustomAfterMicrosoftCommonTargets)
[void]$taskPaths.Add((Join-Path $taskRoot 'BuildVersion.targets'))
[void]$taskPaths.Add((Join-Path $taskRoot 'bump_version.ps1'))
[void]$taskPaths.Add((Join-Path $taskRoot 'Tools/Correct-RecordingTime.ps1'))
$taskInputs=@()
foreach($taskPath in ($taskPaths | Sort-Object)){
    if(!$taskPath.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected source outside the selected workspace'}
    $taskRelative=$taskPath.Substring($taskRoot.Length+1)
    $taskCopy=Join-Path $taskSnapshot $taskRelative
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($taskCopy)) | Out-Null
    Copy-Item -LiteralPath $taskPath -Destination $taskCopy
    $taskInputs+=@{path=$taskRelative;sha256=(Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash}
}
$taskBuildArgs=@($taskAppProject,'/t:Build','/p:Configuration=Release','/p:GenerateNsisInstaller=false','/p:UseSharedCompilation=false','/p:EnableNpFirstSync=true','/p:EnableNpImuGrid=true',"/p:OutputPath=$taskOut\",'/nologo','/v:minimal')
if($CheckedSdk){$taskBuildArgs+='/p:BuildProjectReferences=false'}
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/MSBuild.exe' @taskBuildArgs *> (Join-Path $taskOut 'build.log')
$taskBuildExit=$LASTEXITCODE
$taskChanged=@($taskInputs | Where-Object {(Get-FileHash -LiteralPath (Join-Path $taskRoot $_.path) -Algorithm SHA256).Hash -ne $_.sha256})
if($taskChanged.Count){throw ('Build inputs changed while compiling: '+(($taskChanged | ForEach-Object {$_.path}) -join ', '))}
if($taskBuildExit){Get-Content -LiteralPath (Join-Path $taskOut 'build.log') -Tail 35;throw 'Lab build failed; keep diagnostics and do not retry without inspection'}
$taskExe=Join-Path $taskOut 'WILDX_console.exe'
$taskSdk=Join-Path $taskOut 'NpV2SdDatExport.dll'
if($CheckedSdk -and (Get-FileHash -LiteralPath $taskSdk -Algorithm SHA256).Hash -ne $CheckedSdkSha256){throw 'App compilation changed checked SDK bytes'}
if([Reflection.AssemblyName]::GetAssemblyName($taskExe).Version -ne [Version]"1.0.0.$ExpectedRevision"){throw 'Build did not produce the expected incremented version'}
$taskVersionRelative='USB512_interface/USB512_interface/Properties/AssemblyInfo.cs'
Copy-Item -LiteralPath $taskVersion -Destination (Join-Path $taskSnapshot $taskVersionRelative)
$taskInputs+=@{path=$taskVersionRelative;sha256=(Get-FileHash -LiteralPath $taskVersion -Algorithm SHA256).Hash}
@{schema=1;utc=[DateTime]::UtcNow.ToString('O');scope='Enabled development app/SDK and exact source snapshot; matched firmware and ordinary recording acceptance remain open; no installer/publication';build_exit=0;first_sync_enabled=$true;preparation_enabled=$false;source_stable_during_build=$true;source_snapshot="source$ExpectedRevision$Attempt";source_inputs=$taskInputs;exe_sha256=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash;sdk_sha256=(Get-FileHash -LiteralPath $taskSdk -Algorithm SHA256).Hash;sdk_reused=[bool]$CheckedSdk;sdk_qualification_receipt=$CheckedSdkReceipt;sdk_qualification_receipt_sha256=$(if($CheckedSdk){(Get-FileHash -LiteralPath $CheckedSdkReceipt -Algorithm SHA256).Hash}else{$null});build_script_sha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;published_release_unchanged='1.0.0.207';all_stream_alignment_verified=$false} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $taskReceipt
Write-Output "Enabled lab build 1.0.0.$ExpectedRevision complete; source inputs stable: $($taskInputs.Count)"
Get-FileHash -LiteralPath $taskExe,$taskSdk,$taskReceipt -Algorithm SHA256 | Select-Object Path,Hash
