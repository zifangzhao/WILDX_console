param([string]$OutputDirectory="$PSScriptRoot/_artifacts/ble_session_folder_locator")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh fixture output required.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskSources=@("$taskUi/NpFirstSyncWire.cs","$taskUi/NpFirstSyncSd.cs","$taskUi/NpFineClockWire.cs","$taskUi/NpFineClockEvidenceImport.cs","$PSScriptRoot/Tests/NpFirstSyncWireTests.cs","$PSScriptRoot/Tests/NpFineClockEvidenceImportTests.cs","$PSScriptRoot/Tests/NpFineClockLocatorTests.cs")
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskExecutable=Join-Path $OutputDirectory 'FineClockLocatorTests.exe'
& $taskCompiler /nologo /optimize+ /nowarn:0649 /target:exe /main:NpFineClockLocatorTests /r:System.Web.Extensions.dll /define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE "/out:$taskExecutable" @taskSources
if($LASTEXITCODE){throw 'Locator fixture compilation failed.'}
& $taskExecutable ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'Locator fixture failed.'}
