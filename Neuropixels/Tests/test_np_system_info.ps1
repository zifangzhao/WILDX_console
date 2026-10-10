param([Parameter(Mandatory=$true)][string]$ReleaseExe, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseExe).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Use the VS2019 compiler installed for the console build' }
$testExe = Join-Path $output 'TestNpSystemInfo.exe'
& $compiler /nologo /target:exe /langversion:latest /r:System.Windows.Forms.dll /r:System.Drawing.dll ("/out:" + $testExe) (Join-Path $PSScriptRoot 'TestNpSystemInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'System-info test compilation failed' }
& $testExe $release $output
if ($LASTEXITCODE -ne 0) { throw 'System-info checks failed' }
