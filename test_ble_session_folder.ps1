param(
 [Parameter(Mandatory=$true)][string]$ConsoleExecutable,
 [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts/ble-session-folder-tests'),
 [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$taskOut=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $taskOut){throw 'Fresh test output required'}
New-Item -ItemType Directory -Path $taskOut|Out-Null
$taskCompiler='C:/Program Files (x86)/Microsoft Visual Studio/2019/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$taskSource=Join-Path $PSScriptRoot 'Tests/NpBleSessionFolderTests.cs'
$taskExe=Join-Path $taskOut 'NpBleSessionFolderTests.exe'
$taskArgs=@('/nologo','/warnaserror','/platform:x64','/optimize+','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Configuration.dll',('/out:'+$taskExe),$taskSource)
@{compiler=$taskCompiler;argv=$taskArgs;source_sha256=(Get-FileHash -LiteralPath $taskSource).Hash;hardware_calls=0}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $taskOut 'compile-attempt.json')
$taskLog=& $taskCompiler @taskArgs 2>&1;$taskExit=$LASTEXITCODE;$taskLog|Out-String|Set-Content (Join-Path $taskOut 'compile.log')
if($taskExit){$taskLog;throw 'Session-folder fixture compile failed'}
if($CompileOnly){return}
$taskHost=[IO.Path]::GetFullPath($ConsoleExecutable)
$taskLog=& $taskExe $taskHost (Join-Path $taskOut 'run') 2>&1;$taskExit=$LASTEXITCODE;$taskLog|Out-String|Set-Content (Join-Path $taskOut 'run.log');$taskLog
if($taskExit){throw 'Actual-app session folder/GPIO checks failed'}
@{status='PASS';app=$taskHost;app_sha256=(Get-FileHash -LiteralPath $taskHost).Hash;fixture_sha256=(Get-FileHash -LiteralPath $taskExe).Hash;source_sha256=(Get-FileHash -LiteralPath $taskSource).Hash;receipt_sha256=(Get-FileHash -LiteralPath (Join-Path $taskOut 'run/receipt.json')).Hash;hardware_calls=0}|ConvertTo-Json|Set-Content (Join-Path $taskOut 'receipt.json')
