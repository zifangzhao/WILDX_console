param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot\_artifacts\lfp-time-tests")
$ErrorActionPreference='Stop'
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'TestNpLfpTime.exe'
& $compiler /nologo /warnaserror /optimize+ /r:System.Web.Extensions.dll "/out:$exe" "$PSScriptRoot\Tests\TestNpLfpTime.cs"
if($LASTEXITCODE){throw 'LFP time regression test compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('run-'+[Guid]::NewGuid().ToString('N'))) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface')))
if($LASTEXITCODE){throw 'LFP time regression failed'}
