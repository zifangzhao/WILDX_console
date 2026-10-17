param(
 [string]$BaselineForm,
 [string]$OutputDirectory="$PSScriptRoot\_artifacts\ble-preview-display-tests",
 [string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$shared=Get-Content -LiteralPath "$source/NeuropixelsPreviewForm.cs" -Raw
function Slice([string]$text,[string]$start,[string]$end){
 $a=$text.IndexOf($start,[StringComparison]::Ordinal);$b=$text.IndexOf($end,$a,[StringComparison]::Ordinal)
 if($a-lt0-or$b-le$a){throw 'Production dependency markers changed'}
 $text.Substring($a,$b-$a)
}
$theme=Slice $shared '    internal static class NpPreviewTheme' '    internal sealed class NpPreviewPalette'
$filters=Slice $shared '    internal sealed class NpHighPassControl' '    // Display-only phase buffer.'
$decoder=Get-Content -LiteralPath "$PSScriptRoot/Neuropixels/USBConsoleV2/NpV2UsbConsole.cs" -Raw
$nominal=[regex]::Match($decoder,'internal static short NominalCount\(int raw12\)\s*\{[^}]+\}')
if(!$nominal.Success){throw 'Production nominal sample conversion changed'}
$connection=Get-Content -LiteralPath "$source/Ble/NpBleConnection.cs" -Raw
$previewWrite=Slice $connection '        internal async Task<bool> TryWritePreview' '        internal async Task RescueReset'
$dependencies=Join-Path $OutputDirectory 'production-display-dependencies.cs'
[IO.File]::WriteAllText($dependencies,"using System;using System.Drawing;using System.Windows.Forms;using System.Threading;using System.Threading.Tasks;using CE32_console;namespace USB512_interface{`n"+$theme+$filters+'internal static class NpV2SampleDecoder{'+$nominal.Value+'}internal sealed partial class NpBleConnection{'+$previewWrite+'}}namespace CE32_console{internal static class NpV1PreviewBurst{internal const uint Magic=0x3156504e;}}',[Text.UTF8Encoding]::new($false))
$inputs=@("$PSScriptRoot/Tests/NpBlePreviewDisplayTests.cs",$dependencies,
 "$source/NpAdcCalibration.cs","$source/WildXTheme.cs","$source/Ble/NpV2Preview.cs",
 "$source/Ble/NpV2PreviewWire.cs","$source/Ble/NpBleWire.cs","$source/Ble/NpV2SdControl.cs","$source/Ble/NpSdControl.cs","$source/NpLogger/NpSdGeometry.cs")
foreach($kind in @('baseline','fixed')){
 if($kind-eq'baseline'-and!$BaselineForm){continue}
 $form=if($kind-eq'baseline'){[IO.Path]::GetFullPath($BaselineForm)}else{"$source/Ble/NpBlePreviewForm.cs"}
 $exe=Join-Path $OutputDirectory ("NpBlePreviewDisplayTests-$kind.exe")
 & $Compiler /nologo /optimize+ /target:exe /main:NpBlePreviewDisplayTests /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$exe" @inputs $form
 if($LASTEXITCODE){throw "Actual-source $kind display fixture did not compile"}
 if($kind-eq'baseline'){& $exe --baseline}else{& $exe}
 if($LASTEXITCODE){throw "Actual-source $kind display fixture failed"}
}
Get-FileHash -Algorithm SHA256 -LiteralPath @($inputs+"$source/Ble/NpBlePreviewForm.cs"+"$source/Ble/NpBleConnection.cs"+"$source/NeuropixelsPreviewForm.cs"+"$PSScriptRoot/Neuropixels/USBConsoleV2/NpV2UsbConsole.cs") | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'source-inputs.json')
