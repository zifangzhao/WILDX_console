param(
    [Parameter(Mandatory=$true)][string]$ReleaseRoot,
    [Parameter(Mandatory=$true)][string]$EvidenceRoot,
    [string]$AppPath=(Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface\bin\Release\WILDX_console.exe')
)
$ErrorActionPreference='Stop'
# Offline, exact ED881256 fixture. Never invoke Main, discover hardware, or send commands.
$binding=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$app=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AppPath).Path)
$appDirectory=Split-Path -Parent (Resolve-Path -LiteralPath $AppPath).Path
function Invoke-Checked($type,$target,$name,[object[]]$arguments) {
    for($i=0;$i -lt $arguments.Length;$i++) {
        $arguments[$i]=$arguments[$i].PSObject.BaseObject
    }
    try { return ,($type.GetMethod($name,$binding).Invoke($target,$arguments)) }
    catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}
function Require($condition,[string]$message) { if(-not $condition){throw $message} }

# Exercise the installed app's parser on genuine captured GATT fragments, not fabricated receipts.
$wireType=$app.GetType('USB512_interface.NpBleWire',$true)
$controlType=$app.GetType('CE32_console.NpV2SdControl',$true)
foreach($run in @(@{Name='run20';Hz=20000;Terminal=5},@{Name='run30';Hz=30000;Terminal=255})) {
    $wire=[Activator]::CreateInstance($wireType,$true)
    $stream=[IO.File]::OpenRead((Join-Path $EvidenceRoot ($run.Name+'\notify.bin')))
    $reader=[IO.BinaryReader]::new($stream)
    $states=[Collections.Generic.HashSet[uint32]]::new();$replies=0;$notifications=0
    try {
        Require ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -eq 'NP2RX001') 'Wrong trace format'
        Require ($reader.ReadInt64() -gt 0) 'Invalid trace clock'
        while($stream.Position -lt $stream.Length) {
            Require ($stream.Length-$stream.Position -ge 12) 'Truncated trace record'
            $null=$reader.ReadInt64();$count=$reader.ReadInt32()
            Require ($count -gt 0 -and $count -le 65536 -and $count -le $stream.Length-$stream.Position) 'Invalid trace fragment'
            $frames=Invoke-Checked $wireType $wire 'Feed' (,($reader.ReadBytes($count)));$notifications++
            foreach($frame in $frames) {
                $body=$frame.Item2
                if($frame.Item1 -ne 0xb9 -or [BitConverter]::ToUInt32($body,0) -ne 0x32524c4e){continue}
                $status=Invoke-Checked $controlType $null 'Parse' @($body,[BitConverter]::ToUInt32($body,8),[BitConverter]::ToUInt32($body,12))
                $statusType=$status.GetType()
                Require ($statusType.GetField('Mask',$binding).GetValue($status) -eq 3) 'Firmware probe mask misread'
                if($statusType.GetField('Hz',$binding).GetValue($status) -eq $run.Hz) {
                    $null=$states.Add($statusType.GetField('State',$binding).GetValue($status))
                }
                $replies++
            }
        }
    } finally {$reader.Dispose()}
    Require ($replies -gt 0 -and $states.Contains(3) -and $states.Contains($run.Terminal)) 'Missing actual Recording/terminal receipts'
    "PASS $($run.Name): $notifications real notifications, $replies CRC-valid V2 control replies; Recording and terminal $($run.Terminal) parsed"
}

# Validate the actual Complete footer/UTC against the bundled exporter. The descriptor
# below is reconstructed from its common footer fields, not a retained on-card descriptor.
$exportAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $appDirectory 'NpV2SdDatExport.dll'))
$exportType=$exportAssembly.GetType('USB512_interface.NpV2SdDatExport',$true)
$footer=[IO.File]::ReadAllBytes((Join-Path $EvidenceRoot 'result20\footer.bin'))
$descriptor=$footer.Clone();[Text.Encoding]::ASCII.GetBytes('N2SDLOG1').CopyTo($descriptor,0)
[Array]::Clear($descriptor,100,408);[BitConverter]::GetBytes([uint32]1).CopyTo($descriptor,100)
$crcType=$app.GetType('CE32_console.NpSdControl',$true)
$crc=Invoke-Checked $crcType $null 'Crc' @($descriptor,508)
[BitConverter]::GetBytes([uint32]$crc).CopyTo($descriptor,508)
$start=[BitConverter]::ToUInt32($footer,56)
$capacity=[int64][BitConverter]::ToUInt32($footer,72)*512
$null=Invoke-Checked $exportType $null 'ValidateMetadata' @($descriptor,$footer,$start,$capacity)
$utc=[IO.File]::ReadAllBytes((Join-Path $EvidenceRoot 'result20\utc.bin'))
$null=Invoke-Checked $exportType $null 'ValidateUtc' @($utc,[BitConverter]::ToUInt32($footer,12),[uint32]20000)
$failed=$footer.Clone();[BitConverter]::GetBytes([uint32]4).CopyTo($failed,100)
$crc=Invoke-Checked $crcType $null 'Crc' @($failed,508)
[BitConverter]::GetBytes([uint32]$crc).CopyTo($failed,508)
$rejected=$false
try {$null=Invoke-Checked $exportType $null 'ValidateMetadata' @($descriptor,$failed,$start,$capacity)}
catch [IO.InvalidDataException] {$rejected=$true}
Require $rejected 'Failed recording admitted as Complete'
'PASS bundled SD exporter: actual Complete footer/UTC, derived descriptor geometry; resealed failed footer refused (no payload/DAT claim)'

# Call only the bundled updater's pure package loader, not its hardware entry point.
$updater=[Reflection.Assembly]::LoadFrom((Join-Path $appDirectory 'FirmwareUpdater\XeNpFirmwareUpdater.dll'))
$program=$updater.GetType('Program',$true)
$packagePath=Join-Path $ReleaseRoot 'V2_logger_APP_update_ED881256_20261010.xenp'
Require ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -eq '04A167A036EACA6F0F6B5BE3A28CA9890304F61AAC5AA02E72B271D937C76132') 'Wrong release container'
$package=Invoke-Checked $program $null 'LoadPackage' @($packagePath)
Require ($package.GetType().GetProperty('ImageCrc',$binding).GetValue($package) -eq [Convert]::ToUInt32('ED881256',16)) 'Bundled updater rejected/misread APP CRC'
Require ($package.GetType().GetProperty('Image',$binding).GetValue($package).Length -eq 146756) 'Bundled updater APP length mismatch'
'PASS bundled updater: exact sealed ABI4 ED881256 APP container accepted; no device access or flash'
'PASS offline release compatibility only; BLE automatic sync/heartbeat and preview remain separate missing desktop features; ROM DFU/native USB/recording quality not qualified'
