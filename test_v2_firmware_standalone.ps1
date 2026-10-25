param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2_catalog_20261025/checks")
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Use Windows PowerShell for the actual Framework138 catalog checks.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh check output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$bundle=Join-Path $PSScriptRoot '_artifacts/v2_catalog_20261025/standalone'
$payload=Join-Path $PSScriptRoot '_artifacts/v1_ble_compact_20261025/release/app'
$install=Join-Path $OutputDirectory 'installed'
$manifest=Get-Content (Join-Path $PSScriptRoot 'LATEST_INSTALLERS/release-manifest.json') -Raw | ConvertFrom-Json
if($manifest.version -ne '1.0.0.138'){throw 'Expected unchanged138 installer manifest.'}
foreach($item in $manifest.payload){
 $source=Join-Path $payload $item.path
 if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $item.sha256){throw 'Original138 payload changed.'}
 $target=Join-Path $install $item.path
 New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
 Copy-Item -LiteralPath $source -Destination $target
}
function V1Hashes {
 @(Get-ChildItem -LiteralPath (Join-Path $install 'Firmware/V1') -Recurse -File | Sort-Object FullName | ForEach-Object {$_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}) -join "`n"
}
$catalog=Join-Path $install 'Firmware/releases.json'
$before=Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json
$v1Before=($before.releases | Where-Object family -eq 1) | ConvertTo-Json -Depth 50 -Compress
$filesBefore=V1Hashes
$app=Join-Path $install 'WILDX_console.exe'
$sdk=Join-Path $install 'NpV2SdDatExport.dll'
$appHash=(Get-FileHash -LiteralPath $app).Hash;$sdkHash=(Get-FileHash -LiteralPath $sdk).Hash
& (Join-Path $bundle 'Install-V2Firmware.ps1') -InstallDirectory $install
$after=Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json
$v1After=($after.releases | Where-Object family -eq 1) | ConvertTo-Json -Depth 50 -Compress
if($v1Before -cne $v1After -or $filesBefore -cne (V1Hashes)){throw 'V1 entry or files changed.'}
if((Get-FileHash -LiteralPath $app).Hash -ne $appHash -or (Get-FileHash -LiteralPath $sdk).Hash -ne $sdkHash){throw 'Application/SDK changed.'}
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$assembly=[Reflection.Assembly]::LoadFrom($app)
$type=$assembly.GetType('USB512_interface.NpFirmwareCatalog',$true)
$loaded=$type.GetMethod('Load',$flags).Invoke($null,[object[]]@([string](Join-Path $install 'Firmware')))
$release=$type.GetMethod('For',$flags).Invoke($loaded,@([uint32]2))
$identityType=$assembly.GetType('USB512_interface.NpFirmwareIdentity',$true)
$identity=[Activator]::CreateInstance($identityType,$true)
foreach($pair in @(@('Family',[uint32]2),@('Board',[uint32]0),@('Flags',[uint32]268),@('App',$release.appIdentity),@('Usb',$release.usbIdentity),@('AppCrc32','8DE56637'),@('UsbCrc32','1D3262FB'))){$identityType.GetField($pair[0],$flags).SetValue($identity,$pair[1])}
if($release.GetType().GetMethod('Differs',$flags).Invoke($release,@($identity))){throw 'Matching pair still differs.'}
$notice=$assembly.GetType('USB512_interface.NpFirmwareNotice',$true)
if(!$notice.GetMethod('IsCurrent',$flags).Invoke($null,@($release,$identity))){throw 'Matching pair would still prompt for update.'}
$firstHash=(Get-FileHash -LiteralPath $catalog).Hash
& (Join-Path $bundle 'Install-V2Firmware.ps1') -InstallDirectory $install
if((Get-FileHash -LiteralPath $catalog).Hash -ne $firstHash){throw 'Reapply changed a matching catalog.'}
$bad=Join-Path $OutputDirectory 'bad-install';New-Item -ItemType Directory -Path $bad | Out-Null
[IO.File]::WriteAllText((Join-Path $bad 'WILDX_console.exe'),'wrong app')
$refused=$false;try{& (Join-Path $bundle 'Install-V2Firmware.ps1') -InstallDirectory $bad}catch{$refused=$true}
if(!$refused -or (Test-Path -LiteralPath (Join-Path $bad 'Firmware'))){throw 'Wrong application was not safely refused.'}
$corrupt=Join-Path $OutputDirectory 'corrupt-bundle';Copy-Item -LiteralPath $bundle -Destination $corrupt -Recurse
$image=Get-ChildItem -LiteralPath (Join-Path $corrupt 'Firmware/V2') -Filter '*.xenp' | Select-Object -First 1
$bytes=[IO.File]::ReadAllBytes($image.FullName);$bytes[128]=$bytes[128] -bxor 1;[IO.File]::WriteAllBytes($image.FullName,$bytes)
$refused=$false;try{& (Join-Path $corrupt 'Install-V2Firmware.ps1') -InstallDirectory $install}catch{$refused=$true}
if(!$refused -or (Get-FileHash -LiteralPath $catalog).Hash -ne $firstHash -or $filesBefore -cne (V1Hashes)){throw 'Corrupt bundle was not refused before changing installed metadata.'}
$receipt=[ordered]@{result='PASS actual138 standalone catalog install, V1 preservation, matching-pair/no-prompt, idempotence, wrong-app and corrupt-payload refusal';appSha256=$appHash;sdkSha256=$sdkHash;catalogSha256=$firstHash;v2AppCrc32=$release.appCrc32;v2UsbCrc32=$release.usbCrc32;v1Preserved=$true;hardwareAccessed=$false;installerExecuted=$false}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json')
Write-Output $receipt.result
