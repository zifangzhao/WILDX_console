$ErrorActionPreference='Stop'
$taskPayload=Join-Path $PSScriptRoot 'extracted'
$taskAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $taskPayload 'WILDX_console.exe'))
if($taskAssembly.GetName().Version.ToString() -ne '1.0.0.128'){throw 'Wrong packaged version'}
$taskType=$taskAssembly.GetType('USB512_interface.NpFirmwareCatalog',$true)
$taskCatalog=$taskType.GetProperty('Bundled',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$taskError=$taskType.GetField('Error',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($taskCatalog)
if($taskError){throw $taskError}
$taskReleases=$taskType.GetField('Releases',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($taskCatalog)
if($taskReleases.Length -ne 2){throw 'Firmware catalog incomplete'}
'Packaged version and both firmware catalogs: PASS'
