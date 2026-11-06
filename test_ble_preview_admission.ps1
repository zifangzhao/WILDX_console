param(
 [string]$FirmwareVectors='C:/Frank/Code/XE_NP/_artifacts/v1_gui_support_20261105/native-preview/vectors.bin',
 [string]$OutputDirectory="$PSScriptRoot/_artifacts/ble_preview_admission_20261105",
 [string]$InstalledExecutable
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($out)
function ExtractMethod($source,$name){
 $m=[regex]::Match($source,('(?m)^\s*internal static [^;{}\n]+\b'+[regex]::Escape($name)+'\([^;{}]*\)\s*\{'))
 if(!$m.Success){throw ('Missing production method '+$name)}
 $brace=$source.IndexOf('{',$m.Index);$depth=0
 for($i=$brace;$i -lt $source.Length;$i++){
  if($source[$i] -eq '{'){$depth++};if($source[$i] -eq '}'){$depth--}
  if(!$depth){return $source.Substring($m.Index,$i-$m.Index+1)}
 }
 throw ('Unbalanced production method '+$name)
}
$ui=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$form=Join-Path $ui 'Ble/NpBleForm.cs'
$source=[IO.File]::ReadAllText($form)
$methods=@('CanPreviewHost','PreviewBlockedReason','ClosedLoopPreviewState')|ForEach-Object {ExtractMethod $source $_}
$guard=Join-Path $out 'actual-guard.cs'
[IO.File]::WriteAllText($guard,'using System;using WildNpClosedLoop;namespace USB512_interface{internal static class NpBleForm{'+($methods -join "`n")+'}}',[Text.UTF8Encoding]::new($false))
# Only these actual geometry helpers are dependencies of the pure BLE codec;
# the SD catalog exporter and recording pipeline are outside this fixture.
$geometry=Join-Path $out 'actual-geometry.cs'
$geoSource=[IO.File]::ReadAllText((Join-Path $ui 'NpLogger/NpSdGeometry.cs'))
$geoMethods=@('U','Count','Valid')|ForEach-Object {ExtractMethod $geoSource $_}
[IO.File]::WriteAllText($geometry,'using System;namespace CE32_console{internal static class NpSdGeometry{'+($geoMethods -join "`n")+'}}',[Text.UTF8Encoding]::new($false))
$inputs=@((Join-Path $PSScriptRoot 'Tests/NpBlePreviewAdmissionTests.cs'),$guard,$geometry,(Join-Path $ui 'Ble/NpV1BleProtocol.cs'),(Join-Path $ui 'Ble/NpSdControl.cs'),(Join-Path $PSScriptRoot 'Neuropixels/ClosedLoop/NpClosedLoopControl.cs'),(Join-Path $PSScriptRoot 'Neuropixels/ClosedLoop/NpClosedLoopEvent.cs'))
$exe=Join-Path $out 'PreviewAdmissionTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /target:exe "/out:$exe" @inputs
if($LASTEXITCODE){throw 'Preview admission fixture compilation failed'}
$arguments=@([IO.Path]::GetFullPath($FirmwareVectors))
if($InstalledExecutable){$arguments+=[IO.Path]::GetFullPath($InstalledExecutable)}
$result=& $exe @arguments
$exit=$LASTEXITCODE
$result|Set-Content -LiteralPath (Join-Path $out 'result.txt') -Encoding utf8
if($exit){throw 'Preview admission fixture failed'}
$pins=@(@($inputs)+@($form,(Join-Path $ui 'Ble/NpV1BlePreviewForm.cs'),$FirmwareVectors)+@($InstalledExecutable|Where-Object {$_})|ForEach-Object {[pscustomobject]@{path=[IO.Path]::GetFullPath($_);sha256=(Get-FileHash -LiteralPath $_).Hash}})
[ordered]@{status='PASS';result=($result -join "`n");hardware_access=$false;app_build=$false;scope='Actual extracted admission and receipt methods plus canonical pure BLE parsers;121 actual staged firmware packet vectors. No UI discovery, live BLE or recording validation claim.';inputs=$pins}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $out 'receipt.json') -Encoding utf8
$result
