param([string]$OutputDirectory="$PSScriptRoot/_artifacts/usb-catalog-repair-tests",[string]$ConsoleAssembly)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Slice([string]$text,[string]$start,[string]$end){$a=$text.IndexOf($start);$b=$text.IndexOf($end,$a);if($a-lt0-or$b-le$a){throw 'Production method markers changed'};$text.Substring($a,$b-$a)}
$appRoot=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$v1=Get-Content -LiteralPath "$appRoot/NpLogger/NpV1Management.cs" -Raw
$v2=Get-Content -LiteralPath "$appRoot/NpLogger/NpV2FirmwareForm.cs" -Raw
$stream=Get-Content -LiteralPath "$appRoot/NpLogger/NpDownloadStream.cs" -Raw
$service=Get-Content -LiteralPath "$PSScriptRoot/Neuropixels/USBConsoleV2/NpV2UsbConsole.cs" -Raw
$fixture=(Get-Content -LiteralPath "$PSScriptRoot/Tests/NpUsbCatalogRepairTests.cs" -Raw).
    Replace('ACTUAL_V1_STATUS',(Slice $v1 '        internal sealed class Status' '        internal static string UidFromPath(')).
    Replace('ACTUAL_V1_UID',(Slice $v1 '        internal static string UidFromPath(' '        internal static List<NpV1Target> Enumerate(')).
    Replace('ACTUAL_V1_PARSE',(Slice $v1 '        internal static Status ParseSplitStatus(' '        internal static bool CanUpdateApp(')).
    Replace('ACTUAL_V1_ABI',(Slice $v1 '        private static bool SupportedAbi(' '        internal static void RequireCompatibleLoader(')).
    Replace('ACTUAL_V2_UID',(Slice $v2 '        internal static string Uid(' '        void Discover(')).
    Replace('ACTUAL_CRC',(Slice $stream '        private static readonly uint[] CrcTable=' '        internal NpDownloadStream(string path,bool allowSynthetic=false)')).
    Replace('ACTUAL_SERVICE_STATUS',(Slice $service '    internal sealed class NpV2ServiceStatus' '    internal sealed class NpV2UsbSelfTestReport'))
$fixture="using System.Text.RegularExpressions;`r`nusing System.Globalization;`r`n"+$fixture
$fixturePath=Join-Path $OutputDirectory 'UsbCatalogRepairFixture.cs'
[IO.File]::WriteAllText($fixturePath,$fixture,[Text.UTF8Encoding]::new($false))
$csc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $OutputDirectory 'UsbCatalogRepairTests.exe'
$maintenance=Get-Content -LiteralPath "$appRoot/NpLogger/NpUsbCatalogRepair.cs" -Raw
$namespace=$maintenance.IndexOf('namespace USB512_interface')
$class=$maintenance.IndexOf('    internal sealed class NpUsbCatalogRepair')
if($namespace-lt0-or$class-lt0){throw 'Catalog repair class not found'}
# Format has its own actual-app fake-boundary tests; this fixture isolates repair.
$repairPath=Join-Path $OutputDirectory 'ActualUsbCatalogRepair.cs'
[IO.File]::WriteAllText($repairPath,$maintenance.Substring(0,$namespace)+"namespace USB512_interface`r`n{`r`n"+$maintenance.Substring($class),[Text.UTF8Encoding]::new($false))
& $csc /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$exe" $repairPath $fixturePath
if($LASTEXITCODE){throw 'V1/V2 USB catalog transaction checks did not compile'}
& $exe
if($LASTEXITCODE){throw 'V1/V2 USB catalog transaction checks failed'}
if($ConsoleAssembly){
    $uiExe=Join-Path $OutputDirectory 'UsbCatalogRepairUiTests.exe'
    & $csc /nologo /r:System.Windows.Forms.dll "/out:$uiExe" "$PSScriptRoot/Tests/NpUsbCatalogRepairUiTests.cs"
    if($LASTEXITCODE){throw 'V1/V2 USB catalog UI checks did not compile'}
    & $uiExe $ConsoleAssembly
    if($LASTEXITCODE){throw 'V1/V2 USB catalog UI checks failed'}
}
