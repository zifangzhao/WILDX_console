param([string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts/sd-sweep-policy-tests'))
$ErrorActionPreference='Stop'
$taskOut=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $taskOut){throw 'Fresh focused output required'}
New-Item -ItemType Directory -Path $taskOut|Out-Null
$taskUi=Join-Path $PSScriptRoot 'USB512_interface/USB512_interface'
$taskInputs=@((Join-Path $taskUi 'Ble/NpV1BleProtocol.cs'),(Join-Path $taskUi 'NpLogger/NpSdGeometry.cs'),(Join-Path $taskUi 'Ble/NpV2SdControl.cs'))
function ActualMethod([string]$Path,[string]$Name){
 $taskText=[IO.File]::ReadAllText($Path)
 $taskMatch=[regex]::Match($taskText,'internal static [\w<>]+ '+[regex]::Escape($Name)+'\([^)]*\)\s*\{')
 if(!$taskMatch.Success){throw "Actual profile method missing: $Name"}
 $taskDepth=1;$taskEnd=$taskMatch.Index+$taskMatch.Length
 while($taskDepth -gt 0 -and $taskEnd -lt $taskText.Length){if($taskText[$taskEnd] -eq '{'){$taskDepth++};if($taskText[$taskEnd] -eq '}'){$taskDepth--};$taskEnd++}
 if($taskDepth -ne 0){throw 'Unbalanced actual profile method'}
 return $taskText.Substring($taskMatch.Index,$taskEnd-$taskMatch.Index)
}
# Compile the exact small production predicate bodies. Do not pull unrelated
# raw-card readers/native transports into this pure policy fixture.
$taskActual='namespace CE32_console { internal static class NpV1BleProtocol {'+(ActualMethod $taskInputs[0] 'TheoreticalMode')+'} internal static class NpSdGeometry {'+(ActualMethod $taskInputs[1] 'Count')+'} internal static class NpV2SdControl {'+(ActualMethod $taskInputs[2] 'ProfileTicks')+'} }'
$taskClosure=Join-Path $taskOut 'ActualProfilePredicates.cs';[IO.File]::WriteAllText($taskClosure,$taskActual)
$taskSources=@((Join-Path $taskUi 'NpSdSweepPolicy.cs'),(Join-Path $PSScriptRoot 'Tests/NpSdSweepPolicyTests.cs'),$taskClosure)
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskExe=Join-Path $taskOut 'NpSdSweepPolicyTests.exe'
$taskArgs=@('/nologo','/warnaserror','/optimize+','/r:System.Core.dll',('/out:'+$taskExe))+$taskSources
$taskPins=@(($taskInputs+$taskSources)|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}})
@{compiler=$taskCompiler;argv=$taskArgs;source_inputs=$taskPins;hardware_calls=0}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskOut 'compile-attempt.json')
$taskLog=& $taskCompiler @taskArgs 2>&1;$taskExit=$LASTEXITCODE;$taskLog|Out-String|Set-Content (Join-Path $taskOut 'compile.log');if($taskExit){$taskLog;throw 'Sweep policy fixture compile failed'}
$taskLog=& $taskExe 2>&1;$taskExit=$LASTEXITCODE;$taskLog|Out-String|Set-Content (Join-Path $taskOut 'run.log');$taskLog;if($taskExit){throw 'Sweep policy fixture failed'}
@{status='PASS_PURE_SOFTWARE_ONLY';source_inputs=$taskPins;fixture_sha256=(Get-FileHash -LiteralPath $taskExe).Hash;run_sha256=(Get-FileHash -LiteralPath (Join-Path $taskOut 'run.log')).Hash;hardware_calls=0;product_builds=0;scope='Exact extracted production profile predicates and new pure sweep policy; no controller/firmware/physical throughput claim'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $taskOut 'receipt.json')
