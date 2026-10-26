$ErrorActionPreference='Stop'
$root='D:/Code/WILDX_console/WILDX_console/_artifacts/v1_lfp_preview_20261026/extracted149'
$app=[Reflection.Assembly]::LoadFrom("$root/WILDX_console.exe")
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$type=$app.GetType('USB512_interface.NpFirmwareCatalog',$true)
$catalog=$type.GetProperty('Bundled',$flags).GetValue($null,$null)
$errorText=$type.GetField('Error',$flags).GetValue($catalog);if($errorText){throw $errorText}
$actualRoot=[string]$type.GetField('Root',$flags).GetValue($catalog)
if([IO.Path]::GetFullPath($actualRoot) -ne [IO.Path]::GetFullPath((Join-Path $root 'Firmware'))){throw 'Unexpected bundled root'}
if($app.GetName().Version.ToString() -ne '1.0.0.149'){throw 'Wrong app version'}
foreach($family in 1,2){$release=$type.GetMethod('For',$flags).Invoke($catalog,@([uint32]$family));$want=if($family -eq 1){'4F7B521A'}else{'A71BDECC'};if($release.usbCrc32 -ne $want){throw 'Firmware mismatch'};"PASS extracted149 automatic Bundled catalog: V$family APP$($release.appCrc32) / USB$want"}
