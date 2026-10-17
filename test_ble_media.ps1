param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-media-tests",[string]$ConsoleExecutable)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$form=Get-Content -LiteralPath (Join-Path $source 'Ble/NpBleForm.cs') -Raw
function Method([string]$start,[string]$end) {$a=$form.IndexOf($start);$b=$form.IndexOf($end,$a);if($a -lt 0 -or $b -le $a){throw "Method not found: $start"};$form.Substring($a,$b-$a)}
$methods=(Method '        async Task Run(' '        void OfferFirmwareReview(')+(Method '        void Profiles(' '        async Task Apply(')
$test=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Tests/NpBleMediaFlowTests.cs') -Raw).Replace('ACTUAL_FORM_METHODS',$methods)
$fixture=Join-Path $OutputDirectory 'MediaFlow.cs';[IO.File]::WriteAllText($fixture,$test)
$compiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'MediaFlowTests.exe'
& $compiler /nologo /warnaserror /nowarn:0649 /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" $fixture (Join-Path $source 'Ble/NpBleForm.Media.cs') (Join-Path $source 'Ble/NpBleConnection.Media.cs') (Join-Path $source 'Ble/NpV1BleProtocol.cs') (Join-Path $source 'Ble/NpV2SdControl.cs') (Join-Path $source 'Ble/NpBleWire.cs') (Join-Path $source 'Ble/NpSdControl.cs') (Join-Path $source 'NpLogger/NpSdGeometry.cs')
if($LASTEXITCODE){throw 'BLE media flow checks did not compile'}
& $exe
if($LASTEXITCODE){throw 'BLE media flow checks failed'}
if($ConsoleExecutable) {
 $assemblyTest=Join-Path $OutputDirectory 'MediaAssemblyTests.exe'
 & $compiler /nologo /warnaserror /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$assemblyTest" (Join-Path $PSScriptRoot 'Tests/NpBleMediaTests.cs')
 if($LASTEXITCODE){throw 'Media assembly checks did not compile'}
 & $assemblyTest ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path $OutputDirectory ('assembly-'+[Guid]::NewGuid().ToString('N')))
 if($LASTEXITCODE){throw 'Media assembly checks failed'}
}
