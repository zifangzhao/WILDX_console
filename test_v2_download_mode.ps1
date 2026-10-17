param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2-download-mode-tests")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
function Slice([string]$text,[string]$start,[string]$end){$a=$text.IndexOf($start);$b=$text.IndexOf($end,$a);if($a-lt0-or$b-le$a){throw 'Production method markers changed'};$text.Substring($a,$b-$a)}
$form=Get-Content "$PSScriptRoot/USB512_interface/USB512_interface/NpLogger/NpV2FirmwareForm.cs" -Raw
$methods=(Slice $form '        internal static string Uid(' '        void Discover(')+(Slice $form '        internal static string UsbModeOperation(' '        static async Task<string> RunHelper(')
$service=Get-Content "$PSScriptRoot/Neuropixels/USBConsoleV2/NpV2UsbConsole.cs" -Raw
$status=Slice $service '    internal sealed class NpV2ServiceStatus' '    internal sealed class NpV2UsbSelfTestReport'
$fixture=(Get-Content "$PSScriptRoot/Tests/NpV2DownloadModeTests.cs" -Raw).Replace('ACTUAL_V2_METHODS',$methods).Replace('ACTUAL_SERVICE_STATUS',$status)
$path=Join-Path $OutputDirectory 'V2DownloadMode.cs';[IO.File]::WriteAllText($path,$fixture,[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory 'V2DownloadModeTests.exe'
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Core.dll "/out:$exe" $path
if($LASTEXITCODE){throw 'V2 download mode fixture did not compile'}
& $exe
if($LASTEXITCODE){throw 'V2 download mode checks failed'}
