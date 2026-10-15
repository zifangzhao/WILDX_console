param(
    [string]$ConsoleExecutable=(Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\bin\Release\WILDX_console.exe'),
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '_artifacts\network-time-tests'),
    [int]$LiveSeconds=0,
    [string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$protocol=Join-Path $OutputDirectory 'TestNetworkTime.exe'
& $Compiler /nologo /warnaserror /r:System.Core.dll "/out:$protocol" (Join-Path $PSScriptRoot 'Neuropixels\Tests\TestNetworkTime.cs') (Join-Path $PSScriptRoot 'USB512_interface\USB512_interface\NetworkTimeClock.cs')
if($LASTEXITCODE){throw 'Network time tests did not compile.'}
& $protocol
if($LASTEXITCODE){throw 'Network time regression failed.'}
$ui=Join-Path $OutputDirectory 'TestNetworkTimeUi.exe'
& $Compiler /nologo /warnaserror /r:System.Core.dll /r:System.Configuration.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$ui" (Join-Path $PSScriptRoot 'Neuropixels\Tests\TestNetworkTimeUi.cs')
if($LASTEXITCODE){throw 'Network time UI tests did not compile.'}
& $ui $ConsoleExecutable $OutputDirectory
if($LASTEXITCODE){throw 'Network time UI regression failed.'}
if($LiveSeconds -gt 0){
    & $protocol --live $LiveSeconds
    if($LASTEXITCODE){throw 'Live network time check failed.'}
}
