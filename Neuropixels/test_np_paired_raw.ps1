param([Parameter(Mandatory=$true)][string]$ConsoleExecutable,[string]$OutputDirectory="$PSScriptRoot\_artifacts\paired-raw-tests")
$ErrorActionPreference='Stop'
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe=Join-Path $OutputDirectory 'TestNpPairedRaw.exe'
& $compiler /nologo /warnaserror /optimize+ /r:System.Web.Extensions.dll "/out:$exe" "$PSScriptRoot\Tests\TestNpPairedRaw.cs"
if($LASTEXITCODE){throw 'Paired raw test compilation failed'}
& $exe ([IO.Path]::GetFullPath($ConsoleExecutable)) (Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('run-'+[Guid]::NewGuid().ToString('N'))) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\USB512_interface\USB512_interface')))
if($LASTEXITCODE){throw 'Paired raw test failed'}
