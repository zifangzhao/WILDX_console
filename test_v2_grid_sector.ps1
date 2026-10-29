param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2_grid_sector_20261029")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
# Byte-exact source: XE_NP_V2/Tools/V2Timing/test_npgrid02_sector.c, SHA2563354B7C7A5EED2E457F93B416918D9845CF7AD6AD42E98A687EC3BE5812C9ADC.
$taskPrototype="$PSScriptRoot/Tests/Fixtures/NpV2GridSectorPrototype.c"
$taskPrototypeHash=(Get-FileHash -LiteralPath $taskPrototype -Algorithm SHA256).Hash
if($taskPrototypeHash-ne'3354B7C7A5EED2E457F93B416918D9845CF7AD6AD42E98A687EC3BE5812C9ADC'){throw 'Native prototype changed; review before testing'}
# Compile the actual prototype in this one TU; rename its main, then use its
# actual encode/decode helpers for golden emission. No V2 source is changed.
$taskC=@'
#define main actual_npgrid02_prototype_main
#include "__NPGRID02_FIXTURE__"
#undef main
int main(int argc,char **argv) {
    uint8_t frame[480],sector[512];int32_t rows[12][9];NpImuGridHeader h={0};FILE *out;
    if(argc!=2)return 2;
    if(actual_npgrid02_prototype_main())return 3;
    for(unsigned r=0;r<12;r++)for(unsigned c=0;c<9;c++)rows[r][c]=(int32_t)(r*10+c);
    out=fopen(argv[1],"wb");if(!out)return 4;
    h.capture_generation=42;h.slot_count=12;h.observed_mask=h.ag_valid=h.mag_valid=0xfff;
    size_t n=np_imu_grid_pack(frame,sizeof frame,&h,rows,12);assert(n==480&&encode(sector,42,7,frame,n));
    assert(fwrite(sector,1,512,out)==512);
    h.first_slot=12;h.slot_count=1;h.observed_mask=h.ag_valid=h.mag_valid=1;
    n=np_imu_grid_pack(frame,sizeof frame,&h,rows,1);assert(n==84&&encode(sector,42,7,frame,n));
    assert(fwrite(sector,1,512,out)==512);assert(fclose(out)==0);return 0;
}
'@
$taskC=$taskC.Replace('__NPGRID02_FIXTURE__',$taskPrototype.Replace('\','/'))
[IO.File]::WriteAllText("$taskOutput/native_golden.c",$taskC,[Text.UTF8Encoding]::new($false))
$taskVc='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/VC/Auxiliary/Build/vcvars64.bat'
$taskCommand='call "'+$taskVc+'" >nul && cl /nologo /std:c11 /W4 /WX /O2 /D_CRT_SECURE_NO_WARNINGS /I"'+$PSScriptRoot+'/Neuropixels/Timing" "'+$taskOutput+'/native_golden.c" /Fo"'+$taskOutput+'/native_golden.obj" /Fe"'+$taskOutput+'/native_golden.exe"'
& $env:ComSpec /d /c $taskCommand
if($LASTEXITCODE){throw 'Actual native prototype/golden fixture compile failed'}
& "$taskOutput/native_golden.exe" "$taskOutput/native_sectors.bin"
if($LASTEXITCODE){throw 'Actual native prototype/golden fixture failed'}
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskCodec="$PSScriptRoot/USB512_interface/USB512_interface/NpV2GridSector.cs"
$taskInner="$PSScriptRoot/USB512_interface/USB512_interface/NpImuGridWire.cs"
$taskPhase="$PSScriptRoot/USB512_interface/USB512_interface/NpImuGridPhase.cs"
$taskTests="$PSScriptRoot/Tests/NpV2GridSectorTests.cs"
& $taskCompiler /nologo /langversion:7.3 /warnaserror /r:System.Core.dll /main:NpV2GridSectorTests "/out:$taskOutput/Managed.exe" $taskCodec $taskInner $taskPhase $taskTests
if($LASTEXITCODE){throw 'Standalone managed sector fixture compile failed'}
& "$taskOutput/Managed.exe" "$taskOutput/native_sectors.bin"
if($LASTEXITCODE){throw 'Standalone managed sector fixture failed'}
if((Get-FileHash -LiteralPath $taskPrototype -Algorithm SHA256).Hash-ne$taskPrototypeHash){throw 'Prototype changed during test'}
$taskPins=@($taskPrototype,$taskCodec,$taskInner,$taskPhase,$taskTests,"$PSScriptRoot/Neuropixels/Timing/np_imu_grid_wire.h",$PSCommandPath)|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
$taskReceipt=[ordered]@{scope='Standalone candidate codec only; no project/carrier/export/firmware integration or hardware';passed=$true;inputs=$taskPins;golden_sha256=(Get-FileHash -LiteralPath "$taskOutput/native_sectors.bin" -Algorithm SHA256).Hash;golden_bytes=1024;native_prototype_executed=$true;native_golden_scope='NSG1 only; V2 native producer NSGP phase parity not tested';phase_binding='UID/session/generation/source epoch/counter domain/token; session-wide ordering remains caller policy';managed_language='7.3'}
[IO.File]::WriteAllText("$taskOutput/receipt.json",($taskReceipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
