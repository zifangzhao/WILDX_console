param([string]$OutputDirectory="$PSScriptRoot/_artifacts/fine_clock_import_20261029")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskSources=@("$taskUi/NpFirstSyncWire.cs","$taskUi/NpFirstSyncSd.cs","$taskUi/NpFineClockWire.cs","$taskUi/NpFineClockEvidenceImport.cs","$PSScriptRoot/Tests/NpFirstSyncWireTests.cs","$PSScriptRoot/Tests/NpFineClockEvidenceImportTests.cs")
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
foreach($taskEnabled in @($false,$true)){
    $taskExecutable=Join-Path $taskOutput ("FineClockImport-"+$taskEnabled+".exe")
    $taskDefines='/define:NP2_CONSOLE_STANDALONE';if($taskEnabled){$taskDefines='/define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE'}
    & $taskCompiler /nologo /optimize+ /nowarn:0649 /target:exe /main:NpFineClockEvidenceImportTests /r:System.Web.Extensions.dll $taskDefines "/out:$taskExecutable" @taskSources
    if($LASTEXITCODE){throw 'SD host-clock association checks did not compile'}
    & $taskExecutable $taskOutput
    if($LASTEXITCODE){throw 'SD host-clock association checks failed'}
}
