param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble-preview-multi-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$src=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$text=[IO.File]::ReadAllText((Join-Path $src 'Ble/NpSdControl.cs'))
$at=$text.IndexOf('internal static uint Crc(',[StringComparison]::Ordinal)
if($at -lt 0){throw 'Production CRC method missing'}
$start=$text.IndexOf('{',$at);$depth=1;$end=$start+1
while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
$crc=$text.Substring($at,$end-$at)
$deps=Join-Path $OutputDirectory 'dependencies.cs'
[IO.File]::WriteAllText($deps,'using System;namespace CE32_console{internal static class NpSdControl{'+$crc+'}internal static class NpV1PreviewBurst{internal const uint Magic=0x3156504e;}internal static class NpSdGeometry{internal static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}}}')
$exe=Join-Path $OutputDirectory 'NpV2MultiPreviewTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /optimize+ /target:exe "/out:$exe" (Join-Path $PSScriptRoot 'Tests/NpV2MultiPreviewTests.cs') $deps (Join-Path $src 'Ble/NpV2MultiPreviewWire.cs') (Join-Path $src 'Ble/NpV2PreviewWire.cs') (Join-Path $src 'Ble/NpV2Preview.cs')
if($LASTEXITCODE){throw 'Multi-preview test compilation failed'}
& $exe
if($LASTEXITCODE){throw 'Multi-preview tests failed'}
