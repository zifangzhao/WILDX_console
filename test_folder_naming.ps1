param(
 [string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),
 [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\folder-naming-tests')
)
$ErrorActionPreference='Stop'
$compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$run=Join-Path $OutputDirectory ('run_'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
foreach($test in @('TestNpFolderNaming','TestNp2MergeRecovery')) {
 $exe=Join-Path $run ($test+'.exe')
 & $compiler /nologo /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$exe" (Join-Path $PSScriptRoot ('Neuropixels\Tests\'+$test+'.cs'))
 if($LASTEXITCODE){throw ($test+' compilation failed.')}
 if($test -eq 'TestNp2MergeRecovery'){& $exe $ConsoleExecutable (Join-Path $run 'recovery') --recovery-only}
 else{& $exe $ConsoleExecutable (Join-Path $run 'naming')}
 if($LASTEXITCODE){throw ($test+' failed.')}
}
