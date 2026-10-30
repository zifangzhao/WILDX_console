param([string]$OutputDirectory="$PSScriptRoot/_artifacts/sd_attempt_evidence_20261028")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskSource=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskExe=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'SdAttemptEvidenceTests.exe'
$taskSources=@("$taskSource/NpFirstSyncWire.cs","$taskSource/NpFirstSyncSd.cs","$taskSource/NpFirstSyncSdClient.cs","$taskSource/NpFirstSyncSdAttemptEvidence.cs",
    "$PSScriptRoot/Neuropixels/SD/NpMetadataFile.cs","$PSScriptRoot/Tests/NpFirstSyncSdAttemptEvidenceTests.cs")
& 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /warnaserror /nowarn:0649 /r:System.Web.Extensions.dll "/out:$taskExe" @taskSources
if($LASTEXITCODE){throw 'SD attempt evidence fixtures failed to compile'}
& $taskExe ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE){throw 'SD attempt evidence fixtures failed'}
