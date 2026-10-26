# Software-only: extracted production methods and scripted transport, no device access.
$ErrorActionPreference='Stop'
$repo=$PSScriptRoot
$out=Join-Path $repo '_artifacts/download_burst_source_tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$reader=[IO.File]::ReadAllText((Join-Path $repo 'USB512_interface/USB512_interface/NpLogger/NpDownloadStream.cs'))
function Slice([string]$start,[string]$end){
 $a=$reader.IndexOf($start,[StringComparison]::Ordinal)
 $b=$reader.IndexOf($end,$a,[StringComparison]::Ordinal)
 if($a -lt 0 -or $b -le $a){throw "Production method boundary missing: $start"}
 return $reader.Substring($a,$b-$a)
}
$methods=(Slice '        internal static uint U(' '        internal NpDownloadStream(')+
 (Slice '        private static void ValidateReplyHeader' '        private void WaitForDownloadReady')+
 (Slice '        private void WaitForDownloadReady' '        public override int Read(')
$a=$reader.IndexOf('        public override int Read(', [StringComparison]::Ordinal)
$b=$reader.LastIndexOf("`n    }", [StringComparison]::Ordinal)
if($a -lt 0 -or $b -le $a){throw 'Production stream method boundary missing'}
$methods+=$reader.Substring($a,$b-$a)
$template=[IO.File]::ReadAllText((Join-Path $repo 'Tests/NpDownloadBurstTests.cs'))
[IO.File]::WriteAllText((Join-Path $out 'generated.cs'),$template.Replace('/*PRODUCTION_METHODS*/',$methods))
$framework='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8.1'
$csc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$exe=Join-Path $out 'NpDownloadBurstTests.exe'
& $csc /nologo /target:exe /nostdlib+ "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" "/out:$exe" (Join-Path $out 'generated.cs') (Join-Path $repo 'USB512_interface/USB512_interface/NpLogger/NpDownloadBurst.cs')
if($LASTEXITCODE -ne 0){throw 'Burst source test compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Burst source tests failed'}
