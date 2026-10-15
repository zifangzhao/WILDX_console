param(
    [string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\preview-window-tests'),
    [string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach($name in @('NpPreviewWindowTests','NpUsbUiLifecycleTests')){
    $test=Join-Path $OutputDirectory ($name+'.exe')
    & $Compiler /nologo /warnaserror /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$test" (Join-Path $PSScriptRoot ('Tests\'+$name+'.cs'))
    if($LASTEXITCODE){throw ($name+' did not compile.')}
    & $test $ConsoleExecutable
    if($LASTEXITCODE){throw ($name+' failed.')}
}
