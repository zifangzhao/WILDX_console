param([string]$OutputDirectory='C:/Frank/Code/USB_PCconsole/_artifacts/sd_usb_test_wire',[string]$NativeGoldenDirectory='')
$ErrorActionPreference='Stop'
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskInputs=@('Tests/NpSdUsbTestWireTests.cs','USB512_interface/USB512_interface/NpSdUsbTestWire.cs','USB512_interface/USB512_interface/NpFirstSyncSd.cs','USB512_interface/USB512_interface/NpFirstSyncWire.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$taskBefore=@($taskInputs|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
# Shared NpFirstSyncBinding DTO fields are deliberately unassigned by this codec-only fixture.
& $taskCompiler /nologo /warnaserror /nowarn:0649 /r:System.Core.dll /main:NpSdUsbTestWireTests "/out:$taskOutput/NpSdUsbTestWireTests.exe" @taskInputs
if($LASTEXITCODE){throw 'SD USB test wire fixture compilation failed'}
$taskArguments=@($taskOutput)
if($NativeGoldenDirectory){$taskArguments+=[IO.Path]::GetFullPath($NativeGoldenDirectory)}
& "$taskOutput/NpSdUsbTestWireTests.exe" @taskArguments
if($LASTEXITCODE){throw 'SD USB test wire focused checks failed'}
foreach($taskInput in $taskBefore){if((Get-FileHash -LiteralPath $taskInput.path -Algorithm SHA256).Hash -cne $taskInput.sha256){throw 'Source changed during focused checks'}}
$taskChecks=Get-Content -LiteralPath (Join-Path $taskOutput 'checks.json') -Raw|ConvertFrom-Json
$taskReceipt=[ordered]@{status='PASS';managed_checks=$taskChecks.checks;source_stable=$true;inputs=$taskBefore;native_golden_directory=$NativeGoldenDirectory;exe_sha256=(Get-FileHash -LiteralPath (Join-Path $taskOutput 'NpSdUsbTestWireTests.exe') -Algorithm SHA256).Hash;goldens=@(Get-ChildItem -LiteralPath $taskOutput -Filter 'managed-*.bin'|ForEach-Object {[ordered]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}});hardware_access=$false;production_build=$false}
[IO.File]::WriteAllText((Join-Path $taskOutput 'receipt.json'),($taskReceipt|ConvertTo-Json -Depth 6)+"`n",[Text.UTF8Encoding]::new($false))
[pscustomobject]$taskReceipt|Select-Object status,managed_checks,source_stable|ConvertTo-Json
