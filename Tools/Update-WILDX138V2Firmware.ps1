param([string]$InstallDirectory)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run Install-V2Firmware.cmd with Windows PowerShell.'}
if(!$InstallDirectory){
 $InstallDirectory=(Get-ItemProperty 'HKCU:/Software/WILDX_console' -ErrorAction SilentlyContinue).InstallDir
 if(!$InstallDirectory){$InstallDirectory=Join-Path $env:LOCALAPPDATA 'WILDX_console'}
}
$InstallDirectory=[IO.Path]::GetFullPath($InstallDirectory)
$app=Join-Path $InstallDirectory 'WILDX_console.exe'
if(!(Test-Path -LiteralPath $app)){throw "WILD-X installation not found: $InstallDirectory"}
if((Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -ne '6E1A2E88B98E6DC55A2733082BECD8496913F91DA99A5C4A86876BC195B8DB86'){throw 'This firmware bundle requires the published WILD-X 1.0.0.138 application.'}
if(Get-Process -Name WILDX_console -ErrorAction SilentlyContinue){throw 'Close WILD-X, then run this update again.'}
$bundle=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundle-manifest.json') -Raw | ConvertFrom-Json
$packageFirmware=Join-Path $PSScriptRoot 'Firmware'
$firmware=Join-Path $InstallDirectory 'Firmware'
$catalog=Join-Path $firmware 'releases.json'
if(!(Test-Path -LiteralPath $catalog)){throw 'Installed firmware catalog is missing.'}
foreach($item in $bundle.files){
 if($item.path -ne 'releases.json' -and $item.path -notmatch '^V2/[A-Za-z0-9_.-]+\.(hex|xenp)$'){throw 'Unexpected firmware bundle path.'}
 $source=Join-Path $packageFirmware $item.path
 if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $item.sha256){throw "Firmware bundle hash mismatch: $($item.path)"}
}
$incoming=Get-Content -LiteralPath (Join-Path $packageFirmware 'releases.json') -Raw | ConvertFrom-Json
$installed=Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json
$v2=@($incoming.releases | Where-Object family -eq 2)
$v1=@($installed.releases | Where-Object family -eq 1)
if($incoming.schemaVersion -ne 1 -or $installed.schemaVersion -ne 1 -or $v2.Count -ne 1 -or $v1.Count -ne 1 -or @($installed.releases).Count -ne 2){throw 'Unsupported firmware catalog.'}
if($v2[0].appCrc32 -ne '8DE56637' -or $v2[0].usbCrc32 -ne '1D3262FB'){throw 'Unexpected V2 firmware identity.'}
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$assembly=[Reflection.Assembly]::LoadFrom($app)
$catalogType=$assembly.GetType('USB512_interface.NpFirmwareCatalog',$true)
$load=$catalogType.GetMethod('Load',$flags)
# Loading verifies both families and every catalog asset using the installed app.
$null=$load.Invoke($null,[object[]]@([string]$firmware))
$oldV2=@($installed.releases | Where-Object family -eq 2)
if(($oldV2[0] | ConvertTo-Json -Depth 50 -Compress) -eq ($v2[0] | ConvertTo-Json -Depth 50 -Compress)){
 Write-Output 'The matching V2 firmware catalog is already installed.'
 return
}
$v2Folder=Join-Path $firmware 'V2'
if(Test-Path -LiteralPath $v2Folder){if((Get-Item -LiteralPath $v2Folder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Firmware/V2 must be an ordinary installation folder.'}}
New-Item -ItemType Directory -Path $v2Folder -Force | Out-Null
foreach($item in $bundle.files){
 if($item.path -eq 'releases.json'){continue}
 $destination=Join-Path $firmware $item.path
 Copy-Item -LiteralPath (Join-Path $packageFirmware $item.path) -Destination $destination
 if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $item.sha256){throw "Installed firmware hash mismatch: $($item.path)"}
}
$merged=[ordered]@{schemaVersion=1;releases=@($v1[0],$v2[0])}
$temporary=Join-Path $firmware ('releases.'+[Guid]::NewGuid().ToString('N')+'.new')
$backup=Join-Path $firmware ('releases.before-V2-1D-8DE.'+[Guid]::NewGuid().ToString('N')+'.bak')
[IO.File]::WriteAllText($temporary,($merged | ConvertTo-Json -Depth 50),[Text.UTF8Encoding]::new($false))
[IO.File]::Replace($temporary,$catalog,$backup)
try{$null=$load.Invoke($null,[object[]]@([string]$firmware))}
catch{[IO.File]::Copy($backup,$catalog,$true);throw}
Write-Output 'V2 firmware files and matching catalog installed. Open WILD-X and use Firmware Update to update the device.'
Write-Output "Previous catalog saved as $backup"
