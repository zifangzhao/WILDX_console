param([string]$OutputDirectory="$PSScriptRoot/_artifacts/v2_read_history_20261029")
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskProjectPath=Join-Path $PSScriptRoot 'Neuropixels/SD/NpV2SdDatExport.csproj'
[xml]$taskProject=Get-Content -LiteralPath $taskProjectPath -Raw
$taskProjectFolder=Split-Path -Parent $taskProjectPath
# The actual shared RunCore/verifier/decoder closure; no MSBuild/app version or device access.
$taskSources=@($taskProject.Project.ItemGroup.Compile|ForEach-Object {[IO.Path]::GetFullPath((Join-Path $taskProjectFolder $_.Include))})
$taskSources+=@('Tests/NpV2ReadHistoryTests.cs','Neuropixels/Tests/test_np_v2_usb_console.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /langversion:7.3 /nowarn:0649,0414 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /define:NP2_CONSOLE_STANDALONE,NP_FIRST_SYNC_ENABLE /main:NpV2ReadHistoryTests "/out:$taskOutput/History.exe" $taskSources
if($LASTEXITCODE){throw 'Actual V2 read history/SDK source fixture compile failed'}
& "$taskOutput/History.exe" $PSScriptRoot $taskOutput | Tee-Object -FilePath "$taskOutput/result.txt"
if($LASTEXITCODE){throw 'V2 host read-history fixture failed'}
$taskConsolePath=Join-Path $PSScriptRoot 'Neuropixels/USBConsoleV2/NpV2UsbConsole.cs'
$taskConsole=[IO.File]::ReadAllText($taskConsolePath)
$taskStart=$taskConsole.IndexOf('        private static void QueueReadTracked(',[StringComparison]::Ordinal)
$taskEnd=$taskConsole.IndexOf('        internal static NpV2RecordingResult Run(',$taskStart,[StringComparison]::Ordinal)
if($taskStart-lt0-or$taskEnd-le$taskStart){throw 'Actual queue observer boundary changed'}
$taskActual='using System;using System.ComponentModel;using System.Diagnostics;namespace USB512_interface {internal static class ActualQueue {'+$taskConsole.Substring($taskStart,$taskEnd-$taskStart)+'}}'
[IO.File]::WriteAllText("$taskOutput/ActualQueue.cs",$taskActual,[Text.UTF8Encoding]::new($false))
$taskQueueTest=Join-Path $PSScriptRoot 'Tests/NpV2ReadHistoryQueueTests.cs'
$taskHistory=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface/NpV2ReadHistory.cs'
& $taskCompiler /nologo /langversion:7.3 /warnaserror /r:System.Core.dll /main:NpV2ReadHistoryQueueTests "/out:$taskOutput/Queue.exe" "$taskOutput/ActualQueue.cs" $taskQueueTest $taskHistory
if($LASTEXITCODE){throw 'Actual queue-wrapper boundary fixture compile failed'}
& "$taskOutput/Queue.exe" | Tee-Object -FilePath "$taskOutput/queue-result.txt"
if($LASTEXITCODE){throw 'Actual queue-wrapper one-shot/exception fixture failed'}
$taskSources+=@($taskQueueTest)
$taskPins=@($taskSources)+@($taskProjectPath,$PSCommandPath)|Sort-Object -Unique|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}}
$taskReceipt=[ordered]@{passed=$true;scope='Lab-only bounded host completion history; actual V2 verifier/decoder and SDK-linked RunCore source closure; no device, application build or throughput/clock-offset qualification';language='C#7.3';inputs=$taskPins}
[IO.File]::WriteAllText("$taskOutput/receipt.json",($taskReceipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
