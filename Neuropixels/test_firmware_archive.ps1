param(
    [Parameter(Mandatory=$true)][string]$FixtureDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$MigrationScript="$PSScriptRoot\..\USB512_interface\Installer\ArchiveKnownV1Firmware.ps1",
    [string]$CatalogPath="$PSScriptRoot\..\USB512_interface\Installer\known-v1-firmware.json"
)
$ErrorActionPreference='Stop'
$checks=0
function Check($value,[string]$message){$script:checks++;if(!$value){throw $message}}
$catalog=Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$fixtures=@(Get-ChildItem -LiteralPath $FixtureDirectory -File)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function New-Fixture([string]$name){
    $root=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $name
    if(Test-Path -LiteralPath $root){throw 'Test fixture collision'}
    New-Item -ItemType Directory -Path (Join-Path $root 'firmware') | Out-Null
    foreach($file in $fixtures){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $root ('firmware\'+$file.Name))}
    return $root
}
function Run-Migration([string]$root,[bool]$success=$true,[string]$catalogFile=$CatalogPath){
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
    $start.Arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+[IO.Path]::GetFullPath($MigrationScript)+'" -InstallDirectory "'+$root+'" -CatalogPath "'+[IO.Path]::GetFullPath($catalogFile)+'"'
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$start
    try{
        Check ($process.Start()) 'Migration process did not start'
        $output=$process.StandardOutput.ReadToEnd();$errorText=$process.StandardError.ReadToEnd();$process.WaitForExit()
        Check (($process.ExitCode -eq 0) -eq $success) ('Unexpected migration exit: '+$output+$errorText)
        if($success){return ($output | ConvertFrom-Json)}
    }finally{$process.Dispose()}
}
function Check-Frozen([string]$path,$entry){Check ((Get-Item -LiteralPath $path).Length -eq $entry.bytes -and (Get-FileHash -LiteralPath $path).Hash -eq $entry.sha256) ('Changed archived or preserved bytes: '+$entry.name)}
function Check-AllActive([string]$root){foreach($file in $fixtures){Check ((Get-FileHash -LiteralPath (Join-Path $root ('firmware\'+$file.Name))).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) ('Preflight failure moved/changed '+$file.Name)}}

$root=New-Fixture 'exact upgrade & $(literal)'
$result=Run-Migration $root
Check ($result.archived_files -eq $catalog.files.Count) 'Exact old installed fixture was not archived by ownership'
foreach($entry in $catalog.files){Check (!(Test-Path -LiteralPath (Join-Path $root ('firmware\'+$entry.name)))) ('Known file stayed active: '+$entry.name);Check-Frozen (Join-Path $result.archive_directory $entry.name) $entry}
foreach($file in $fixtures | Where-Object{$_.Name -notin $catalog.files.name}){Check ((Get-FileHash -LiteralPath (Join-Path $root ('firmware\'+$file.Name))).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) 'Unproven ownership file was moved'}
Check ((Get-Content -LiteralPath (Join-Path $result.archive_directory 'migration.json') -Raw | ConvertFrom-Json).files.Count -eq $catalog.files.Count) 'Recoverable archive receipt missing'
$again=Run-Migration $root
Check ($again.archived_files -eq 0 -and $null -eq $again.archive_directory) 'Rerun was not idempotent'
Check ((Get-ChildItem -LiteralPath (Join-Path $root 'legacy\firmware') -Directory).Count -eq 1) 'Rerun added an empty archive'

$root=New-Fixture 'unknown and other device bundles'
$unknown=@('firmware\user-supplied.hex','firmware\WILD_SD_BLE.hex','firmware\WILD\manifest.json','other-device\firmware\README.md')
foreach($relative in $unknown){$path=Join-Path $root $relative;New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null;[IO.File]::WriteAllText($path,'user bytes '+$relative)}
$hashes=@{};foreach($relative in $unknown){$hashes[$relative]=(Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash}
$result=Run-Migration $root
Check ($result.archived_files -eq $catalog.files.Count) 'Unknown files interfered with owned-file migration'
foreach($relative in $unknown){Check ((Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash -eq $hashes[$relative]) ('Unknown/device bundle altered: '+$relative)}

$root=New-Fixture 'modified known names'
$modified=@($catalog.files | Where-Object{$_.name -like 'V1_SD_BLE*'} | Select-Object -First 1)[0]
$path=Join-Path $root ('firmware\'+$modified.name);$bytes=[IO.File]::ReadAllBytes($path);$bytes[20]=$bytes[20] -bxor 1;[IO.File]::WriteAllBytes($path,$bytes)
$changedHash=(Get-FileHash -LiteralPath $path).Hash
$readme=Join-Path $root 'firmware\README.md';[IO.File]::WriteAllText($readme,'User-owned documentation');$readmeHash=(Get-FileHash -LiteralPath $readme).Hash
$result=Run-Migration $root
Check ($result.archived_files -eq $catalog.files.Count-2) 'Modified known names were admitted as installer owned'
Check ((Get-FileHash -LiteralPath $path).Hash -eq $changedHash) 'Modified same-length firmware was moved or altered'
Check ((Get-FileHash -LiteralPath $readme).Hash -eq $readmeHash) 'User-modified README was replaced'

$fresh=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'fresh install does not exist'
$result=Run-Migration $fresh
Check ($result.archived_files -eq 0 -and !(Test-Path -LiteralPath $fresh)) 'Fresh install created firmware or a legacy folder'

$root=New-Fixture 'locked owned file'
$locked=New-Object IO.FileStream((Join-Path $root ('firmware\'+$modified.name)),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try{Run-Migration $root $false | Out-Null}finally{$locked.Dispose()}
Check-AllActive $root
Check (!(Test-Path -LiteralPath (Join-Path $root 'legacy'))) 'Hash preflight failure partially archived firmware'

$root=New-Fixture 'path traversal catalog'
$bad=Join-Path $root 'bad-catalog.json'
@{schema_version=1;family='V1';files=@(@{name='..\outside.hex';sha256=('a'*64);bytes=1})} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $bad -Encoding UTF8
Run-Migration $root $false $bad | Out-Null;Check-AllActive $root

$root=New-Fixture 'linked firmware directory'
$linked=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'junction installation';New-Item -ItemType Directory -Path $linked | Out-Null
New-Item -ItemType Junction -Path (Join-Path $linked 'firmware') -Target (Join-Path $root 'firmware') | Out-Null
Run-Migration $linked $false | Out-Null;Check-AllActive $root
Run-Migration 'C:\' $false | Out-Null

Write-Output ('PASS '+$checks+' actual Windows PowerShell migration checks: exact fixture, archived hashes, idempotence, unknown/modified/device-bundle preservation, literal argument paths, locked-file preflight and directory/catalog boundaries; no installer execution or hardware calls.')
