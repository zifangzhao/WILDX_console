param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v1_ble_compact_20261025/checks")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
function Slice([string]$text,[string]$start,[string]$end){
 $a=$text.IndexOf($start,[StringComparison]::Ordinal);$b=$text.IndexOf($end,$a,[StringComparison]::Ordinal)
 if($a-lt0-or$b-le$a){throw 'Production dependency markers changed'}
 $text.Substring($a,$b-$a)
}
$theme=Slice (Get-Content "$source/NeuropixelsPreviewForm.cs" -Raw) '    internal static class NpPreviewTheme' '    internal sealed class NpPreviewPalette'
$geometry=Slice (Get-Content "$source/NpLogger/NpSdGeometry.cs" -Raw) ' internal static class NpSdGeometry {' '  internal static void Validate'
$write=Slice (Get-Content "$source/Ble/NpBleConnection.cs" -Raw) '        internal async Task<bool> TryWritePreview' '        internal async Task RescueReset'
$deps=Join-Path $OutputDirectory 'production-preview-dependencies.cs'
[IO.File]::WriteAllText($deps,'using System;using System.IO;using System.Drawing;using System.Windows.Forms;using System.Threading;using System.Threading.Tasks;using CE32_console;namespace USB512_interface{'+$theme+'internal sealed partial class NpBleConnection{'+$write+'}}namespace CE32_console{internal static class NpV2PreviewBurst{internal const uint Magic=0x3256504e;}'+$geometry+'}}',[Text.UTF8Encoding]::new($false))
$inputs=@("$PSScriptRoot/Tests/NpV1CompactPreviewTests.cs",$deps,"$source/Ble/NpV1BleProtocol.cs","$source/Ble/NpV1BlePreviewForm.cs","$source/Ble/NpBleWire.cs","$source/Ble/NpSdControl.cs","$source/NpAdcCalibration.cs","$source/WildXTheme.cs")
$exe=Join-Path $OutputDirectory 'NpV1CompactPreviewTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$exe" @inputs
if($LASTEXITCODE){throw 'V1 compact preview source fixture failed to compile'}
& $exe $OutputDirectory "$PSScriptRoot/Tests/Fixtures/V1CompactPreview/ap-lfp.bin"
if($LASTEXITCODE){throw 'V1 compact preview source fixture failed'}
Get-FileHash -Algorithm SHA256 -LiteralPath @($inputs+"$source/Ble/NpBleConnection.cs") | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'source-inputs.json')
