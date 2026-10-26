param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-preview-capture-tests",[string]$ConnectionSource)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source="$PSScriptRoot/USB512_interface/USB512_interface"
if(!$ConnectionSource){$ConnectionSource="$source/Ble/NpBleConnection.cs"}
function Slice([string]$text,[string]$start,[string]$end){
 $a=$text.IndexOf($start,[StringComparison]::Ordinal);$b=$text.IndexOf($end,$a,[StringComparison]::Ordinal)
 if($a-lt0-or$b-le$a){throw 'Production method markers changed'}
 $text.Substring($a,$b-$a)
}
$connection=Get-Content -LiteralPath $ConnectionSource -Raw
$write=Slice $connection '        internal async Task<bool> TryWritePreview' '        internal async Task RescueReset'
$capture=Slice $connection '        internal async Task<NpV2PreviewBurst> CapturePreview' '        internal Task<byte[]> ExchangeAsync(byte[] request)'
$wire=Slice (Get-Content "$source/Ble/NpBleWire.cs" -Raw) '        internal static byte[] Frame' '        internal List<Tuple<byte, byte[]>> Feed'
$geometry=Slice (Get-Content "$source/NpLogger/NpSdGeometry.cs" -Raw) ' internal static class NpSdGeometry {' '  internal static void Validate'
$deps=Join-Path $OutputDirectory 'production-capture-methods.cs'
[IO.File]::WriteAllText($deps,'using System;using System.IO;using System.Threading;using System.Threading.Tasks;using CE32_console;namespace USB512_interface{internal sealed partial class NpBleConnection{'+$write+$capture+'}internal sealed class NpBleWire{'+$wire+'}}namespace CE32_console{internal static class NpV1PreviewBurst{internal const uint Magic=0x3156504e;}'+$geometry+'}}')
$exe=Join-Path $OutputDirectory 'NpBlePreviewCaptureTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /target:exe "/out:$exe" "$PSScriptRoot/Tests/NpBlePreviewCaptureTests.cs" $deps "$source/Ble/NpV2PreviewWire.cs" "$source/Ble/NpV2Preview.cs" "$source/Ble/NpSdControl.cs" "$source/Ble/NpV2SdControl.cs"
if($LASTEXITCODE){throw 'Preview capture fixture compilation failed'}
& $exe
if($LASTEXITCODE){throw 'Preview capture regression failed'}
